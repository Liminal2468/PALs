"""File: app.py
Description: Configure and run the Mentos Flask host API.
Version: 1.0.0
"""

from __future__ import annotations

import os
import secrets
from threading import Lock
from typing import Any

import requests
from flask import Flask, jsonify, request
from flask_limiter import Limiter
from flask_limiter.util import get_remote_address
from werkzeug.exceptions import BadRequest, HTTPException, NotFound
from werkzeug.middleware.proxy_fix import ProxyFix
from waitress import serve

from api.db.db import DB, db
from api.auth_providers import build_provider_registry
from api.limiting import limiter
from api.passwords import hash_password
from api.routes import api, record_rate_limited_auth_attempt
from api.sessions import MariaDBSessionInterface, session_lifetime


def create_app(database: DB | None = None, *, testing: bool = False) -> Flask:
    app = Flask(__name__)
    selected_database = database if database is not None else db
    # Keep browser session contents in MariaDB, not in the signed cookie.
    app.config.update(
        PERMANENT_SESSION_LIFETIME=session_lifetime(
            int(os.getenv("SESSION_LIFETIME_HOURS", "12"))
        ),
        SESSION_COOKIE_NAME="mentos_session",
        SESSION_COOKIE_HTTPONLY=True,
        SESSION_COOKIE_SAMESITE="Lax",
        SESSION_COOKIE_SECURE=os.getenv("SESSION_COOKIE_SECURE", "true").lower() == "true",
        SESSION_COOKIE_PATH="/",
        TESTING=testing,
    )
    app.session_interface = MariaDBSessionInterface(selected_database)
    app.extensions["mentos_database"] = selected_database
    app.extensions["auth_providers"] = build_provider_registry()
    app.extensions["admin_dummy_password_hash"] = hash_password(
        secrets.token_urlsafe(32)
    )
    app.extensions["dbserv_health_cache"] = {
        "status": "degraded",
        "expires_at": 0.0,
    }
    app.extensions["dbserv_health_lock"] = Lock()
    limiter.init_app(app)
    app.register_blueprint(api)
    if os.getenv("TRUST_PROXY", "").lower() in {"1", "true"}:
        app.wsgi_app = ProxyFix(app.wsgi_app, x_for=1, x_proto=1)

    @app.after_request
    def secure_api_response(response: Any) -> Any:
        if request.path == "/api/v1" or request.path.startswith("/api/v1/"):
            response.headers["Cache-Control"] = "no-store"
            response.headers["X-Content-Type-Options"] = "nosniff"
        return response

    # Return consistent JSON errors without hiding unexpected server failures.
    @app.errorhandler(BadRequest)
    def handle_bad_request(error: BadRequest) -> Any:
        return jsonify({"error": {"code": "bad_request", "message": error.description}}), 400

    @app.errorhandler(NotFound)
    def handle_not_found(error: NotFound) -> Any:
        return jsonify({"error": {"code": "not_found", "message": error.description}}), 404

    @app.errorhandler(429)
    def handle_rate_limit(_error: Any) -> Any:
        if request.path.startswith("/api/v1/auth/"):
            record_rate_limited_auth_attempt()
        return jsonify({
            "error": {
                "code": "rate_limited",
                "message": "Too many requests.",
            },
        }), 429

    @app.errorhandler(Exception)
    def handle_unexpected_error(error: Exception) -> Any:
        if isinstance(error, HTTPException):
            return jsonify({
                "error": {
                    "code": "http_error",
                    "message": error.description,
                },
            }), error.code
        upstream_response = None
        if isinstance(error, requests.Timeout):
            upstream_response = (
                503,
                "dbserv_timeout",
                "The database service timed out.",
            )
        elif isinstance(error, requests.ConnectionError):
            upstream_response = (
                503,
                "dbserv_unavailable",
                "The database service is unavailable.",
            )
        elif isinstance(error, requests.HTTPError) and error.response is not None:
            upstream_status = error.response.status_code
            if upstream_status == 429:
                upstream_response = (
                    503,
                    "dbserv_rate_limited",
                    "The database service is temporarily unavailable.",
                )
            elif 500 <= upstream_status <= 599:
                upstream_response = (
                    502,
                    "dbserv_error",
                    "The database service request failed.",
                )
        if upstream_response is not None:
            app.logger.error(
                "DBSERV request failed: %s",
                error,
                exc_info=(type(error), error, error.__traceback__),
            )
            status, code, message = upstream_response
            return jsonify({"error": {"code": code, "message": message}}), status
        app.logger.exception("Unhandled Mentos API error")
        return jsonify({
            "error": {
                "code": "internal_error",
                "message": "Request failed.",
            },
        }), 500

    return app


def main() -> None:
    port = int(os.getenv("PORT", "8000"))
    serve(create_app(), host="127.0.0.1", port=port)
