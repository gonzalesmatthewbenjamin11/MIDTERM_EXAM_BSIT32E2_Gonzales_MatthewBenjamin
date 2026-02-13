using BowlingApp.API.Data;
using BowlingApp.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BowlingApp.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GameController : ControllerBase
    {
        private readonly BowlingContext _context;

        public GameController(BowlingContext context)
        {
            _context = context;
        }

        // POST: api/Game
        // Create a new game with players
        [HttpPost]
        public async Task<ActionResult<Game>> CreateGame([FromBody] List<string> playerNames)
        {
            // STUDENT TODO:
            // 1. Create a new Game entity.
            // 2. Create Player entities for each name provided.
            // 3. (Optional) Initialize 10 empty Frames for each player to simplify logic?
            // 4. Save to Database using _context.
            // 5. Return the created Game object (including Players).

            if (playerNames == null || playerNames.Count == 0)
                return BadRequest("At least 1 player is required.");

            if (playerNames.Count > 4)
                return BadRequest("Maximum of 4 players only.");

            var cleanedNames = playerNames
                .Select(n => (n ?? "").Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            if (cleanedNames.Count == 0)
                return BadRequest("Player names cannot be empty.");

            // 1. Create a new Game entity.
            var game = new Game
            {
                IsFinished = false,
                Players = new List<Player>()
            };

            // 2. Create Player entities for each name provided.
            foreach (var name in cleanedNames)
            {
                game.Players.Add(new Player
                {
                    Name = name
                });
            }

            // 4. Save to Database using _context. (Save game+players first so players get IDs)
            _context.Games.Add(game);
            await _context.SaveChangesAsync();

            // 3. Initialize 10 empty Frames for each player (sets PlayerId explicitly)
            foreach (var player in game.Players)
            {
                var frames = new List<Frame>();
                for (int i = 1; i <= 10; i++)
                {
                    frames.Add(new Frame
                    {
                        FrameNumber = i,
                        Roll1 = null,
                        Roll2 = null,
                        Roll3 = null,
                        Score = null,
                        PlayerId = player.Id
                    });
                }

                _context.Frames.AddRange(frames);
            }

            await _context.SaveChangesAsync();

            // 5. Return the created Game object (including Players).
            var created = await _context.Games
                .Include(g => g.Players)
                .ThenInclude(p => p.Frames)
                .FirstAsync(g => g.Id == game.Id);

            foreach (var p in created.Players)
                p.Frames = p.Frames.OrderBy(f => f.FrameNumber).ToList();

            return Ok(created);
        }

        // GET: api/Game/5
        // Get game details and current scores
        [HttpGet("{id}")]
        public async Task<ActionResult<Game>> GetGame(int id)
        {
            // STUDENT TODO:
            // 1. Find the Game by ID.
            // 2. Include Players and Frames in the query (use .Include()).
            // 3. Return the Game.

            var game = await _context.Games
                .Include(g => g.Players)
                .ThenInclude(p => p.Frames)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (game == null) return NotFound();

            foreach (var p in game.Players)
                p.Frames = p.Frames.OrderBy(f => f.FrameNumber).ToList();

            return Ok(game);
        }

        // POST: api/Game/5/roll
        // Record a roll for a specific player
        [HttpPost("{gameId}/roll")]
        public async Task<IActionResult> Roll(int gameId, [FromBody] RollRequest request)
        {
            // STUDENT TODO:
            // 1. Find the Player and Game.
            // 2. Determine the Current Frame for the player (the first incompletion frame).
            // 3. Update the Frame with the rolled pins (Roll1, Roll2, or Roll3).
            // 4. BOWLING LOGIC:
            //    - Check for Strikes (10 on 1st roll) -> Mark frame as Strike.
            //    - Check for Spares (Total 10 on 2 rolls) -> Mark frame as Spare.
            // 5. SCORING CALCULATION:
            //    - Update the score for the current frame.
            //    - CRITICAL: Check *previous* frames. If they were strikes/spares, they might need this new roll to calculate their final score!
            // 6. Save changes to Database.

            if (request == null) return BadRequest("Request body required.");
            if (request.Pins < 0 || request.Pins > 10) return BadRequest("Pins must be between 0 and 10.");

            // 1. Find the Player and Game.
            var game = await _context.Games
                .Include(g => g.Players)
                .ThenInclude(p => p.Frames)
                .FirstOrDefaultAsync(g => g.Id == gameId);

            if (game == null) return NotFound("Game not found.");

            var player = game.Players.FirstOrDefault(p => p.Id == request.PlayerId);
            if (player == null) return NotFound("Player not found in this game.");

            player.Frames = player.Frames.OrderBy(f => f.FrameNumber).ToList();

            if (IsPlayerFinished(player))
                return BadRequest("This player already finished the game.");

            // 2. Determine the Current Frame for the player
            var frame = GetCurrentFrame(player);
            if (frame == null)
                return BadRequest("No available frame found.");

            // 3 & 4. Update the Frame + apply bowling logic (strike/spare detection by math)
            var apply = ApplyRoll(frame, request.Pins);
            if (!apply.Success)
                return BadRequest(apply.Error);

            // 5. SCORING CALCULATION (recalculate all frames so strike/spare bonuses update)
            RecalculateScores(player);

            // Update game finished flag if all players complete
            game.IsFinished = game.Players.All(IsPlayerFinished);

            // 6. Save changes to Database.
            await _context.SaveChangesAsync();

            // Return updated game state (useful for frontend)
            var updated = await _context.Games
                .Include(g => g.Players)
                .ThenInclude(p => p.Frames)
                .FirstAsync(g => g.Id == gameId);

            foreach (var p in updated.Players)
                p.Frames = p.Frames.OrderBy(f => f.FrameNumber).ToList();

            return Ok(updated);
        }

        // -----------------------------
        // Helper Methods
        // -----------------------------

        private static Frame? GetCurrentFrame(Player player)
        {
            // Frames 1-9: incomplete if Roll1 is null OR (not strike and Roll2 is null)
            foreach (var f in player.Frames.Where(f => f.FrameNumber < 10).OrderBy(f => f.FrameNumber))
            {
                if (f.Roll1 == null) return f;
                if (f.Roll1 != 10 && f.Roll2 == null) return f;
            }

            // 10th: special completion logic
            var tenth = player.Frames.FirstOrDefault(f => f.FrameNumber == 10);
            if (tenth == null) return null;

            if (!IsTenthComplete(tenth)) return tenth;

            return null;
        }

        private static bool IsPlayerFinished(Player player)
        {
            var tenth = player.Frames.FirstOrDefault(f => f.FrameNumber == 10);
            return tenth != null && IsTenthComplete(tenth);
        }

        private static bool IsTenthComplete(Frame f)
        {
            if (f.Roll1 == null) return false;
            if (f.Roll2 == null) return false;

            int r1 = f.Roll1.Value;
            int r2 = f.Roll2.Value;

            // Strike or spare => needs Roll3
            if (r1 == 10) return f.Roll3 != null;
            if (r1 + r2 == 10) return f.Roll3 != null;

            // Open => complete after 2 rolls
            return true;
        }

        private sealed class ApplyRollResult
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
            public static ApplyRollResult Ok() => new() { Success = true };
            public static ApplyRollResult Fail(string error) => new() { Success = false, Error = error };
        }

        private static ApplyRollResult ApplyRoll(Frame frame, int pins)
        {
            // Frames 1-9
            if (frame.FrameNumber < 10)
            {
                if (frame.Roll1 == null)
                {
                    frame.Roll1 = pins;

                    // Strike ends frame immediately (Roll2 stays null)
                    return ApplyRollResult.Ok();
                }

                if (frame.Roll2 == null)
                {
                    int r1 = frame.Roll1.Value;

                    if (r1 == 10)
                        return ApplyRollResult.Fail("Strike frame already complete.");

                    if (r1 + pins > 10)
                        return ApplyRollResult.Fail("Total pins in a frame cannot exceed 10.");

                    frame.Roll2 = pins;
                    return ApplyRollResult.Ok();
                }

                return ApplyRollResult.Fail("Frame already complete.");
            }

            // 10th frame
            if (frame.Roll1 == null)
            {
                frame.Roll1 = pins;
                return ApplyRollResult.Ok();
            }

            if (frame.Roll2 == null)
            {
                int r1 = frame.Roll1.Value;

                // If first roll isn't strike, first two cannot exceed 10
                if (r1 != 10 && r1 + pins > 10)
                    return ApplyRollResult.Fail("Total pins in 10th frame (first two rolls) cannot exceed 10 unless first is a strike.");

                frame.Roll2 = pins;
                return ApplyRollResult.Ok();
            }

            // Roll3 only allowed if strike or spare in 10th
            if (frame.Roll3 == null)
            {
                int r1 = frame.Roll1.Value;
                int r2 = frame.Roll2.Value;

                bool allowThird = (r1 == 10) || (r1 + r2 == 10);
                if (!allowThird)
                    return ApplyRollResult.Fail("Roll 3 is only allowed in 10th frame if you scored a strike or spare.");

                // After strike in 10th: if roll2 isn't strike, roll2+roll3 <= 10
                if (r1 == 10 && r2 != 10 && r2 + pins > 10)
                    return ApplyRollResult.Fail("In 10th frame after a strike, if roll2 isn't a strike, roll2 + roll3 cannot exceed 10.");

                frame.Roll3 = pins;
                return ApplyRollResult.Ok();
            }

            return ApplyRollResult.Fail("10th frame already complete.");
        }

        private static void RecalculateScores(Player player)
        {
            var frames = player.Frames.OrderBy(f => f.FrameNumber).ToList();

            // Flatten all rolls in bowling order
            var rolls = new List<int>();
            foreach (var f in frames)
            {
                if (f.Roll1 != null) rolls.Add(f.Roll1.Value);
                if (f.Roll2 != null) rolls.Add(f.Roll2.Value);
                if (f.FrameNumber == 10 && f.Roll3 != null) rolls.Add(f.Roll3.Value);
            }

            // frame -> starting roll index
            var startIndex = new Dictionary<int, int>();
            int idx = 0;

            foreach (var f in frames)
            {
                startIndex[f.FrameNumber] = idx;

                if (f.FrameNumber < 10)
                {
                    if (f.Roll1 == null) { }
                    else if (f.Roll1 == 10) idx += 1;
                    else if (f.Roll2 != null) idx += 2;
                    else idx += 1;
                }
                else
                {
                    if (f.Roll1 != null) idx++;
                    if (f.Roll2 != null) idx++;
                    if (f.Roll3 != null) idx++;
                }
            }

            // Cumulative scoring
            int running = 0;
            bool blocked = false;

            foreach (var f in frames)
            {
                if (blocked)
                {
                    f.Score = null;
                    continue;
                }

                var pts = CalculateFramePoints(frames, rolls, startIndex, f.FrameNumber);
                if (pts == null)
                {
                    f.Score = null;
                    blocked = true;
                    continue;
                }

                running += pts.Value;
                f.Score = running;
            }
        }

        private static int? CalculateFramePoints(
            List<Frame> frames,
            List<int> rolls,
            Dictionary<int, int> startIndex,
            int frameNumber)
        {
            var f = frames.First(x => x.FrameNumber == frameNumber);

            if (f.Roll1 == null) return null;

            if (frameNumber < 10)
            {
                int start = startIndex[frameNumber];
                int r1 = f.Roll1.Value;

                // Strike
                if (r1 == 10)
                {
                    if (start + 2 >= rolls.Count) return null;
                    return 10 + rolls[start + 1] + rolls[start + 2];
                }

                if (f.Roll2 == null) return null;
                int r2 = f.Roll2.Value;

                // Spare
                if (r1 + r2 == 10)
                {
                    if (start + 2 >= rolls.Count) return null;
                    return 10 + rolls[start + 2];
                }

                // Open
                return r1 + r2;
            }

            // 10th
            if (f.Roll2 == null) return null;

            int t1 = f.Roll1.Value;
            int t2 = f.Roll2.Value;

            // Strike or spare => need Roll3
            if (t1 == 10 || (t1 + t2 == 10))
            {
                if (f.Roll3 == null) return null;
                return t1 + t2 + f.Roll3.Value;
            }

            return t1 + t2;
        }
    }

    public class RollRequest
    {
        public int PlayerId { get; set; }
        public int Pins { get; set; }
    }
}
