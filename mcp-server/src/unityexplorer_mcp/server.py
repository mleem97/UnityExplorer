"""FastMCP server exposing Unity game inspection tools."""

from __future__ import annotations

import base64
import logging
import os

from fastmcp import FastMCP, Image

from .bridge import GameBridge

logger = logging.getLogger(__name__)

WS_PORT = int(os.environ.get("UNITY_MCP_WS_PORT", "27015"))
TIMEOUT = float(os.environ.get("UNITY_MCP_TIMEOUT", "10"))

bridge = GameBridge(port=WS_PORT)
mcp = FastMCP("unity-explorer", lifespan=bridge.lifespan)


async def _cmd(command: str, params: dict | None = None) -> dict:
    """Send command to game, return result dict or raise on error."""
    resp = await bridge.send_command(command, params, timeout=TIMEOUT)
    if not resp.success:
        raise RuntimeError(resp.error or "Unknown error from game")
    return resp.result or {}


# ── Game Status ──────────────────────────────────────────────────────────────

@mcp.tool()
async def game_status() -> dict:
    """Get the connection status of the Unity game, including game name, Unity version, runtime, and FPS."""
    if not bridge.connected or bridge.identity is None:
        return {"connected": False}

    try:
        dynamic = await _cmd("game_status")
    except Exception:
        dynamic = {}

    return {
        "connected": True,
        "game_name": bridge.identity.game_name,
        "unity_version": bridge.identity.unity_version,
        "explorer_version": bridge.identity.explorer_version,
        "runtime": bridge.identity.runtime,
        "fps": dynamic.get("fps"),
    }


# ── Scene Operations ─────────────────────────────────────────────────────────

@mcp.tool()
async def list_scenes() -> dict:
    """List all loaded scenes and available scenes from build settings."""
    return await _cmd("list_scenes")


@mcp.tool()
async def load_scene(scene_name: str, mode: str = "single") -> dict:
    """Load a scene by name. Mode is 'single' (replaces current) or 'additive' (adds alongside)."""
    return await _cmd("load_scene", {"scene_name": scene_name, "mode": mode})


@mcp.tool()
async def get_scene_objects(scene_name: str | None = None) -> dict:
    """Get root GameObjects in a scene. If scene_name is omitted, uses the currently selected scene."""
    return await _cmd("get_scene_objects", {"scene_name": scene_name})


# ── Hierarchy Navigation ─────────────────────────────────────────────────────

@mcp.tool()
async def get_children(instance_id: int) -> dict:
    """Get the immediate children of a GameObject by its instance ID."""
    return await _cmd("get_children", {"instance_id": instance_id})


@mcp.tool()
async def get_hierarchy(instance_id: int, depth: int = 3) -> dict:
    """Get the full hierarchy tree of a GameObject up to the specified depth."""
    return await _cmd("get_hierarchy", {"instance_id": instance_id, "depth": depth})


# ── Object Search ─────────────────────────────────────────────────────────────

@mcp.tool()
async def search_objects(
    name_filter: str | None = None,
    type_filter: str | None = None,
    scene_filter: str = "any",
    child_filter: str = "any",
    max_results: int = 100,
) -> dict:
    """Search for Unity objects by name and/or type. scene_filter: any|actively_loaded|dont_destroy_on_load|hide_and_dont_save. child_filter: any|root_object|has_parent."""
    return await _cmd("search_objects", {
        "name_filter": name_filter,
        "type_filter": type_filter,
        "scene_filter": scene_filter,
        "child_filter": child_filter,
        "max_results": max_results,
    })


@mcp.tool()
async def search_classes(name_filter: str, max_results: int = 100) -> dict:
    """Search for types/classes by name across all loaded assemblies."""
    return await _cmd("search_classes", {"name_filter": name_filter, "max_results": max_results})


@mcp.tool()
async def search_singletons(type_filter: str, max_results: int = 50) -> dict:
    """Search for singleton instances by type name."""
    return await _cmd("search_singletons", {"type_filter": type_filter, "max_results": max_results})


# ── Component Inspection ─────────────────────────────────────────────────────

@mcp.tool()
async def get_components(instance_id: int) -> dict:
    """List all components on a GameObject by its instance ID."""
    return await _cmd("get_components", {"instance_id": instance_id})


@mcp.tool()
async def inspect_component(
    component_id: int,
    member_filter: str | None = None,
    include_private: bool = False,
) -> dict:
    """Inspect a component's fields, properties, and methods. Use component_id from get_components results."""
    return await _cmd("inspect_component", {
        "component_id": component_id,
        "member_filter": member_filter,
        "include_private": include_private,
    })


@mcp.tool()
async def get_value(component_id: int, member_name: str) -> dict:
    """Get the current value of a specific field or property on a component."""
    return await _cmd("get_value", {"component_id": component_id, "member_name": member_name})


@mcp.tool()
async def set_value(component_id: int, member_name: str, value: object) -> dict:
    """Set a field or property value on a component. Supports primitives, enums (by name or int), and Unity structs (as {x,y,z} objects)."""
    return await _cmd("set_value", {"component_id": component_id, "member_name": member_name, "value": value})


@mcp.tool()
async def invoke_method(
    component_id: int,
    method_name: str,
    args: list | None = None,
    arg_types: list[str] | None = None,
) -> dict:
    """Invoke a method on a component. Use arg_types to disambiguate overloads (e.g. ['string', 'int'])."""
    return await _cmd("invoke_method", {
        "component_id": component_id,
        "method_name": method_name,
        "args": args or [],
        "arg_types": arg_types,
    })


# ── C# REPL ──────────────────────────────────────────────────────────────────

@mcp.tool()
async def execute_csharp(code: str) -> dict:
    """Execute arbitrary C# code in the game's runtime. Returns output, return value, or error. May be unavailable on some IL2CPP builds."""
    return await _cmd("execute_csharp", {"code": code})


# ── Logging ───────────────────────────────────────────────────────────────────

@mcp.tool()
async def get_logs(count: int = 50, log_type: str = "all") -> dict:
    """Get recent game logs. log_type: all|log|warning|error."""
    return await _cmd("get_logs", {"count": count, "log_type": log_type})


# ── Screenshot ────────────────────────────────────────────────────────────────

@mcp.tool()
async def capture_screenshot() -> Image:
    """Capture a screenshot of the current game view."""
    result = await _cmd("capture_screenshot")
    png_bytes = base64.b64decode(result["data"])
    return Image(data=png_bytes, format="png")


# ── Entrypoint ────────────────────────────────────────────────────────────────

def main():
    logging.basicConfig(level=logging.INFO, format="%(name)s: %(message)s")
    mcp.run()
