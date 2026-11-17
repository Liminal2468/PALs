"""File: sessions.py
Description: Store opaque browser sessions in MariaDB.
Version: 1.0.0
"""

from __future__ import annotations

import hashlib
import json
import secrets
from datetime import datetime, timedelta, timezone
from typing import Any

from flask import Flask, Request, Response, session
from flask.sessions import SessionInterface, SessionMixin
from werkzeug.datastructures import CallbackDict


SESSION_RESOURCE = "web_sessions"


def _session_key_digest(session_id: str) -> str:
    return hashlib.sha256(session_id.encode("utf-8")).hexdigest()


class ServerSession(CallbackDict[str, Any], SessionMixin):
    def __init__(
        self,
        initial: dict[str, Any] | None = None,
        *,
        session_id: str | None = None,
        record_id: int | None = None,
    ) -> None:
        def on_update(_: CallbackDict[str, Any]) -> None:
            self.modified = True
            self.accessed = True

        super().__init__(initial or {}, on_update)
        self.session_id = session_id
        self.record_id = record_id
        self.previous_session_id: str | None = None
        self.modified = False
        self.accessed = False

    def __getitem__(self, key: str) -> Any:
        self.accessed = True
        return super().__getitem__(key)

    def get(self, key: str, default: Any = None) -> Any:
        self.accessed = True
        return super().get(key, default)


class MariaDBSessionInterface(SessionInterface):
    session_class = ServerSession

    def __init__(self, database: Any) -> None:
        self.database = database

    def open_session(self, app: Flask, request: Request) -> ServerSession:
        session_id = request.cookies.get(self.get_cookie_name(app))
        if not session_id:
            return self.session_class()

        # Only the cookie carries the raw ID; the DB stores its irreversible digest.
        session_key = _session_key_digest(session_id)
        rows = self.database.read(SESSION_RESOURCE, params={"session_key": session_key})
        if not isinstance(rows, list):
            raise RuntimeError("Database returned an invalid session list.")
        if not rows:
            legacy_rows = self.database.read(
                SESSION_RESOURCE,
                params={"session_key": session_id},
            )
            if not isinstance(legacy_rows, list):
                raise RuntimeError("Database returned an invalid legacy session list.")
            if legacy_rows:
                self.database.update(
                    SESSION_RESOURCE,
                    legacy_rows[0]["id"],
                    {"session_key": session_key},
                )
                rows = legacy_rows
        if not rows:
            return self.session_class()

        record = rows[0]
        expires_at = _parse_datetime(record.get("expires_at"))
        if expires_at is None or expires_at <= datetime.now(timezone.utc):
            self.database.delete(SESSION_RESOURCE, record["id"])
            return self.session_class()

        data = record.get("session_data")
        if isinstance(data, str):
            data = json.loads(data)
        if not isinstance(data, dict):
            raise RuntimeError("Stored session data is not a JSON object.")

        return self.session_class(
            data,
            session_id=session_id,
            record_id=int(record["id"]),
        )

    def save_session(
        self,
        app: Flask,
        current_session: ServerSession,
        response: Response,
    ) -> None:
        cookie_name = self.get_cookie_name(app)
        if current_session.accessed:
            response.vary.add("Cookie")
        if not current_session:
            if current_session.record_id is not None:
                self.database.delete(SESSION_RESOURCE, current_session.record_id)
            self._delete_previous_session(current_session)
            response.delete_cookie(
                cookie_name,
                domain=self.get_cookie_domain(app),
                path=self.get_cookie_path(app),
                secure=self.get_cookie_secure(app),
                httponly=self.get_cookie_httponly(app),
                samesite=self.get_cookie_samesite(app),
            )
            return

        if not current_session.modified and current_session.record_id is not None:
            current_session.accessed = False
            return

        if current_session.session_id is None:
            current_session.session_id = secrets.token_urlsafe(32)

        lifetime = app.permanent_session_lifetime
        expires_at = datetime.now(timezone.utc) + lifetime
        values = {
            "session_key": _session_key_digest(current_session.session_id),
            "session_data": dict(current_session),
            "expires_at": expires_at.strftime("%Y-%m-%d %H:%M:%S.%f"),
        }

        if current_session.record_id is None:
            created = self.database.create(SESSION_RESOURCE, values)
            if not isinstance(created, dict) or not isinstance(created.get("id"), int):
                raise RuntimeError("Database did not return the new session record ID.")
            current_session.record_id = created["id"]
        else:
            self.database.update(SESSION_RESOURCE, current_session.record_id, values)

        self._delete_previous_session(current_session)
        current_session.previous_session_id = None
        current_session.modified = False
        current_session.accessed = False
        response.set_cookie(
            cookie_name,
            current_session.session_id,
            expires=self.get_expiration_time(app, current_session),
            httponly=self.get_cookie_httponly(app),
            domain=self.get_cookie_domain(app),
            path=self.get_cookie_path(app),
            secure=self.get_cookie_secure(app),
            samesite=self.get_cookie_samesite(app),
        )

    def rotate_session_id(self, current_session: ServerSession) -> None:
        # Replace pre-authentication IDs to prevent session fixation.
        if current_session.session_id is not None:
            current_session.previous_session_id = current_session.session_id
        current_session.session_id = secrets.token_urlsafe(32)
        current_session.record_id = None
        current_session.modified = True

    def _delete_previous_session(self, current_session: ServerSession) -> None:
        if current_session.previous_session_id is None:
            return
        old_rows = self.database.read(
            SESSION_RESOURCE,
            params={"session_key": _session_key_digest(current_session.previous_session_id)},
        )
        if old_rows:
            self.database.delete(SESSION_RESOURCE, old_rows[0]["id"])


def establish_authenticated_session(
    user_id: int | str,
    *,
    roles: list[str] | None = None,
) -> None:
    if isinstance(user_id, bool) or not str(user_id).isdigit():
        raise ValueError("Authenticated user ID must be a positive integer.")
    if int(user_id) < 1:
        raise ValueError("Authenticated user ID must be a positive integer.")

    current_session = session._get_current_object()
    interface = _current_session_interface()
    current_session.clear()
    interface.rotate_session_id(current_session)
    current_session["user_id"] = int(user_id)
    current_session["roles"] = roles or []
    current_session["authenticated_at"] = datetime.now(timezone.utc).isoformat()
    current_session.permanent = True


def _current_session_interface() -> MariaDBSessionInterface:
    from flask import current_app

    interface = current_app.session_interface
    if not isinstance(interface, MariaDBSessionInterface):
        raise RuntimeError("MariaDB session interface is not configured.")
    return interface


def _parse_datetime(value: object) -> datetime | None:
    if isinstance(value, datetime):
        parsed = value
    elif isinstance(value, str):
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        except ValueError:
            return None
    else:
        return None
    if parsed.tzinfo is None:
        return parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def session_lifetime(hours: int = 12) -> timedelta:
    # Cap sessions at one day so a deployment misconfiguration stays bounded.
    if hours < 1 or hours > 24:
        raise ValueError("Session lifetime must be between 1 and 24 hours.")
    return timedelta(hours=hours)
