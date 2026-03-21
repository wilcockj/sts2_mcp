from fastmcp import FastMCP
import requests

PORT = 15527

mcp = FastMCP("STS2 MCP")

@mcp.tool
def play_card(card_index: int, target_index: int | None = None):
    """
    Play a card based on its card_index (0 indexed).
    If the card affects an enemy (damage, debuf, etc.) then you must set a target_index (also 0 indexed).
    """
    url = f"http://localhost:{PORT}/api/v1/playcard"
    try:
        payload = {
            "card_index": card_index
        }

        if target_index is not None:
            payload["target_index"] = target_index
            
        response = requests.post(url, timeout=2, json=payload)
        response.raise_for_status()
        return response.json()
    except Exception as e:
        return f"Failed to play card: {e}"

@mcp.tool
def player_state():
    """Gets the player's current state"""
    url = f"http://localhost:{PORT}/api/v1/player"
    try:
        response = requests.get(url, timeout=2)
        response.raise_for_status()
        return response.json()
    except Exception as e:
        return f"Failed to get player state: {e}"
        
@mcp.tool
def get_enemy_info():
    """Get the current enemies and their info"""
    url = f"http://localhost:{PORT}/api/v1/enemies"
    try:
        response = requests.get(url, timeout=2)
        response.raise_for_status()
        return response.json()
    except Exception as e:
        return f"Failed to get enemies state: {e}"
    
def health() -> str:
    """gets the players health"""
    response = requests.get(f"http://localhost:{PORT}/health")
    return response.text
    
@mcp.tool
def sts2_get_map():
    """Gets the current act map and recommends two paths to the boss:
    - SafestPath: fewest fights (avoids Monster, Elite, Unknown nodes)
    - MostAggressivePath: maximizes rewards (favors Elite, Treasure, Monster, Unknown nodes)
    Returns the node sequence for each recommended path."""
    try:
        response = requests.get(f"http://localhost:{PORT}/api/v1/map", timeout=2)
        response.raise_for_status()
        return response.json()
    except Exception as e:
        return f"Failed to get map: {e}"
        
@mcp.tool
def end_turn():
    """Ends the player's current turn in combat"""
    try:
        response = requests.get(f"http://localhost:{PORT}/api/v1/end_turn", timeout=2)
        response.raise_for_status()
        return response.json()
    except Exception as e:
        return f"Failed to end turn: {e}"

@mcp.tool
def select_character(character: str):
    """Select a character and start the game. Must be on the character select screen first.
    Pass the character ID (e.g. 'IRONCLAD'). Returns available characters if the ID is not found."""
    try:
        response = requests.post(f"http://localhost:{PORT}/api/v1/select_character",
                                 timeout=2, json={"character": character})
        response.raise_for_status()
        return response.json()
    except Exception as e:
        return f"Failed to select character: {e}"

@mcp.tool
def get_to_character_select():
    """Gets you to the character select screen so you can
    choose your chracter and then start the game"""
    try:
        response = requests.get(f"http://localhost:{PORT}/api/v1/enter_char_select", timeout=2)
        response.raise_for_status()
        return response.json()
    except Exception as e:
        return f"Failed to get to chracter select screen: {e}"
        
if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--stdio", action="store_true", help="Run as stdio instead of HTTP server")
    args = parser.parse_args()

    if args.stdio:
        mcp.run()
    else:
        mcp.run(transport="http", host="0.0.0.0", port=8005)