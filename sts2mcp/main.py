from fastmcp import FastMCP
import requests

PORT = 15527

mcp = FastMCP("STS2 MCP")

@mcp.tool
def health() -> str:
    """gets the players health"""
    response = requests.get(f"http://localhost:{PORT}/health")
    return response.text

if __name__ == "__main__":
    mcp.run()
