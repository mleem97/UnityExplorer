"""Shared protocol types for communication between MCP server and Unity game."""

from __future__ import annotations

import json
import uuid
from dataclasses import dataclass, field, asdict
from typing import Any


@dataclass
class CommandRequest:
    """Server → Game command."""
    command: str
    params: dict[str, Any] = field(default_factory=dict)
    id: str = field(default_factory=lambda: str(uuid.uuid4()))

    def to_json(self) -> str:
        return json.dumps(asdict(self))


@dataclass
class CommandResponse:
    """Game → Server response."""
    id: str
    success: bool
    result: dict[str, Any] | None = None
    error: str | None = None

    @classmethod
    def from_json(cls, data: str) -> CommandResponse:
        parsed = json.loads(data)
        return cls(
            id=parsed["id"],
            success=parsed["success"],
            result=parsed.get("result"),
            error=parsed.get("error"),
        )


@dataclass
class GameIdentity:
    """Game → Server identity message sent on connect."""
    protocol_version: int
    game_name: str
    unity_version: str
    explorer_version: str
    runtime: str

    @classmethod
    def from_dict(cls, data: dict) -> GameIdentity:
        return cls(
            protocol_version=data["protocol_version"],
            game_name=data["game_name"],
            unity_version=data["unity_version"],
            explorer_version=data["explorer_version"],
            runtime=data["runtime"],
        )
