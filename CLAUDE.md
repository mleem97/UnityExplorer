# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

UnityExplorer is an in-game UI for exploring, debugging, and modifying Unity games at runtime. It supports Unity 5.2 through 2021+ across both IL2CPP and Mono runtimes, and ships as plugins for BepInEx (5 & 6), MelonLoader, or as a standalone injector.

## Build Commands

```powershell
# Full build (all 9 configurations + packaging)
./build.ps1

# Single configuration build
dotnet build src/UnityExplorer.sln -c <Configuration>
```

**Configurations:** `BIE_Cpp`, `BIE_Cpp_CoreCLR`, `BIE5_Mono`, `BIE6_Mono`, `ML_Cpp_net6`, `ML_Cpp_net472`, `ML_Mono`, `STANDALONE_Mono`, `STANDALONE_Cpp`

CI prefix `-noci` in commit messages skips the GitHub Actions build.

There is no automated test suite. The `src/Tests/` directory contains manual experimentation code only.

## Architecture

### Loader Abstraction

Each mod loader has an adapter implementing `IExplorerLoader` (in `src/Loader/`):
- **BepInEx:** `ExplorerBepInPlugin` — entry via `Awake()` (BIE5) or `Load()` (BIE6)
- **MelonLoader:** `ExplorerMelonMod` — entry via `OnApplicationStart()`
- **Standalone:** `ExplorerStandalone` — entry via `CreateInstance()`

All loaders call `ExplorerCore.Init(loader)` which initializes config, UniverseLib, and schedules a delayed `LateInit` (default 1s) that sets up the UI.

### Conditional Compilation

Code is conditionalized with `#if` directives using these symbols:

| Symbol | Meaning |
|--------|---------|
| `CPP` | IL2CPP runtime |
| `MONO` | Mono runtime |
| `UNHOLLOWER` | Il2CppAssemblyUnhollower (older IL2CPP) |
| `INTEROP` | Il2CppInterop (CoreCLR IL2CPP) |
| `BIE` / `BIE5` / `BIE6` | BepInEx loader |
| `ML` | MelonLoader |
| `STANDALONE` | Standalone loader |

Most features must handle both `CPP` and `MONO` paths. When adding code that touches IL2CPP types, wrap it in the appropriate `#if` block.

### Target Frameworks

- `net35` — Mono builds (BIE5_Mono, BIE6_Mono, ML_Mono, STANDALONE_Mono)
- `net472` — IL2CPP Unhollower builds (BIE_Cpp, ML_Cpp_net472, STANDALONE_Cpp)
- `net6` — CoreCLR builds (BIE_Cpp_CoreCLR, ML_Cpp_net6)

### Key Subsystems (all under `src/`)

- **CacheObject/** — Reflection caching layer that wraps fields, properties, and methods for the inspector UI. `IValues/` contains interactive value editors.
- **Inspectors/** — Object and type inspection. `InspectorManager` is the public API (`InspectorManager.Inspect(object)` / `InspectorManager.Inspect(Type)`). `MouseInspectors/` handles world/UI picking.
- **ObjectExplorer/** — Scene hierarchy browser (`SceneExplorer`, `SceneHandler`) and runtime object search (`ObjectSearch`, `SearchProvider`).
- **CSConsole/** — C# REPL console using Mono.CSharp (`mcs.dll`). `ScriptEvaluator` wraps the compiler; `CSAutoCompleter` provides intellisense.
- **Hooks/** — UI for creating Harmony method hooks at runtime.
- **UI/** — `UIManager` coordinates panels. Individual panels live in `UI/Panels/`, reusable widgets in `UI/Widgets/`.
- **Config/** — `ConfigManager` registers `ConfigElement<T>` entries with change callbacks. Each loader has its own `ConfigHandler` implementation.
- **Runtime/** — `UERuntimeHelper` with `Il2CppHelper`/`MonoHelper` subclasses for runtime-specific reflection. `UnityCrashPrevention` patches known crash sources.

### Core Dependencies

- **UniverseLib** — UI framework and runtime abstraction (separate NuGet packages per runtime: `UniverseLib.Mono`, `UniverseLib.IL2CPP.Unhollower`, `UniverseLib.IL2CPP.Interop`)
- **HarmonyX** — Method patching/hooking
- **Samboy063.Tomlet** — TOML config parsing
- **mcs.dll** (Mono.CSharp) — C# REPL evaluator (in `lib/`)

### Build Pipeline

`build.ps1` runs `dotnet build` for each configuration, then uses `lib/ILRepack.exe` to merge dependencies (mcs.dll, Tomlet.dll) into a single output DLL per variant. Output goes to `Release/<variant>/` with loader-appropriate directory structure (plugins/, Mods/, UserLibs/).

### Global Usings

Defined in `src/ExplorerCore.cs`: `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Reflection`, `UnityEngine`, `UnityEngine.UI`, `UniverseLib`, `UniverseLib.Utility`. These are available in all source files.

## MCP Server

UnityExplorer includes an MCP (Model Context Protocol) server for programmatic game inspection from Claude Code or other MCP clients.

### Setup

1. Ensure `uv` is installed
2. Add to `.claude/mcp.json`:
```json
{
  "mcpServers": {
    "unity-explorer": {
      "command": "uv",
      "args": ["run", "--directory", "/path/to/UnityExplorer/mcp-server", "server"],
      "env": { "UNITY_MCP_WS_PORT": "27015" }
    }
  }
}
```
3. Launch a game with UnityExplorer installed — it auto-connects to the MCP server

### Architecture

`mcp-server/` contains the Python FastMCP server (middleman). `src/MCP/` contains the C# WebSocket client that runs inside the game. Communication uses JSON over WebSocket on `localhost:27015`.

The MCP module uses direct reflection for inspection (not `CacheMemberFactory`), manual JSON via `JsonHelper` (no external JSON lib), and raw TCP WebSocket framing (no `ClientWebSocket`) for net35 compatibility.

### Build Note

Solution configurations are prefixed with `Release_` (e.g., `dotnet build src/UnityExplorer.sln -c Release_BIE5_Mono`).
