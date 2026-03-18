# STS2 MCP

A Slay the Spire 2 mod that enables Model Context Protocol (MCP) server functionality.

## Requirements

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [MegaDot](https://megadot.megacrit.com/) (Godot editor for mod development)
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

### Windows
```powershell
.\build.ps1
```

### Linux/macOS
```bash
./build.sh
```

Add `-Run` to build and launch the game:
```powershell
# Windows
.\build.ps1 -Run
```

```bash
# Linux/macOS
./build.sh -Run
```

This will compile the mod and copy the output to:
`{STEAM_PATH}/Slay the Spire 2/mods/sts2mcp/`

## Running the Mod

1. Build the mod using the script for your platform
2. Launch Slay the Spire 2 through Steam
3. The mod will be automatically loaded

### Linux Debug Launch

To enable Harmony debugging on Linux, use the `-Run` flag or add to Steam launch options:
```
LD_PRELOAD=/lib/libgcc_s.so.1 HARMONY_DEBUG="true" %command% --remote-debug tcp://127.0.0.1:6007 --nomods
```