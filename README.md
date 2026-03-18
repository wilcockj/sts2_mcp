# STS2 MCP

A Slay the Spire 2 mod that enables Model Context Protocol (MCP) server functionality.

## Requirements

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Godot 4.x](https://godotengine.org/) (for intellisense/development)
- Slay the Spire 2 (installed via Steam)

## Setup

1. **Install .NET 9.0 SDK** from the link above

2. **Set game path** (if not using default Steam install locations):
   - Create a `local.props` file in the project root
   - Add your game path:
   ```xml
   <Project>
     <PropertyGroup>
       <STS2GamePath>C:/path/to/Slay the Spire 2</STS2GamePath>
     </PropertyGroup>
   </Project>
   ```

3. **Restore packages**:
   ```bash
   dotnet restore
   ```

## Build

```bash
dotnet build
```

This will compile the mod and copy the output to:
`{STEAM_PATH}/Slay the Spire 2/mods/sts2mcp/`

## Running the Mod

1. Build the mod as shown above
2. Launch Slay the Spire 2 through Steam
3. The mod will be automatically loaded

### Linux/MacOS Debug Launch

To enable Harmony debugging on Linux:
```bash
LD_PRELOAD=/lib/libgcc_s.so.1 HARMONY_DEBUG="true" %command% --remote-debug tcp://127.0.0.1:6007 --nomods
```
Add this to your Steam launch options for the game.