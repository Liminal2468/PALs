from __future__ import annotations

import hashlib
import os
import time
import unittest
from datetime import datetime, timedelta, timezone
from typing import Any
from urllib.parse import parse_qs, urlsplit
from unittest.mock import patch

import requests
from flask import Flask
from werkzeug.middleware.proxy_fix import ProxyFix

from api.app import create_app, main
from api.auth_providers import OpenIDConnectAdapter, ProviderError
from api.demo_seed import seed_demo_accounts
from api.passwords import hash_password, verify_password
from api.routes import HEALTH_CACHE_SECONDS


class MemoryDatabase:
    def __init__(self) -> None:
        self.rows: dict[str, list[dict[str, Any]]] = {
            "web_sessions": [],
            "users": [{
                "id": 7,
                "display_name": "Kai",
                "email": "private@example.test",
                "external_subject": "private-subject",
                "account_status": "active",
            }],
            "student_profiles": [{
                "id": 12,
                "user_id": 7,
                "grade_level": 12,
                "strand": "IT",
                "timezone": "Asia/Manila",
                "learning_preferences": {"format": "visual"},
                "private_note": "do not expose",
            }],
            "roles": [{"id": 1, "role_key": "tutee"}],
            "user_roles": [{"id": 2, "user_id": 7, "role_id": 1}],
            "learning_requests": [],
            "auth_identities": [],
            "auth_attempts": [],
            "admin_credentials": [],
        }
        self.next_id = 100
        self.health_checks = 0
        self.health_error: Exception | None = None
        self.read_errors: dict[str, Exception] = {}
        self.provider_config: dict[str, Any] = {"enabled": False}
        self.provider_exchange: dict[str, Any] = {}
        self.provider_error: Exception | None = None

    def check_database_health(self) -> None:
        self.health_checks += 1
        if self.health_error is not None:
            raise self.health_error

    def get_provider_config(self, provider: str) -> dict[str, Any]:
        if provider != "google":
            raise ValueError("Unsupported provider.")
        return dict(self.provider_config)

    def exchange_provider_code(
        self,
        provider: str,
        payload: dict[str, str],
    ) -> dict[str, Any]:
        if provider != "google":
            raise ValueError("Unsupported provider.")
        self.provider_exchange = dict(payload)
        if self.provider_error is not None:
            raise self.provider_error
        return {
            "provider": "google",
            "subject": "google-subject",
            "email": "kai@example.test",
            "display_name": "Kai",
        }

    def read(
        self,
        resource: str,
        record_id: str | int | None = None,
        *,
        params: dict[str, Any] | None = None,
    ) -> Any:
        if resource in self.read_errors:
            raise self.read_errors[resource]
        if record_id is not None:
            return next(
                (dict(row) for row in self.rows.get(resource, []) if row["id"] == int(record_id)),
                None,
            )
        return [
            dict(row)
            for row in self.rows.get(resource, [])
            if all(row.get(key) == value for key, value in (params or {}).items())
        ]

    def create(self, resource: str, data: dict[str, Any]) -> dict[str, Any]:
        row = {"id": self.next_id, **data}
        self.next_id += 1
        self.rows.setdefault(resource, []).append(row)
        return {"id": row["id"], "affected_rows": 1}

    def update(self, resource: str, record_id: str | int, data: dict[str, Any]) -> None:
        for row in self.rows.get(resource, []):
            if row["id"] == int(record_id):
                row.update(data)
                return
        raise AssertionError("Record not found")

    def delete(self, resource: str, record_id: str | int) -> None:
        self.rows[resource] = [
            row for row in self.rows.get(resource, []) if row["id"] != int(record_id)
        ]


class HostApiTests(unittest.TestCase):
    def setUp(self) -> None:
        self.database = MemoryDatabase()
        self.app = create_app(self.database, testing=True)
        self.client = self.app.test_client()
        self.app.config["SESSION_COOKIE_SECURE"] = False

    def authenticate(self) -> None:
        with self.client.session_transaction() as browser_session:
            browser_session.update({"user_id": 7, "authenticated_at": "test"})

    def test_health_and_login_request_validation(self) -> None:
        response = self.client.get("/api/v1/health")
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json, {"status": "ready"})
        self.assertNotIn("Cookie", response.vary)
        self.assertEqual(self.database.health_checks, 1)

        cached = self.client.get("/api/v1/health")
        self.assertEqual(cached.json, {"status": "ready"})
        self.assertEqual(self.database.health_checks, 1)

        login = self.client.post("/api/v1/auth/login", json={})
        self.assertEqual(login.status_code, 400)
        self.assertEqual(login.headers["Cache-Control"], "no-store")
        self.assertEqual(login.headers["X-Content-Type-Options"], "nosniff")

    def test_dev_provider_is_disabled_unless_explicitly_enabled(self) -> None:
        with patch.dict(os.environ, {"DEV_LOGIN_ENABLED": "false"}):
            app = create_app(MemoryDatabase(), testing=True)
        client = app.test_client()

        options = client.get("/api/v1/auth/options")
        start = client.get("/api/v1/auth/dev/start?identity=student")

        self.assertEqual(options.status_code, 200)
        self.assertEqual(
            options.json["data"]["providers"],
            [{
                "id": "google",
                "label": "Google OAuth2",
                "start_url": "/api/v1/auth/google/start",
                "available": False,
                "status": "not_configured",
            }],
        )
        self.assertEqual(
            options.json["data"]["planned_providers"],
            [{"id": "microsoft-entra", "label": "Microsoft Entra", "status": "planned"}],
        )
        self.assertEqual(start.status_code, 404)
        self.assertEqual(start.json["error"]["code"], "provider_unavailable")

    def test_google_preview_uses_state_pkce_and_server_side_secret_boundary(self) -> None:
        callback_uri = "http://localhost/api/v1/auth/google/callback"
        self.database.provider_config = {
            "enabled": True,
            "client_id": "public-client",
            "client_secret": "must-not-leak",
            "authorization_endpoint": "https://accounts.google.com/o/oauth2/v2/auth",
            "redirect_uris": [callback_uri],
        }
        self.database.rows["auth_identities"].append({
            "id": 51,
            "user_id": 7,
            "provider": "google",
            "provider_subject": "google-subject",
            "email": "kai@example.test",
        })

        options = self.client.get("/api/v1/auth/options")
        google = next(
            item for item in options.json["data"]["providers"]
            if item["id"] == "google"
        )
        self.assertTrue(google["available"])
        self.assertEqual(google["status"], "preview")
        self.assertNotIn("must-not-leak", options.get_data(as_text=True))

        start = self.client.get(google["start_url"])
        self.assertEqual(start.status_code, 302)
        query = parse_qs(urlsplit(start.headers["Location"]).query)
        self.assertEqual(query["client_id"], ["public-client"])
        self.assertEqual(query["redirect_uri"], [callback_uri])
        self.assertEqual(query["response_type"], ["code"])
        self.assertEqual(query["code_challenge_method"], ["S256"])
        self.assertNotIn("code_verifier", query)

        with self.client.session_transaction() as browser_session:
            flow = browser_session["_auth_flows"]["google"]
        self.assertEqual(
            flow["redirect_uri"],
            callback_uri,
        )
        self.assertEqual(
            self.client.get(
                f"/api/v1/auth/google/callback?state={flow['state']}&code=authorization-code"
            ).status_code,
            200,
        )
        self.assertEqual(
            self.database.provider_exchange,
            {
                "code": "authorization-code",
                "code_verifier": flow["verifier"],
                "nonce": flow["nonce"],
                "redirect_uri": callback_uri,
            },
        )

    def test_google_start_rejects_unregistered_redirect_and_callback_checks_state(self) -> None:
        self.database.provider_config = {
            "enabled": True,
            "client_id": "public-client",
            "authorization_endpoint": "https://accounts.google.com/o/oauth2/v2/auth",
            "redirect_uris": ["https://unrelated.example.test/callback"],
        }
        start = self.client.get("/api/v1/auth/google/start")
        self.assertEqual(start.status_code, 503)

        callback = self.client.get(
            "/api/v1/auth/google/callback?state=not-a-session-state&code=code"
        )
        self.assertEqual(callback.status_code, 401)
        self.assertEqual(callback.json["error"]["code"], "invalid_credentials")
        self.assertEqual(self.database.provider_exchange, {})

    def test_google_login_post_starts_the_same_real_provider_flow(self) -> None:
        self.database.provider_config = {
            "enabled": True,
            "client_id": "public-client",
            "authorization_endpoint": "https://accounts.google.com/o/oauth2/v2/auth",
            "redirect_uris": ["http://localhost/api/v1/auth/google/callback"],
        }

        response = self.client.post("/api/v1/auth/login", json={"provider": "google"})
        invalid_identity = self.client.post(
            "/api/v1/auth/login",
            json={"provider": "google", "identity": "student"},
        )

        self.assertEqual(response.status_code, 302)
        self.assertIn("accounts.google.com/o/oauth2/v2/auth", response.headers["Location"])
        self.assertEqual(invalid_identity.status_code, 400)

    def test_google_exchange_401_is_mapped_to_generic_provider_failure(self) -> None:
        self.database.provider_config = {
            "enabled": True,
            "client_id": "public-client",
            "authorization_endpoint": "https://accounts.google.com/o/oauth2/v2/auth",
            "redirect_uris": ["http://localhost/api/v1/auth/google/callback"],
        }
        response = requests.Response()
        response.status_code = 401
        self.database.provider_error = requests.HTTPError(
            "Provider rejected authorization",
            response=response,
        )
        self.client.get("/api/v1/auth/google/start")
        with self.client.session_transaction() as browser_session:
            state = browser_session["_auth_flows"]["google"]["state"]

        with self.assertLogs("api.routes", level="ERROR"):
            callback = self.client.get(
                f"/api/v1/auth/google/callback?state={state}&code=authorization-code"
            )

        self.assertEqual(callback.status_code, 401)
        self.assertEqual(callback.json["error"], {
            "code": "invalid_credentials",
            "message": "Provider authentication failed.",
        })
        self.assertEqual(
            self.database.rows["auth_attempts"][-1]["reason_code"],
            "provider_authentication_failed",
        )

    def test_dev_provider_is_refused_in_production(self) -> None:
        with patch.dict(
            os.environ,
            {"DEV_LOGIN_ENABLED": "true", "APP_ENV": "production"},
        ):
            with self.assertRaisesRegex(RuntimeError, "cannot be enabled in production"):
                create_app(MemoryDatabase(), testing=True)

    def test_dev_provider_authenticates_seeded_identity_and_logs_attempts(self) -> None:
        self.database.rows["auth_identities"].append({
            "id": 51,
            "user_id": 7,
            "provider": "dev",
            "provider_subject": "demo-student",
            "email": "student@example.test",
        })
        self.database.rows["roles"].append({"id": 3, "role_key": "student"})
        self.database.rows["user_roles"].append({
            "id": 4,
            "user_id": 7,
            "role_id": 3,
        })
        with patch.dict(os.environ, {"DEV_LOGIN_ENABLED": "true", "APP_ENV": "development"}):
            app = create_app(self.database, testing=True)
        client = app.test_client()

        options = client.get("/api/v1/auth/options")
        self.assertEqual(options.json["data"]["providers"][0]["id"], "dev")
        start = client.get("/api/v1/auth/dev/start?identity=student")
        self.assertEqual(start.status_code, 302)
        callback = client.get(start.headers["Location"])

        self.assertEqual(callback.status_code, 200)
        self.assertEqual(callback.json["data"], {
            "status": "authenticated",
            "roles": ["student", "tutee"],
        })
        session_status = client.get("/api/v1/session")
        self.assertTrue(session_status.json["authenticated"])
        self.assertEqual(
            [row["outcome"] for row in self.database.rows["auth_attempts"]],
            ["started", "success"],
        )
        self.assertEqual(
            self.database.rows["auth_attempts"][0]["client_ip_prefix"],
            "127.0.0.0/24",
        )
        self.assertNotIn(
            "127.0.0.1",
            self.database.rows["auth_attempts"][0]["client_ip_prefix"],
        )

    def test_generic_oidc_builds_pkce_url_and_rejects_non_allowlisted_redirect(self) -> None:
        settings = {
            "OIDC_PROVIDER_ID": "sample",
            "OIDC_ISSUER": "https://issuer.example.test",
            "OIDC_CLIENT_ID": "public-client-id",
            "OIDC_CLIENT_SECRET": "not-used-by-this-test",
            "OIDC_AUTHORIZATION_ENDPOINT": "https://issuer.example.test/authorize",
            "OIDC_TOKEN_ENDPOINT": "https://issuer.example.test/token",
            "OIDC_JWKS_URI": "https://issuer.example.test/jwks",
            "OIDC_REDIRECT_URI": "https://host.example.test/api/v1/auth/sample/callback",
            "OIDC_REDIRECT_URI_ALLOWLIST": (
                "https://host.example.test/api/v1/auth/sample/callback"
            ),
        }
        adapter = OpenIDConnectAdapter(settings)
        url = adapter.authorization_url(
            state="state",
            nonce="nonce",
            verifier="v" * 64,
            redirect_uri=settings["OIDC_REDIRECT_URI"],
        )
        query = parse_qs(urlsplit(url).query)

        self.assertEqual(query["state"], ["state"])
        self.assertEqual(query["nonce"], ["nonce"])
        self.assertEqual(query["code_challenge_method"], ["S256"])
        self.assertEqual(query["redirect_uri"], [settings["OIDC_REDIRECT_URI"]])
        with self.assertRaisesRegex(ProviderError, "redirect_uri_not_allowed"):
            adapter.authorization_url(
                state="state",
                nonce="nonce",
                verifier="v" * 64,
                redirect_uri="https://attacker.example.test/callback",
            )

    def test_google_is_not_enabled_by_the_host_oidc_adapter(self) -> None:
        settings = {
            "OIDC_PROVIDER_ID": "google",
            "OIDC_ISSUER": "https://accounts.google.com",
            "OIDC_CLIENT_ID": "public-client-id",
            "OIDC_CLIENT_SECRET": "not-configured-here",
            "OIDC_AUTHORIZATION_ENDPOINT": "https://accounts.google.com/authorize",
            "OIDC_TOKEN_ENDPOINT": "https://accounts.google.com/token",
            "OIDC_JWKS_URI": "https://accounts.google.com/jwks",
            "OIDC_REDIRECT_URI": "https://host.example.test/callback",
            "OIDC_REDIRECT_URI_ALLOWLIST": "https://host.example.test/callback",
        }
        with self.assertRaisesRegex(RuntimeError, "Google is reserved"):
            from api.auth_providers import build_provider_registry

            build_provider_registry(settings)

        settings["OIDC_PROVIDER_ID"] = "workforce"
        with self.assertRaisesRegex(RuntimeError, "Google is reserved"):
            build_provider_registry(settings)

    def test_unknown_dev_identity_is_created_pending_without_authenticating(self) -> None:
        with patch.dict(os.environ, {"DEV_LOGIN_ENABLED": "true", "APP_ENV": "development"}):
            app = create_app(self.database, testing=True)
        client = app.test_client()

        start = client.get("/api/v1/auth/dev/start?identity=teacher")
        callback = client.get(start.headers["Location"])

        self.assertEqual(callback.status_code, 202)
        self.assertEqual(callback.json["data"]["status"], "signup_required")
        self.assertNotIn("user_id", callback.json["data"])
        created_user = self.database.rows["users"][-1]
        self.assertEqual(created_user["account_status"], "pending")
        self.assertIsNone(created_user["external_subject"])
        self.assertEqual(
            self.database.rows["auth_identities"][-1]["provider_subject"],
            "demo-teacher",
        )
        signup = client.post(
            "/api/v1/auth/signup",
            json={"terms_accepted": True, "terms_version": "demo-v1"},
        )
        self.assertEqual(signup.status_code, 202)
        self.assertEqual(signup.json["data"]["status"], "pending_review")
        self.assertEqual(created_user["terms_version"], "demo-v1")
        self.assertEqual(created_user["account_status"], "pending")
        self.assertFalse(client.get("/api/v1/session").json["authenticated"])

    def test_admin_login_returns_generic_failure_and_logs_attempts(self) -> None:
        dummy_hash = hash_password("demo-password-that-is-long-enough")
        self.database.rows["admin_credentials"].append({
            "id": 61,
            "user_id": 7,
            "username": "demo-admin",
            "password_hash": dummy_hash,
        })

        unknown = self.client.post(
            "/api/v1/auth/admin/login",
            json={"username": "missing-admin", "password": "demo-password-that-is-long-enough"},
        )
        wrong_password = self.client.post(
            "/api/v1/auth/admin/login",
            json={"username": "demo-admin", "password": "incorrect-password-123"},
        )

        self.assertEqual(unknown.status_code, 401)
        self.assertEqual(unknown.json, wrong_password.json)
        self.assertEqual(unknown.json["error"]["code"], "invalid_credentials")
        self.assertEqual(
            [row["reason_code"] for row in self.database.rows["auth_attempts"]],
            ["invalid_credentials", "invalid_credentials"],
        )

    def test_admin_login_requires_active_admin_and_rotates_into_session(self) -> None:
        password = "demo-password-that-is-long-enough"
        self.database.rows["admin_credentials"].append({
            "id": 62,
            "user_id": 7,
            "username": "demo-admin",
            "password_hash": hash_password(password),
        })
        self.database.rows["roles"].append({"id": 9, "role_key": "admin"})
        self.database.rows["user_roles"].append({
            "id": 10,
            "user_id": 7,
            "role_id": 9,
        })
        with self.client.session_transaction() as browser_session:
            browser_session["untrusted_pre_auth_value"] = "keep out"
        previous_cookie = self.client.get_cookie("mentos_session").value

        response = self.client.post(
            "/api/v1/auth/admin/login",
            json={"username": "demo-admin", "password": password},
        )

        self.assertEqual(response.status_code, 200)
        self.assertNotEqual(
            self.client.get_cookie("mentos_session").value,
            previous_cookie,
        )
        self.assertEqual(response.json["data"]["roles"], ["admin", "tutee"])
        self.assertTrue(self.client.get("/api/v1/session").json["authenticated"])
        self.assertEqual(self.database.rows["auth_attempts"][-1]["outcome"], "success")
        self.assertNotIn("password", response.get_data(as_text=True))
        self.assertNotIn("scrypt$", response.get_data(as_text=True))

        self.database.rows["users"][0]["account_status"] = "suspended"
        denied = self.client.post(
            "/api/v1/auth/admin/login",
            json={"username": "demo-admin", "password": password},
        )
        self.assertEqual(denied.status_code, 401)
        self.assertEqual(denied.json["error"]["code"], "invalid_credentials")

    def test_admin_login_inputs_are_strict_and_throttled_by_username(self) -> None:
        invalid = self.client.post(
            "/api/v1/auth/admin/login",
            json={"username": "admin';--", "password": "demo-password-that-is-long-enough"},
        )
        self.assertEqual(invalid.status_code, 400)
        self.assertEqual(invalid.json["error"]["code"], "bad_request")

        for index in range(5):
            response = self.client.post(
                "/api/v1/auth/admin/login",
                json={
                    "username": "shared-admin",
                    "password": "demo-password-that-is-long-enough",
                },
                environ_overrides={"REMOTE_ADDR": f"198.51.100.{index + 1}"},
            )
            self.assertEqual(response.status_code, 401)
        limited = self.client.post(
            "/api/v1/auth/admin/login",
            json={
                "username": "shared-admin",
                "password": "demo-password-that-is-long-enough",
            },
            environ_overrides={"REMOTE_ADDR": "198.51.100.20"},
        )
        self.assertEqual(limited.status_code, 429)
        self.assertEqual(
            self.database.rows["auth_attempts"][-1]["reason_code"],
            "rate_limited",
        )

    def test_demo_seed_is_idempotent_and_hashes_runtime_admin_password(self) -> None:
        self.database.rows["roles"] = [
            {"id": 1, "role_key": "student"},
            {"id": 2, "role_key": "tutor"},
            {"id": 3, "role_key": "tutee"},
            {"id": 4, "role_key": "teacher"},
            {"id": 5, "role_key": "admin"},
        ]
        password = "temporary-demo-admin-password"

        seed_demo_accounts(self.database, password)
        seed_demo_accounts(self.database, password)

        self.assertEqual(len(self.database.rows["auth_identities"]), 5)
        self.assertEqual(len(self.database.rows["user_roles"]), 6)
        self.assertEqual(len(self.database.rows["admin_credentials"]), 1)
        encoded_hash = self.database.rows["admin_credentials"][0]["password_hash"]
        self.assertNotEqual(encoded_hash, password)
        self.assertTrue(
            verify_password(
                password,
                encoded_hash,
                self.app.extensions["admin_dummy_password_hash"],
            )
        )

    def test_health_hides_dbserv_failure_details_and_caches_degraded_state(self) -> None:
        self.database.health_error = RuntimeError("private upstream detail")

        with self.assertLogs("api", level="ERROR"):
            response = self.client.get("/api/v1/health")
        self.assertEqual(response.json, {"status": "degraded"})
        self.assertNotIn("private upstream detail", response.get_data(as_text=True))

        cached = self.client.get("/api/v1/health")
        self.assertEqual(cached.json, {"status": "degraded"})
        self.assertEqual(self.database.health_checks, 1)

        self.app.extensions["dbserv_health_cache"]["expires_at"] = 0
        with patch("api.routes.monotonic", return_value=HEALTH_CACHE_SECONDS + 1):
            with self.assertLogs("api", level="ERROR"):
                refreshed = self.client.get("/api/v1/health")
        self.assertEqual(refreshed.json, {"status": "degraded"})
        self.assertEqual(self.database.health_checks, 2)

    def test_profile_and_learning_request_require_session(self) -> None:
        unauthenticated = self.client.get("/api/v1/me")
        self.assertEqual(unauthenticated.status_code, 401)
        self.assertIn("Cookie", unauthenticated.vary)
        self.authenticate()

        profile = self.client.get("/api/v1/me")
        self.assertEqual(profile.status_code, 200)
        self.assertIn("Cookie", profile.vary)
        self.assertEqual(profile.json["data"]["user"]["id"], 7)
        self.assertEqual(
            set(profile.json["data"]["user"]),
            {"id", "display_name"},
        )
        self.assertNotIn("email", profile.json["data"]["user"])
        self.assertNotIn("external_subject", profile.json["data"]["user"])
        self.assertEqual(profile.json["data"]["roles"], ["tutee"])
        self.assertEqual(
            profile.json["data"]["student_profile"]["strand"],
            "IT",
        )
        self.assertEqual(
            set(profile.json["data"]["student_profile"]),
            {"grade_level", "strand", "timezone", "learning_preferences"},
        )

        created = self.client.post(
            "/api/v1/learning-requests",
            json={
                "subject_id": 3,
                "current_proficiency": 2,
                "preferred_format": "one_to_one",
            },
        )
        self.assertEqual(created.status_code, 201)
        stored = self.database.rows["learning_requests"][0]
        self.assertEqual(stored["tutee_user_id"], 7)
        self.assertEqual(stored["request_status"], "open")

        listed = self.client.get("/api/v1/learning-requests")
        self.assertEqual(len(listed.json["data"]), 1)
        self.assertEqual(
            set(listed.json["data"][0]),
            {
                "id",
                "subject_id",
                "current_proficiency",
                "preferred_format",
                "request_status",
            },
        )

    def test_inactive_account_cannot_use_authenticated_routes(self) -> None:
        self.authenticate()
        self.database.rows["users"][0]["account_status"] = "suspended"

        response = self.client.get("/api/v1/me")

        self.assertEqual(response.status_code, 403)
        self.assertEqual(response.json["error"]["code"], "account_inactive")
        self.assertFalse(self.client.get("/api/v1/session").json["authenticated"])

    def test_learning_request_serializer_excludes_unapproved_fields(self) -> None:
        self.authenticate()
        self.database.rows["learning_requests"] = [{
            "id": 23,
            "tutee_user_id": 7,
            "subject_id": 3,
            "topic_id": 4,
            "current_proficiency": 2,
            "requested_help": "Review examples",
            "preferred_format": "one_to_one",
            "request_status": "open",
            "created_at": "2026-11-02T10:00:00Z",
            "updated_at": "2026-11-02T10:00:00Z",
            "private_note": "do not expose",
            "email": "private@example.test",
        }]

        response = self.client.get("/api/v1/learning-requests")

        self.assertEqual(
            set(response.json["data"][0]),
            {
                "id",
                "subject_id",
                "topic_id",
                "current_proficiency",
                "requested_help",
                "preferred_format",
                "request_status",
                "created_at",
                "updated_at",
            },
        )
        self.assertNotIn("email", response.json["data"][0])

    def test_profile_rejects_role_escalation_fields(self) -> None:
        self.authenticate()
        response = self.client.patch("/api/v1/me", json={"account_status": "admin"})
        self.assertEqual(response.status_code, 400)

    def test_session_cookie_is_opaque_and_http_only(self) -> None:
        self.assertEqual(self.app.config["SESSION_COOKIE_NAME"], "mentos_session")
        with self.client.session_transaction() as browser_session:
            browser_session["user_id"] = 7
        cookie = self.client.get_cookie("mentos_session")
        self.assertIsNotNone(cookie)
        self.assertTrue(cookie.http_only)
        self.assertNotIn("user_id", cookie.value)
        self.assertEqual(len(self.database.rows["web_sessions"]), 1)
        self.assertNotEqual(
            self.database.rows["web_sessions"][0]["session_key"],
            cookie.value,
        )
        self.assertEqual(
            self.database.rows["web_sessions"][0]["session_key"],
            hashlib.sha256(cookie.value.encode("utf-8")).hexdigest(),
        )

    def test_expiration_uses_utc_when_local_timezone_is_not_utc(self) -> None:
        original_tz = os.environ.get("TZ")
        os.environ["TZ"] = "America/Los_Angeles"
        time.tzset()
        self.addCleanup(self._restore_timezone, original_tz)

        raw_session_id = "legacy-session-key"
        expires_at = datetime.now(timezone.utc) + timedelta(hours=1)
        self.database.rows["web_sessions"].append({
            "id": 41,
            "session_key": raw_session_id,
            "session_data": {"user_id": 7, "authenticated_at": "test"},
            "expires_at": expires_at.replace(tzinfo=None).isoformat(sep=" "),
        })
        self.client.set_cookie("mentos_session", raw_session_id)

        active = self.client.get("/api/v1/session")

        self.assertTrue(active.json["authenticated"])
        self.assertEqual(
            self.database.rows["web_sessions"][0]["session_key"],
            hashlib.sha256(raw_session_id.encode("utf-8")).hexdigest(),
        )

        self.database.rows["web_sessions"][0]["expires_at"] = (
            datetime.now(timezone.utc) - timedelta(hours=1)
        ).replace(tzinfo=None).isoformat(sep=" ")
        expired = self.client.get("/api/v1/session")

        self.assertFalse(expired.json["authenticated"])
        self.assertEqual(self.database.rows["web_sessions"], [])

    @staticmethod
    def _restore_timezone(original_tz: str | None) -> None:
        if original_tz is None:
            os.environ.pop("TZ", None)
        else:
            os.environ["TZ"] = original_tz
        time.tzset()

    def test_deleting_session_revokes_server_side_record(self) -> None:
        self.authenticate()
        self.assertEqual(len(self.database.rows["web_sessions"]), 1)

        response = self.client.delete("/api/v1/session")

        self.assertEqual(response.status_code, 204)
        self.assertEqual(self.database.rows["web_sessions"], [])
        self.assertFalse(self.client.get("/api/v1/session").json["authenticated"])

    def test_upstream_errors_are_logged_and_return_stable_sanitized_errors(self) -> None:
        self.authenticate()
        upstream_429 = requests.Response()
        upstream_429.status_code = 429
        upstream_500 = requests.Response()
        upstream_500.status_code = 500
        cases = (
            (requests.Timeout("private upstream detail"), 503, "dbserv_timeout"),
            (requests.ConnectionError("private upstream detail"), 503, "dbserv_unavailable"),
            (
                requests.HTTPError("private upstream detail", response=upstream_429),
                503,
                "dbserv_rate_limited",
            ),
            (
                requests.HTTPError("private upstream detail", response=upstream_500),
                502,
                "dbserv_error",
            ),
        )

        for error, status, code in cases:
            with self.subTest(code=code):
                self.database.read_errors["student_profiles"] = error
                with self.assertLogs("api.app", level="ERROR") as captured:
                    response = self.client.get("/api/v1/me")
                self.database.read_errors.clear()

                self.assertEqual(response.status_code, status)
                self.assertEqual(response.json["error"]["code"], code)
                self.assertNotIn("private upstream detail", response.get_data(as_text=True))
                self.assertIn("private upstream detail", "\n".join(captured.output))
                self.assertEqual(response.headers["Cache-Control"], "no-store")
                self.assertEqual(response.headers["X-Content-Type-Options"], "nosniff")

    def test_rate_limits_cover_api_and_tighten_session_and_login_routes(self) -> None:
        api_ip = "192.0.2.110"
        for _ in range(120):
            response = self.client.get(
                "/api/v1/health",
                environ_overrides={"REMOTE_ADDR": api_ip},
            )
            self.assertEqual(response.status_code, 200)

        limited = self.client.get(
            "/api/v1/health",
            environ_overrides={"REMOTE_ADDR": api_ip},
        )
        self.assertEqual(limited.status_code, 429)
        self.assertEqual(limited.json["error"]["code"], "rate_limited")
        self.assertEqual(limited.headers["Cache-Control"], "no-store")
        self.assertEqual(limited.headers["X-Content-Type-Options"], "nosniff")
        self.assertEqual(
            self.client.get(
                "/api/v1/health",
                environ_overrides={"REMOTE_ADDR": "192.0.2.111"},
            ).status_code,
            200,
        )

        session_ip = "192.0.2.112"
        for _ in range(30):
            self.assertEqual(
                self.client.get(
                    "/api/v1/session",
                    environ_overrides={"REMOTE_ADDR": session_ip},
                ).status_code,
                200,
            )
        self.assertEqual(
            self.client.get(
                "/api/v1/session",
                environ_overrides={"REMOTE_ADDR": session_ip},
            ).status_code,
            429,
        )

        login_ip = "192.0.2.113"
        for _ in range(10):
            self.assertEqual(
                self.client.post(
                    "/api/v1/auth/login",
                    environ_overrides={"REMOTE_ADDR": login_ip},
                ).status_code,
                400,
            )
        self.assertEqual(
            self.client.post(
                "/api/v1/auth/login",
                environ_overrides={"REMOTE_ADDR": login_ip},
            ).status_code,
            429,
        )

    def test_proxy_fix_is_explicit_and_rate_limit_uses_trusted_forwarded_ip(self) -> None:
        with patch.dict(os.environ, {"TRUST_PROXY": "false"}):
            default_app = create_app(MemoryDatabase(), testing=True)
        self.assertNotIsInstance(default_app.wsgi_app, ProxyFix)

        with patch.dict(os.environ, {"TRUST_PROXY": "true"}):
            proxied_app = create_app(MemoryDatabase(), testing=True)
        self.assertIsInstance(proxied_app.wsgi_app, ProxyFix)
        client = proxied_app.test_client()
        remote_addr = "127.0.0.1"
        first_ip_headers = {"X-Forwarded-For": "198.51.100.40"}
        for _ in range(120):
            self.assertEqual(
                client.get(
                    "/api/v1/health",
                    headers=first_ip_headers,
                    environ_overrides={"REMOTE_ADDR": remote_addr},
                ).status_code,
                200,
            )
        self.assertEqual(
            client.get(
                "/api/v1/health",
                headers={"X-Forwarded-For": "198.51.100.41"},
                environ_overrides={"REMOTE_ADDR": remote_addr},
            ).status_code,
            200,
        )

    def test_main_runs_waitress_on_loopback(self) -> None:
        with (
            patch.dict(os.environ, {"PORT": "8123", "TRUST_PROXY": "false"}),
            patch("api.app.serve") as waitress_serve,
        ):
            main()

        served_app = waitress_serve.call_args.args[0]
        self.assertIsInstance(served_app, Flask)
        self.assertEqual(waitress_serve.call_args.kwargs, {"host": "127.0.0.1", "port": 8123})


if __name__ == "__main__":
    unittest.main()
