from fastmcp import FastMCP
import requests

PORT = 15527
mcp = FastMCP("STS2 MCP")

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
    

if __name__ == "__main__":
    mcp.run(transport="http", host="0.0.0.0", port=8005)