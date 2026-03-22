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

### TODO 

- Get optimal paths and tell the agent which ones they can pick, might need to change path based on 
  how the fights go. For example if you lost a lot of health may need to go for a campfire early
  need to search from the current spot up to the boss and give a best aggressive and best safe route,
  maybe do this every time you enter a room
- add getting the cards that you currently hold.
- add getting current fight state
- add control for starting the run and then selecting a character
- add control for restarting a run
- control to get what is in the shop currently / choose selection
- acknowledge for whether the card selected is correct before playing?
- a way to tell the mcp it is not its turn yet
- need to be able to select cards, for example when a card requires discarding