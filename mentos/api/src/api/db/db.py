"""File: db.py
Description: Sign requests and call the internal database REST API.
Version: 1.0.0
"""

from __future__ import annotations

import logging
import os
import re
import time
from pathlib import Path
from typing import Any
from urllib.parse import quote

import jwt
import requests


CONFIG_PATH = Path(__file__).with_name(".env.config")
PROJECT_CONFIG_PATH = Path(__file__).resolve().parents[3] / ".env"
_legacy_dbserv_url_warning_emitted = False


def _configured_dbserv_url(config: dict[str, str]) -> str:
    global _legacy_dbserv_url_warning_emitted
    configured_url = os.getenv("DBSERV_URL") or config.get("DBSERV_URL")
    if configured_url:
        return configured_url

    legacy_url = os.getenv("HOST") or config.get("HOST", "")
    if legacy_url and not _legacy_dbserv_url_warning_emitted:
        logging.getLogger(__name__).warning(
            "HOST is deprecated for the DBSERV origin; use DBSERV_URL instead."
        )
        _legacy_dbserv_url_warning_emitted = True
    return legacy_url


def _read_config() -> dict[str, str]:
    config: dict[str, str] = {}
    # Preserve the legacy file while allowing project-local api/.env settings.
    for path in (CONFIG_PATH, PROJECT_CONFIG_PATH):
        if not path.is_file():
            continue
        for line in path.read_text(encoding="utf-8").splitlines():
            stripped = line.strip()
            if not stripped or stripped.startswith("#"):
                continue
            name, separator, value = line.partition("=")
            if separator and name.strip():
                config[name.strip()] = value.strip().strip("\"'")
    return config


class DB:
    def __init__(
        self,
        host: str | None = None,
        private_key_path: str | Path | None = None,
        key_id: str | None = None,
        issuer: str | None = None,
        audience: str | None = None,
        token_ttl_seconds: int | None = None,
        timeout: float = 10.0,
        session: requests.Session | None = None,
    ) -> None:
        config = _read_config()
        self.host = (host or _configured_dbserv_url(config)).rstrip("/")
        configured_key_path = (
            private_key_path
            or os.getenv("JWT_PRIVATE_KEY_PATH")
            or config.get("JWT_PRIVATE_KEY_PATH", "")
        )
        self.private_key_path = Path(configured_key_path) if configured_key_path else None
        self.key_id = key_id or os.getenv("JWT_KEY_ID") or config.get("JWT_KEY_ID", "")
        self.issuer = issuer or os.getenv("JWT_ISSUER") or config.get("JWT_ISSUER", "")
        self.audience = audience or os.getenv("JWT_AUDIENCE") or config.get("JWT_AUDIENCE", "")
        configured_ttl_value = token_ttl_seconds
        if configured_ttl_value is None:
            configured_ttl_value = int(
                os.getenv("JWT_TOKEN_TTL_SECONDS")
                or config.get("JWT_TOKEN_TTL_SECONDS", "300")
            )
        configured_ttl = configured_ttl_value
        if configured_ttl < 1 or configured_ttl > 900:
            raise ValueError("JWT_TOKEN_TTL_SECONDS must be between 1 and 900.")
        self.token_ttl_seconds = configured_ttl
        self._private_key: str | None = None
        self.timeout = timeout
        self.session = session or requests.Session()

    @staticmethod
    def _resource_path(resource: str, record_id: str | int | None = None) -> str:
        resource_name = resource.strip("/")
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", resource_name):
            raise ValueError("Resource names must contain only letters, numbers, and underscores.")

        path = quote(resource_name, safe="")
        if record_id is not None:
            path = f"{path}/{quote(str(record_id), safe='')}"
        return path

    def _request(
        self,
        method: str,
        path: str,
        *,
        scope: str,
        resource: str,
        action: str,
        params: dict[str, Any] | None = None,
        payload: Any = None,
    ) -> Any:
        if not self.host:
            raise RuntimeError("Set DBSERV_URL in api/.env, .env.config, or the environment.")
        token = self._issue_token(scope, resource, action)

        kwargs: dict[str, Any] = {
            "headers": {
                "Accept": "application/json",
                "Authorization": f"Bearer {token}",
            },
            "params": params,
            "timeout": self.timeout,
        }
        if payload is not None:
            kwargs["json"] = payload

        # HTTP failures propagate to the route layer; callers must not get success-shaped defaults.
        response = self.session.request(
            method,
            f"{self.host}/db/{path.lstrip('/')}",
            **kwargs,
        )
        response.raise_for_status()
        if response.status_code == 204 or not response.content:
            return None
        result = response.json()
        if isinstance(result, dict) and "data" in result:
            return result["data"]
        return result

    def check_liveness(self) -> None:
        if not self.host:
            raise RuntimeError("Set DBSERV_URL in api/.env, .env.config, or the environment.")
        response = self.session.request(
            "GET",
            f"{self.host}/db/health",
            headers={"Accept": "application/json"},
            timeout=self.timeout,
        )
        response.raise_for_status()

    def check_database_health(self) -> None:
        self._request(
            "GET",
            "health/database",
            scope="db:read",
            resource="database",
            action="health",
        )

    def get_provider_config(self, provider: str) -> dict[str, Any]:
        if provider != "google":
            raise ValueError("Unsupported authentication provider.")
        return self._request_auth(
            "GET",
            "providers/google/config",
            action="provider_config",
        )

    def exchange_provider_code(
        self,
        provider: str,
        payload: dict[str, str],
    ) -> dict[str, Any]:
        if provider != "google":
            raise ValueError("Unsupported authentication provider.")
        return self._request_auth(
            "POST",
            "providers/google/exchange",
            action="provider_exchange",
            payload=payload,
        )

    def _request_auth(
        self,
        method: str,
        path: str,
        *,
        action: str,
        payload: dict[str, str] | None = None,
    ) -> Any:
        if not self.host:
            raise RuntimeError("Set DBSERV_URL in api/.env, .env.config, or the environment.")
        token = self._issue_token(
            "auth:provider",
            "authentication",
            action,
        )
        kwargs: dict[str, Any] = {
            "headers": {
                "Accept": "application/json",
                "Authorization": f"Bearer {token}",
            },
            "timeout": self.timeout,
        }
        if payload is not None:
            kwargs["json"] = payload
        response = self.session.request(
            method,
            f"{self.host}/auth/v1/{path.lstrip('/')}",
            **kwargs,
        )
        response.raise_for_status()
        result = response.json()
        if isinstance(result, dict) and "data" in result:
            return result["data"]
        return result

    def _issue_token(self, scope: str, resource: str, action: str) -> str:
        if not self.issuer or not self.audience or not self.key_id or self.private_key_path is None:
            raise RuntimeError(
                "Set JWT_ISSUER, JWT_AUDIENCE, JWT_KEY_ID, and "
                "JWT_PRIVATE_KEY_PATH in api/.env, .env.config, or the environment."
            )
        if scope not in {"db:read", "db:write", "auth:provider"}:
            raise ValueError(f"Unsupported database JWT scope: {scope}")
        if action not in {
            "read",
            "aggregate",
            "create",
            "update",
            "delete",
            "health",
            "provider_config",
            "provider_exchange",
        }:
            raise ValueError(f"Unsupported database JWT action: {action}")
        if scope == "auth:provider" and (
            resource != "authentication"
            or action not in {"provider_config", "provider_exchange"}
        ):
            raise ValueError("Unsupported authentication capability.")
        if scope != "auth:provider" and action in {"provider_config", "provider_exchange"}:
            raise ValueError("Authentication actions require the auth:provider scope.")
        if self._private_key is None:
            self._private_key = self.private_key_path.read_text(encoding="utf-8")

        now = int(time.time())
        claims = {
            "iss": self.issuer, 
            "aud": self.audience,
            "sub": "mentos-api",
            "iat": now,
            "exp": now + self.token_ttl_seconds,
            "scope": scope,
            "resource": resource,
            "action": action,
        }
        return jwt.encode(
            claims,
            self._private_key,
            algorithm="RS256",
            headers={"kid": self.key_id, "typ": "JWT"},
        )

    def create(self, resource: str, data: dict[str, Any]) -> Any:
        return self._request(
            "POST",
            self._resource_path(resource),
            scope="db:write",
            resource=resource.strip("/"),
            action="create",
            payload=data,
        )

    def read(
        self,
        resource: str,
        record_id: str | int | None = None,
        *,
        params: dict[str, Any] | None = None,
    ) -> Any:
        return self._request(
            "GET",
            self._resource_path(resource, record_id),
            scope="db:read",
            resource=resource.strip("/"),
            action="read",
            params=params,
        )

    def update(
        self,
        resource: str,
        record_id: str | int,
        data: dict[str, Any],
    ) -> Any:
        return self._request(
            "PATCH",
            self._resource_path(resource, record_id),
            scope="db:write",
            resource=resource.strip("/"),
            action="update",
            payload=data,
        )

    def aggregate(self, resource: str, specification: dict[str, Any]) -> Any:
        path = f"{self._resource_path(resource)}/aggregate"
        return self._request(
            "POST",
            path,
            scope="db:read",
            resource=resource.strip("/"),
            action="aggregate",
            payload=specification,
        )

    def delete(self, resource: str, record_id: str | int) -> Any:
        return self._request(
            "DELETE",
            self._resource_path(resource, record_id),
            scope="db:write",
            resource=resource.strip("/"),
            action="delete",
        )


db = DB()
