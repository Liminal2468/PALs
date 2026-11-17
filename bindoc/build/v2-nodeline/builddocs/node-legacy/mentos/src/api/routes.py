"""File: routes.py
Description: Define versioned host API routes and session authorization.
Version: 1.0.0
"""

from __future__ import annotations

import ipaddress
import base64
import hashlib
import logging
import re
import secrets
from datetime import datetime, timezone
from functools import wraps
from threading import Lock
from time import monotonic
from typing import Any, Callable, TypeVar
from urllib.parse import urlencode

from flask import Blueprint, current_app, g, jsonify, redirect, request, session, url_for
import requests

from api.auth_providers import ProviderAdapter, ProviderError, ProviderIdentity
from api.db.db import DB
from api.limiting import limiter
from api.passwords import verify_password
from api.sessions import establish_authenticated_session


api = Blueprint("api", __name__, url_prefix="/api/v1")
F = TypeVar("F", bound=Callable[..., Any])
HEALTH_CACHE_SECONDS = 5
GOOGLE_AUTHORIZATION_ENDPOINT = "https://accounts.google.com/o/oauth2/v2/auth"


def _database() -> DB:
    return current_app.extensions["mentos_database"]


def _error(code: str, message: str, status: int) -> tuple[Any, int]:
    return jsonify({"error": {"code": code, "message": message}}), status


def _user_roles(database: DB, user_id: int) -> list[str]:
    assignments = database.read("user_roles", params={"user_id": user_id})
    assigned_role_ids = {row.get("role_id") for row in assignments}
    roles = database.read("roles")
    return sorted(
        row["role_key"]
        for row in roles
        if row.get("id") in assigned_role_ids and isinstance(row.get("role_key"), str)
    )


def _has_role(database: DB, user_id: int, role_key: str) -> bool:
    return role_key in _user_roles(database, user_id)


def _client_ip_prefix() -> str | None:
    remote_addr = request.remote_addr
    if not remote_addr:
        return None
    try:
        address = ipaddress.ip_address(remote_addr.split("%", maxsplit=1)[0])
        prefix_length = 24 if address.version == 4 else 64
        return str(ipaddress.ip_network(f"{address}/{prefix_length}", strict=False))
    except ValueError:
        return None


def _record_auth_attempt(
    *,
    event: str,
    provider: str,
    outcome: str,
    reason_code: str,
    user_id: int | None = None,
) -> None:
    safe_provider = (
        provider
        if re.fullmatch(r"[a-z][a-z0-9_-]{0,31}", provider)
        else "unknown"
    )
    _database().create(
        "auth_attempts",
        {
            "event": event,
            "provider": safe_provider,
            "user_id": user_id,
            "outcome": outcome,
            "reason_code": reason_code,
            "client_ip_prefix": _client_ip_prefix(),
        },
    )


def record_rate_limited_auth_attempt() -> None:
    path = request.path.removeprefix("/api/v1")
    if path == "/auth/admin/login":
        _record_auth_attempt(
            event="admin_login",
            provider="admin",
            outcome="failure",
            reason_code="rate_limited",
        )
    elif path == "/auth/login" or path.endswith("/start"):
        provider = "unknown"
        if request.view_args and isinstance(request.view_args.get("provider"), str):
            provider = request.view_args["provider"]
        elif path.endswith("/start"):
            provider = path.split("/")[-2]
        if path == "/auth/login":
            payload = request.get_json(silent=True)
            if isinstance(payload, dict) and isinstance(payload.get("provider"), str):
                provider = payload["provider"]
        _record_auth_attempt(
            event="provider_start",
            provider=provider,
            outcome="failure",
            reason_code="rate_limited",
        )
    elif path.endswith("/callback"):
        provider = "unknown"
        if request.view_args and isinstance(request.view_args.get("provider"), str):
            provider = request.view_args["provider"]
        else:
            provider = path.split("/")[-2]
        _record_auth_attempt(
            event="provider_callback",
            provider=provider,
            outcome="failure",
            reason_code="rate_limited",
        )
    elif path == "/auth/signup":
        provider = session.get("pending_signup_provider", "unknown")
        _record_auth_attempt(
            event="signup",
            provider=provider,
            outcome="failure",
            reason_code="rate_limited",
        )


def _provider_registry() -> dict[str, ProviderAdapter]:
    return current_app.extensions["auth_providers"]


def _auth_flow_store() -> dict[str, dict[str, str]]:
    stored = session.get("_auth_flows", {})
    return stored if isinstance(stored, dict) else {}


def _admin_username_rate_key() -> str:
    payload = request.get_json(silent=True)
    username = payload.get("username") if isinstance(payload, dict) else None
    if not isinstance(username, str):
        return f"invalid:{request.remote_addr or 'unknown'}"
    return hashlib.sha256(username.strip().lower().encode("utf-8")).hexdigest()


def _complete_provider_login(identity: ProviderIdentity) -> Any:
    database = _database()
    matches = database.read(
        "auth_identities",
        params={
            "provider": identity.provider,
            "provider_subject": identity.subject,
        },
    )
    if not isinstance(matches, list):
        raise RuntimeError("Database returned an invalid provider identity list.")
    if matches:
        user_id = int(matches[0]["user_id"])
        user = database.read("users", user_id)
        if not isinstance(user, dict):
            raise RuntimeError("Provider identity references a missing user.")
    else:
        created = database.create(
            "users",
            {
                "external_subject": None,
                "email": identity.email,
                "display_name": identity.display_name,
                "account_status": "pending",
            },
        )
        if not isinstance(created, dict) or not isinstance(created.get("id"), int):
            raise RuntimeError("Database did not return the new user ID.")
        user_id = created["id"]
        database.create(
            "auth_identities",
            {
                "user_id": user_id,
                "provider": identity.provider,
                "provider_subject": identity.subject,
                "email": identity.email,
            },
        )
        session["pending_signup_user_id"] = user_id
        session["pending_signup_provider"] = identity.provider
        _record_auth_attempt(
            event="provider_callback",
            provider=identity.provider,
            user_id=user_id,
            outcome="pending",
            reason_code="signup_required",
        )
        return jsonify({
            "data": {
                "status": "signup_required",
                "provider": identity.provider,
            },
        }), 202

    if user.get("account_status") == "pending":
        session["pending_signup_user_id"] = user_id
        session["pending_signup_provider"] = identity.provider
        _record_auth_attempt(
            event="provider_callback",
            provider=identity.provider,
            user_id=user_id,
            outcome="pending",
            reason_code="signup_required",
        )
        return jsonify({
            "data": {
                "status": "signup_required",
                "provider": identity.provider,
            },
        }), 202

    if user.get("account_status") != "active":
        _record_auth_attempt(
            event="provider_callback",
            provider=identity.provider,
            user_id=user_id,
            outcome="failure",
            reason_code="account_inactive",
        )
        return _error("account_inactive", "The account is not active.", 403)

    roles = _user_roles(database, user_id)
    establish_authenticated_session(user_id, roles=roles)
    _record_auth_attempt(
        event="provider_callback",
        provider=identity.provider,
        user_id=user_id,
        outcome="success",
        reason_code="authenticated",
    )
    return jsonify({"data": {"status": "authenticated", "roles": roles}})


def _start_provider(provider_id: str, identity_hint: str | None = None) -> Any:
    provider = _provider_registry().get(provider_id)
    if provider is None:
        _record_auth_attempt(
            event="provider_start",
            provider=provider_id,
            outcome="failure",
            reason_code="provider_unavailable",
        )
        return _error("provider_unavailable", "This login provider is not enabled.", 404)

    if provider_id == "dev" and identity_hint is None:
        identity_hint = request.args.get("identity")
    redirect_uri = url_for(
        "api.provider_callback",
        provider=provider_id,
        _external=provider_id != "dev",
    )
    if provider_id != "dev":
        redirect_uri = getattr(provider, "redirect_uri", "")
        if not redirect_uri:
            _record_auth_attempt(
                event="provider_start",
                provider=provider_id,
                outcome="failure",
                reason_code="provider_not_configured",
            )
            return _error("provider_unavailable", "This login provider is not configured.", 503)

    state = secrets.token_urlsafe(32)
    nonce = secrets.token_urlsafe(32)
    verifier = secrets.token_urlsafe(64)
    try:
        authorization_url = provider.authorization_url(
            state=state,
            nonce=nonce,
            verifier=verifier,
            redirect_uri=redirect_uri,
            identity_hint=identity_hint,
        )
    except ProviderError as error:
        _record_auth_attempt(
            event="provider_start",
            provider=provider_id,
            outcome="failure",
            reason_code=error.reason_code,
        )
        return _error("invalid_request", "The provider login request is invalid.", 400)

    flows = dict(_auth_flow_store())
    flows[provider_id] = {
        "state": state,
        "nonce": nonce,
        "verifier": verifier,
        "redirect_uri": redirect_uri,
    }
    session["_auth_flows"] = flows
    _record_auth_attempt(
        event="provider_start",
        provider=provider_id,
        outcome="started",
        reason_code="authorization_started",
    )
    return redirect(authorization_url, code=302)

def _serialize_record(record: Any, allowed_fields: tuple[str, ...]) -> dict[str, Any]:
    if not isinstance(record, dict):
        raise RuntimeError("Database returned an invalid record.")
    return {field: record[field] for field in allowed_fields if field in record}


def _serialize_learning_requests(rows: Any) -> list[dict[str, Any]]:
    if not isinstance(rows, list):
        raise RuntimeError("Database returned an invalid learning-request list.")
    fields = (
        "id",
        "subject_id",
        "topic_id",
        "current_proficiency",
        "requested_help",
        "preferred_format",
        "request_status",
        "created_at",
        "updated_at",
    )
    return [_serialize_record(row, fields) for row in rows]


def authenticated(handler: F) -> F:
    @wraps(handler)
    def wrapped(*args: Any, **kwargs: Any) -> Any:
        # Routes trust only the server-side session identity, never a body ID.
        user_id = session.get("user_id")
        if not user_id:
            return _error("unauthorized", "Authentication is required.", 401)
        user = _database().read("users", user_id)
        if not isinstance(user, dict):
            session.clear()
            return _error("unauthorized", "Authentication is required.", 401)
        if user.get("account_status") != "active":
            session.clear()
            return _error("account_inactive", "The account is not active.", 403)
        g.authenticated_user = user
        return handler(*args, **kwargs)

    return wrapped  # type: ignore[return-value]


@api.get("/health")
def health() -> Any:
    app = current_app
    cache = app.extensions["dbserv_health_cache"]
    with app.extensions["dbserv_health_lock"]:
        now = monotonic()
        if cache["expires_at"] > now:
            return jsonify({"status": cache["status"]})

        try:
            _database().check_database_health()
        except (requests.RequestException, RuntimeError):
            app.logger.exception("DBSERV readiness check failed.")
            cache["status"] = "degraded"
        else:
            cache["status"] = "ready"
        cache["expires_at"] = now + HEALTH_CACHE_SECONDS
        return jsonify({"status": cache["status"]})


@api.route("/session", methods=["GET", "DELETE"])
@limiter.limit("30 per 15 minutes", override_defaults=True)
def session_status() -> Any:
    if request.method == "DELETE":
        session.clear()
        return "", 204
    user_id = session.get("user_id")
    if not user_id:
        return jsonify({"authenticated": False})
    return jsonify({
        "authenticated": True,
        "user_id": user_id,
        "authenticated_at": session.get("authenticated_at"),
    })


@api.get("/auth/options")
def auth_options() -> Any:
    providers = [
        {
            "id": provider.provider_id,
            "label": provider.label,
            "start_url": url_for(
                "api.provider_start",
                provider=provider.provider_id,
                _external=False,
            ),
        }
        for provider in _provider_registry().values()
    ]
    google_config = _database().get_provider_config("google")
    if not isinstance(google_config, dict):
        raise RuntimeError("DBSERV returned an invalid Google provider configuration.")
    providers.append({
        "id": "google",
        "label": "Google OAuth2",
        "start_url": url_for("api.google_start", _external=False),
        "available": google_config.get("enabled") is True,
        "status": "preview" if google_config.get("enabled") is True else "not_configured",
    })
    return jsonify({
        "data": {
            "providers": providers,
            "planned_providers": [
                {
                    "id": "microsoft-entra",
                    "label": "Microsoft Entra",
                    "status": "planned",
                },
            ],
            "admin": {
                "id": "admin",
                "label": "Admin login",
                "url": url_for("api.admin_login", _external=False),
            },
        },
    })


@api.get("/auth/google/start")
@limiter.limit("10 per 15 minutes", override_defaults=True)
def google_start() -> Any:
    return _start_google_provider()


def _start_google_provider() -> Any:
    config = _database().get_provider_config("google")
    if not isinstance(config, dict) or config.get("enabled") is not True:
        _record_auth_attempt(
            event="provider_start",
            provider="google",
            outcome="failure",
            reason_code="provider_not_configured",
        )
        return _error("provider_unavailable", "Google sign-in is not configured.", 503)

    client_id = config.get("client_id")
    redirect_uri = url_for("api.google_callback", _external=True)
    redirect_uris = config.get("redirect_uris")
    if (
        not isinstance(client_id, str)
        or not client_id
        or len(client_id) > 256
        or config.get("authorization_endpoint") != GOOGLE_AUTHORIZATION_ENDPOINT
        or not isinstance(redirect_uris, list)
        or redirect_uri not in redirect_uris
    ):
        _record_auth_attempt(
            event="provider_start",
            provider="google",
            outcome="failure",
            reason_code="provider_not_configured",
        )
        return _error("provider_unavailable", "Google sign-in is not configured.", 503)

    state = secrets.token_urlsafe(32)
    nonce = secrets.token_urlsafe(32)
    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(
        hashlib.sha256(verifier.encode("ascii")).digest()
    ).rstrip(b"=").decode("ascii")
    flows = dict(_auth_flow_store())
    flows["google"] = {
        "state": state,
        "nonce": nonce,
        "verifier": verifier,
        "redirect_uri": redirect_uri,
    }
    session["_auth_flows"] = flows
    query = urlencode({
        "client_id": client_id,
        "redirect_uri": redirect_uri,
        "response_type": "code",
        "scope": "openid email profile",
        "state": state,
        "nonce": nonce,
        "code_challenge": challenge,
        "code_challenge_method": "S256",
    })
    _record_auth_attempt(
        event="provider_start",
        provider="google",
        outcome="started",
        reason_code="authorization_started",
    )
    return redirect(f"{GOOGLE_AUTHORIZATION_ENDPOINT}?{query}", code=302)


@api.get("/auth/<provider>/start")
def provider_start(provider: str) -> Any:
    return _start_provider(provider)


@api.post("/auth/login")
@limiter.limit("10 per 15 minutes", override_defaults=True)
def login_with_provider() -> Any:
    payload = request.get_json(silent=True)
    if (
        not isinstance(payload, dict)
        or set(payload) - {"provider", "identity"}
        or not isinstance(payload.get("provider"), str)
        or ("identity" in payload and not isinstance(payload["identity"], str))
    ):
        return _error("bad_request", "A provider identifier is required.", 400)
    if payload["provider"] == "google":
        if "identity" in payload:
            return _error("bad_request", "A Google login does not accept an identity hint.", 400)
        return _start_google_provider()
    return _start_provider(payload["provider"], payload.get("identity"))


@api.route("/auth/<provider>/callback", methods=["GET", "POST"])
def provider_callback(provider: str) -> Any:
    adapter = _provider_registry().get(provider)
    if adapter is None:
        _record_auth_attempt(
            event="provider_callback",
            provider=provider,
            outcome="failure",
            reason_code="provider_unavailable",
        )
        return _error("provider_unavailable", "This login provider is not enabled.", 404)
    flows = dict(_auth_flow_store())
    flow = flows.pop(provider, None)
    session["_auth_flows"] = flows
    state = request.values.get("state", "")
    code = request.values.get("code", "")
    if (
        not isinstance(flow, dict)
        or any(
            not isinstance(flow.get(key), str)
            for key in ("state", "nonce", "verifier", "redirect_uri")
        )
        or not state
        or not secrets.compare_digest(state, flow.get("state", ""))
        or not code
    ):
        _record_auth_attempt(
            event="provider_callback",
            provider=provider,
            outcome="failure",
            reason_code="invalid_state",
        )
        return _error("invalid_credentials", "Provider authentication failed.", 401)

    try:
        identity = adapter.authenticate(
            code=code,
            nonce=flow["nonce"],
            verifier=flow["verifier"],
            redirect_uri=flow["redirect_uri"],
            callback_url=request.base_url,
        )
        if identity.provider != provider:
            raise ProviderError("provider_mismatch")
    except ProviderError as error:
        _record_auth_attempt(
            event="provider_callback",
            provider=provider,
            outcome="failure",
            reason_code=error.reason_code,
        )
        return _error("invalid_credentials", "Provider authentication failed.", 401)
    return _complete_provider_login(identity)


@api.get("/auth/google/callback")
@limiter.limit("10 per 15 minutes", override_defaults=True)
def google_callback() -> Any:
    flows = dict(_auth_flow_store())
    flow = flows.pop("google", None)
    session["_auth_flows"] = flows
    state = request.args.get("state", "")
    code = request.args.get("code", "")
    if (
        request.args.get("error")
        or not isinstance(flow, dict)
        or any(
            not isinstance(flow.get(key), str)
            for key in ("state", "nonce", "verifier", "redirect_uri")
        )
        or not state
        or not secrets.compare_digest(state, flow.get("state", ""))
        or not re.fullmatch(r"[\x21-\x7e]{1,2048}", code)
    ):
        _record_auth_attempt(
            event="provider_callback",
            provider="google",
            outcome="failure",
            reason_code="invalid_state",
        )
        return _error("invalid_credentials", "Provider authentication failed.", 401)

    try:
        result = _database().exchange_provider_code(
            "google",
            {
                "code": code,
                "code_verifier": flow["verifier"],
                "nonce": flow["nonce"],
                "redirect_uri": flow["redirect_uri"],
            },
        )
    except (requests.Timeout, requests.ConnectionError, requests.HTTPError) as error:
        logging.getLogger(__name__).error(
            "DBSERV Google provider exchange failed: %s",
            error,
            exc_info=(type(error), error, error.__traceback__),
        )
        if (
            isinstance(error, requests.HTTPError)
            and error.response is not None
            and error.response.status_code == 401
        ):
            _record_auth_attempt(
                event="provider_callback",
                provider="google",
                outcome="failure",
                reason_code="provider_authentication_failed",
            )
            return _error("invalid_credentials", "Provider authentication failed.", 401)
        status = (
            503
            if isinstance(error, (requests.Timeout, requests.ConnectionError))
            or (
                isinstance(error, requests.HTTPError)
                and error.response is not None
                and error.response.status_code >= 500
            )
            else 502
        )
        return _error("provider_unavailable", "Google sign-in is temporarily unavailable.", status)

    if (
        not isinstance(result, dict)
        or result.get("provider") != "google"
        or not isinstance(result.get("subject"), str)
        or not result["subject"]
        or not isinstance(result.get("email"), str)
        or not isinstance(result.get("display_name"), str)
    ):
        _record_auth_attempt(
            event="provider_callback",
            provider="google",
            outcome="failure",
            reason_code="invalid_provider_response",
        )
        return _error("invalid_credentials", "Provider authentication failed.", 401)
    return _complete_provider_login(ProviderIdentity(
        provider="google",
        subject=result["subject"],
        email=result["email"],
        display_name=result["display_name"],
    ))


@api.post("/auth/signup")
@limiter.limit("5 per 15 minutes", override_defaults=True)
def complete_signup() -> Any:
    payload = request.get_json(silent=True)
    if (
        not isinstance(payload, dict)
        or set(payload) != {"terms_accepted", "terms_version"}
        or payload.get("terms_accepted") is not True
        or not isinstance(payload.get("terms_version"), str)
        or not re.fullmatch(r"[A-Za-z0-9._-]{1,64}", payload["terms_version"])
    ):
        return _error("bad_request", "Valid terms acceptance is required.", 400)

    user_id = session.get("pending_signup_user_id")
    provider = session.get("pending_signup_provider")
    if not isinstance(user_id, int) or not isinstance(provider, str):
        return _error("signup_required", "Start provider login before signing up.", 401)
    user = _database().read("users", user_id)
    if not isinstance(user, dict) or user.get("account_status") != "pending":
        session.pop("pending_signup_user_id", None)
        session.pop("pending_signup_provider", None)
        return _error("signup_unavailable", "This signup cannot be completed.", 409)

    _database().update(
        "users",
        user_id,
        {
            "terms_version": payload["terms_version"],
            "terms_accepted_at": datetime.now(timezone.utc).isoformat(),
        },
    )
    _record_auth_attempt(
        event="signup",
        provider=provider,
        user_id=user_id,
        outcome="pending",
        reason_code="signup_submitted",
    )
    return jsonify({"data": {"status": "pending_review"}}), 202


@api.post("/auth/admin/login")
@limiter.limit("5 per 15 minutes", override_defaults=True)
@limiter.limit(
    "5 per 15 minutes",
    key_func=_admin_username_rate_key,
    override_defaults=True,
)
def admin_login() -> Any:
    payload = request.get_json(silent=True)
    if not isinstance(payload, dict):
        _record_auth_attempt(
            event="admin_login",
            provider="admin",
            outcome="failure",
            reason_code="invalid_request",
        )
        return _error("bad_request", "Expected a username and password.", 400)
    username = payload.get("username")
    password = payload.get("password")
    if (
        set(payload) != {"username", "password"}
        or not isinstance(username, str)
        or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{2,63}", username)
        or not isinstance(password, str)
        or not re.fullmatch(r"[\x20-\x7e]{12,128}", password)
    ):
        _record_auth_attempt(
            event="admin_login",
            provider="admin",
            outcome="failure",
            reason_code="invalid_request",
        )
        return _error("bad_request", "Invalid admin login input.", 400)

    database = _database()
    records = database.read(
        "admin_credentials",
        params={"username": username.lower()},
    )
    if not isinstance(records, list):
        raise RuntimeError("Database returned an invalid admin credential list.")
    credential = records[0] if records else None
    dummy_hash = current_app.extensions["admin_dummy_password_hash"]
    stored_hash = (
        credential.get("password_hash")
        if isinstance(credential, dict) and isinstance(credential.get("password_hash"), str)
        else dummy_hash
    )
    password_matches = verify_password(password, stored_hash, dummy_hash)
    if credential is None or not password_matches:
        _record_auth_attempt(
            event="admin_login",
            provider="admin",
            outcome="failure",
            reason_code="invalid_credentials",
        )
        return _error("invalid_credentials", "Username or password is incorrect.", 401)

    user_id = credential.get("user_id")
    user = database.read("users", user_id) if isinstance(user_id, int) else None
    roles = _user_roles(database, user_id) if isinstance(user_id, int) else []
    if not isinstance(user, dict) or user.get("account_status") != "active":
        _record_auth_attempt(
            event="admin_login",
            provider="admin",
            user_id=user_id if isinstance(user_id, int) else None,
            outcome="failure",
            reason_code="account_inactive",
        )
        return _error("invalid_credentials", "Username or password is incorrect.", 401)
    if "admin" not in roles:
        _record_auth_attempt(
            event="admin_login",
            provider="admin",
            user_id=user_id,
            outcome="failure",
            reason_code="admin_role_required",
        )
        return _error("invalid_credentials", "Username or password is incorrect.", 401)

    establish_authenticated_session(user_id, roles=roles)
    _record_auth_attempt(
        event="admin_login",
        provider="admin",
        user_id=user_id,
        outcome="success",
        reason_code="authenticated",
    )
    return jsonify({"data": {"status": "authenticated", "roles": roles}})


@api.get("/me")
@authenticated
def get_me() -> Any:
    database = _database()
    user_id = session["user_id"]
    profile_rows = database.read("student_profiles", params={"user_id": user_id})
    if not isinstance(profile_rows, list):
        raise RuntimeError("Database returned an invalid student-profile list.")
    return jsonify({
        "data": {
            "user": _serialize_record(g.authenticated_user, ("id", "display_name")),
            "student_profile": (
                _serialize_record(
                    profile_rows[0],
                    ("grade_level", "strand", "timezone", "learning_preferences"),
                )
                if profile_rows
                else None
            ),
            "roles": _user_roles(database, user_id),
        },
    })


@api.patch("/me")
@authenticated
def update_me() -> Any:
    payload = request.get_json(silent=False)
    if not isinstance(payload, dict) or not payload:
        return _error("bad_request", "Expected a non-empty JSON object.", 400)
    allowed = {"display_name", "grade_level", "strand", "timezone", "learning_preferences"}
    # Reject identity, role, and account-status fields rather than silently ignoring them.
    unknown = set(payload) - allowed
    if unknown:
        return _error(
            "bad_request",
            f"Unsupported profile field(s): {', '.join(sorted(unknown))}.",
            400,
        )
    if "display_name" in payload and (
        not isinstance(payload["display_name"], str)
        or not payload["display_name"].strip()
        or len(payload["display_name"]) > 120
    ):
        return _error("bad_request", "display_name must be 1 to 120 characters.", 400)
    if "grade_level" in payload and (
        isinstance(payload["grade_level"], bool)
        or payload["grade_level"] not in {11, 12}
    ):
        return _error("bad_request", "grade_level must be 11 or 12.", 400)
    for field in ("strand", "timezone"):
        if field in payload and (
            not isinstance(payload[field], str)
            or not payload[field].strip()
            or len(payload[field]) > (100 if field == "strand" else 64)
        ):
            return _error("bad_request", f"{field} must be a non-empty string.", 400)
    if "learning_preferences" in payload and not isinstance(
        payload["learning_preferences"], (dict, list)
    ):
        return _error("bad_request", "learning_preferences must be an object or array.", 400)

    database = _database()
    user_id = session["user_id"]
    user_changes = {key: payload[key] for key in ("display_name",) if key in payload}
    profile_changes = {
        key: value
        for key, value in payload.items()
        if key in {"grade_level", "strand", "timezone", "learning_preferences"}
    }

    profile_rows = (
        database.read("student_profiles", params={"user_id": user_id})
        if profile_changes
        else []
    )
    if profile_changes and not profile_rows:
        return _error("conflict", "Student profile has not been created.", 409)

    if user_changes:
        database.update("users", user_id, user_changes)
    if profile_changes:
        database.update("student_profiles", profile_rows[0]["id"], profile_changes)
    return get_me()


@api.get("/learning-requests")
@authenticated
def list_learning_requests() -> Any:
    rows = _database().read(
        "learning_requests",
        params={"tutee_user_id": session["user_id"]},
    )
    return jsonify({"data": _serialize_learning_requests(rows)})


@api.post("/learning-requests")
@authenticated
def create_learning_request() -> Any:
    payload = request.get_json(silent=False)
    if not isinstance(payload, dict):
        return _error("bad_request", "Expected a JSON object.", 400)

    allowed = {
        "subject_id",
        "topic_id",
        "current_proficiency",
        "requested_help",
        "preferred_format",
    }
    if set(payload) - allowed or "subject_id" not in payload:
        return _error("bad_request", "Invalid learning-request fields.", 400)
    proficiency = payload.get("current_proficiency")
    if proficiency is not None and (
        isinstance(proficiency, bool)
        or not isinstance(proficiency, int)
        or proficiency < 1
        or proficiency > 5
    ):
        return _error("bad_request", "current_proficiency must be an integer from 1 to 5.", 400)
    preferred_format = payload.get("preferred_format", "either")
    if preferred_format not in {"one_to_one", "group", "either"}:
        return _error("bad_request", "Invalid preferred_format.", 400)
    subject_id = payload["subject_id"]
    if isinstance(subject_id, bool) or not isinstance(subject_id, int) or subject_id < 1:
        return _error("bad_request", "subject_id must be a positive integer.", 400)
    topic_id = payload.get("topic_id")
    if topic_id is not None and (
        isinstance(topic_id, bool) or not isinstance(topic_id, int) or topic_id < 1
    ):
        return _error("bad_request", "topic_id must be a positive integer.", 400)
    requested_help = payload.get("requested_help")
    if requested_help is not None and (
        not isinstance(requested_help, str) or len(requested_help) > 4000
    ):
        return _error("bad_request", "requested_help must be a string up to 4000 characters.", 400)
    if not _has_role(_database(), session["user_id"], "tutee"):
        return _error("forbidden", "The authenticated user does not have the tutee role.", 403)

    # Derive ownership from the authenticated session to prevent user-ID spoofing.
    created = _database().create(
        "learning_requests",
        {
            **payload,
            "tutee_user_id": session["user_id"],
            "preferred_format": preferred_format,
            "request_status": "open",
        },
    )
    return jsonify({"data": _serialize_record(created, ("id",))}), 201


def establish_user_session(user_id: int | str) -> None:
    establish_authenticated_session(user_id)
