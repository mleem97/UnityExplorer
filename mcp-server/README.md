# UnityExplorer MCP Server

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io) server that lets Claude Code (or any MCP client) programmatically inspect and interact with Unity games running [UnityExplorer](https://github.com/sinai-dev/UnityExplorer).

## How It Works

```
Claude Code ←(stdio MCP)→ FastMCP Server (Python) ←(WebSocket)→ UnityExplorer (in game)
```

The Python server acts as a persistent middleman. Any game running UnityExplorer auto-connects via WebSocket on `localhost:27015`. Claude Code talks to the Python server, which relays commands to the game and returns results.

## Setup

### 1. Install UnityExplorer with MCP Support

Download the appropriate build for your mod loader from the [Releases](https://github.com/DrDraxi/UnityExplorer/releases) page and install normally:

- **BepInEx**: Copy DLL to `BepInEx/plugins/`
- **MelonLoader**: Copy DLL to `Mods/`, libs to `UserLibs/`
- **Standalone**: Inject directly

### 2. Configure Claude Code

Requires [uv](https://docs.astral.sh/uv/getting-started/installation/) installed.

```bash
claude mcp add --transport stdio unity-explorer \
  --scope project \
  --env UNITY_MCP_WS_PORT=27015 \
  -- uv run --directory /path/to/UnityExplorer/mcp-server server
```

Or manually add to `.mcp.json` in your project root:

```json
{
  "mcpServers": {
    "unity-explorer": {
      "type": "stdio",
      "command": "uv",
      "args": ["run", "--directory", "/path/to/UnityExplorer/mcp-server", "server"],
      "env": { "UNITY_MCP_WS_PORT": "27015" }
    }
  }
}
```

### 3. Launch the Game

Start any game with UnityExplorer installed. It will auto-connect to the MCP server within a few seconds. Press **F7** to toggle the UnityExplorer UI.

## Available Tools

| Tool | Description |
|------|-------------|
| `game_status` | Connection status, game name, Unity version, FPS |
| `list_scenes` | All loaded and available scenes |
| `load_scene` | Load a scene (single or additive) |
| `get_scene_objects` | Root GameObjects in a scene |
| `get_children` | Children of a GameObject |
| `get_hierarchy` | Full tree dump with configurable depth |
| `search_objects` | Find objects by name/type with filters |
| `search_classes` | Find types across loaded assemblies |
| `search_singletons` | Find singleton instances |
| `get_components` | List components on a GameObject |
| `inspect_component` | Read fields, properties, methods via reflection |
| `get_value` | Get a specific field/property value |
| `set_value` | Set a field/property (primitives, enums, Vector3, Color, etc.) |
| `invoke_method` | Call a method with argument type disambiguation |
| `execute_csharp` | Run arbitrary C# in the game's REPL |
| `get_logs` | Recent game logs with type filtering |
| `capture_screenshot` | Screenshot of the current game view |

## Configuration

UnityExplorer settings (press F7 in-game, go to Options):

| Setting | Default | Description |
|---------|---------|-------------|
| MCP Server URL | `ws://localhost:27015` | WebSocket URL to connect to |
| MCP Auto Connect | `true` | Connect on startup |
| MCP Reconnect Delay | `5.0` | Seconds between reconnection attempts |

Environment variables for the Python server:

| Variable | Default | Description |
|----------|---------|-------------|
| `UNITY_MCP_WS_PORT` | `27015` | WebSocket server port |
| `UNITY_MCP_TIMEOUT` | `10` | Command timeout in seconds |

## Compatibility

Works with all UnityExplorer build variants:
- Unity 5.2 through 2022+ (Mono and IL2CPP)
- BepInEx 5, BepInEx 6, MelonLoader, Standalone
- net35, net472, net6 targets
