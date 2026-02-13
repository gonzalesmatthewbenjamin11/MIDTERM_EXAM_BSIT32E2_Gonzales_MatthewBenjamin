const MODE = import.meta.env.VITE_APP_MODE;
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || "https://localhost:7264/api";

const handleResponse = async (response) => {
    if (!response.ok) {
        const text = await response.text();
        throw new Error(text || "API request failed");
    }
    return await response.json();
};

export const createGame = async (playerNames) => {
    if (MODE !== "LIVE") {
        throw new Error("App is not in LIVE mode");
    }

    const response = await fetch(`${API_BASE_URL}/game`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(playerNames),
    });

    return handleResponse(response);
};

export const getGame = async (gameId) => {
    if (MODE !== "LIVE") {
        throw new Error("App is not in LIVE mode");
    }

    const response = await fetch(`${API_BASE_URL}/game/${gameId}`);
    return handleResponse(response);
};

export const rollBall = async (gameId, playerId, pins) => {
    if (MODE !== "LIVE") {
        throw new Error("App is not in LIVE mode");
    }

    const response = await fetch(`${API_BASE_URL}/game/${gameId}/roll`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ playerId, pins }),
    });

    return handleResponse(response);
};
