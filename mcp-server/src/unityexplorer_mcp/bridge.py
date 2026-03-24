"""WebSocket server that manages the connection to a Unity game."""

from __future__ import annotations

import asyncio
import json
import logging
import time
from contextlib import asynccontextmanager

import websockets.server

from .protocol import CommandRequest, CommandResponse, GameIdentity

logger = logging.getLogger(__name__)

HEARTBEAT_TIMEOUT = 15.0  # seconds without heartbeat = disconnected


class GameBridge:
    """Manages WebSocket connection to a single Unity game instance."""

    def __init__(self, port: int = 27015):
        self.port = port
        self._connection: websockets.server.ServerConnection | None = None
        self._identity: GameIdentity | None = None
        self._pending: dict[str, asyncio.Future[CommandResponse]] = {}
        self._server: websockets.server.WebSocketServer | None = None
        self._last_heartbeat: float = 0.0

    @property
    def connected(self) -> bool:
        return self._connection is not None and not self._connection.close_code

    @property
    def identity(self) -> GameIdentity | None:
        return self._identity

    @property
    def heartbeat_stale(self) -> bool:
        if not self.connected:
            return True
        return (time.monotonic() - self._last_heartbeat) > HEARTBEAT_TIMEOUT

    async def start(self):
        """Start the WebSocket server in the background."""
        self._server = await websockets.server.serve(
            self._handle_connection,
            "localhost",
            self.port,
        )
        logger.info(f"WebSocket server listening on ws://localhost:{self.port}")

    async def stop(self):
        """Stop the WebSocket server."""
        if self._server:
            self._server.close()
            await self._server.wait_closed()

    @asynccontextmanager
    async def lifespan(self):
        """Context manager for use with FastMCP lifespan — starts/stops the bridge."""
        await self.start()
        try:
            yield
        finally:
            await self.stop()

    async def send_command(
        self, command: str, params: dict | None = None, timeout: float = 10.0
    ) -> CommandResponse:
        """Send a command to the game and wait for the response."""
        if not self.connected:
            raise ConnectionError("No game is currently connected to the MCP server.")

        if self.heartbeat_stale:
            raise ConnectionError(
                "Game appears to be unresponsive (no heartbeat received). "
                "It may be frozen or have disconnected."
            )

        request = CommandRequest(command=command, params=params or {})
        loop = asyncio.get_running_loop()
        future: asyncio.Future[CommandResponse] = loop.create_future()
        self._pending[request.id] = future

        try:
            await self._connection.send(request.to_json())
            return await asyncio.wait_for(future, timeout=timeout)
        except asyncio.TimeoutError:
            raise TimeoutError(
                f"Game did not respond to '{command}' within {timeout}s. "
                "The game may be frozen or processing a heavy operation."
            )
        finally:
            self._pending.pop(request.id, None)

    async def _handle_connection(self, websocket: websockets.server.ServerConnection):
        """Handle a new game connection."""
        if self.connected:
            logger.warning("Rejecting new connection: a game is already connected.")
            await websocket.close(1008, "Another game is already connected")
            return

        self._connection = websocket
        self._last_heartbeat = time.monotonic()
        logger.info(f"Game connected from {websocket.remote_address}")

        try:
            async for raw_message in websocket:
                try:
                    message = json.loads(raw_message)
                except json.JSONDecodeError:
                    logger.warning(f"Received invalid JSON: {raw_message[:200]}")
                    continue

                msg_type = message.get("type")

                if msg_type == "identity":
                    self._identity = GameIdentity.from_dict(message)
                    self._last_heartbeat = time.monotonic()
                    logger.info(
                        f"Game identified: {self._identity.game_name} "
                        f"(Unity {self._identity.unity_version}, "
                        f"{self._identity.runtime})"
                    )
                elif msg_type == "heartbeat":
                    self._last_heartbeat = time.monotonic()
                elif "id" in message:
                    # Command response
                    response = CommandResponse.from_json(raw_message)
                    future = self._pending.get(response.id)
                    if future and not future.done():
                        future.set_result(response)
                    else:
                        logger.warning(f"Received response for unknown command ID: {response.id}")
                else:
                    logger.warning(f"Unknown message type: {message}")

        except websockets.ConnectionClosed:
            logger.info("Game disconnected.")
        finally:
            # Fail all pending commands
            for future in self._pending.values():
                if not future.done():
                    future.set_exception(
                        ConnectionError("Game disconnected while command was pending.")
                    )
            self._pending.clear()
            self._connection = None
            self._identity = None
