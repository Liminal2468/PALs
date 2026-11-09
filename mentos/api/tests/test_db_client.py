from __future__ import annotations

import tempfile
import unittest
import os
from pathlib import Path
from typing import Any
from unittest.mock import patch

import jwt
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.primitives.serialization import (
    Encoding,
    NoEncryption,
    PrivateFormat,
    PublicFormat,
)

from api.db.db import DB


class MockResponse:
    def __init__(self, status_code: int, payload: Any = None):
        self.status_code = status_code
        self.payload = payload
        self.content = b"" if status_code == 204 else b"response"

    def raise_for_status(self) -> None:
        if self.status_code >= 400:
            raise RuntimeError(f"HTTP {self.status_code}")

    def json(self) -> Any:
        return self.payload


class MockSession:
    def __init__(self):
        self.calls: list[tuple[str, str, dict[str, Any]]] = []

    def request(self, method: str, url: str, **kwargs: Any) -> MockResponse:
        self.calls.append((method, url, kwargs))
        if method == "DELETE":
            return MockResponse(204)
        return MockResponse(200, {"data": {"method": method}})


class DBClientTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.private_key_path = Path(self.temp_dir.name) / "host-private.pem"
        private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        self.private_key_path.write_bytes(private_key.private_bytes(
            encoding=Encoding.PEM,
            format=PrivateFormat.PKCS8,
            encryption_algorithm=NoEncryption(),
        ))
        self.public_key = private_key.public_key().public_bytes(
            encoding=Encoding.PEM,
            format=PublicFormat.SubjectPublicKeyInfo,
        )
        self.session = MockSession()
        self.db = DB(
            host="http://db.example.test",
            private_key_path=self.private_key_path,
            key_id="host-key-id",
            issuer="mentos-host",
            audience="mentos-db",
            token_ttl_seconds=120,
            session=self.session,  # type: ignore[arg-type]
        )

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_crud_and_aggregation_use_server_paths_and_jwt_auth(self) -> None:
        self.assertEqual(self.db.create("students", {"name": "Kai"}), {"method": "POST"})
        self.assertEqual(self.db.read("students", "id/7", params={"status": "active"}), {"method": "GET"})
        self.assertEqual(self.db.update("students", 7, {"name": "Lee"}), {"method": "PATCH"})
        self.assertEqual(
            self.db.aggregate(
                "students",
                {"group_by": "status", "metrics": [{"operation": "count", "field": "*"}]},
            ),
            {"method": "POST"},
        )
        self.assertIsNone(self.db.delete("students", 7))

        self.assertEqual(
            [(method, url) for method, url, _ in self.session.calls],
            [
                ("POST", "http://db.example.test/db/students"),
                ("GET", "http://db.example.test/db/students/id%2F7"),
                ("PATCH", "http://db.example.test/db/students/7"),
                ("POST", "http://db.example.test/db/students/aggregate"),
                ("DELETE", "http://db.example.test/db/students/7"),
            ],
        )
        self.assertEqual(self.session.calls[0][2]["json"], {"name": "Kai"})
        self.assertEqual(self.session.calls[1][2]["params"], {"status": "active"})
        self.assertEqual(self.session.calls[2][2]["json"], {"name": "Lee"})
        self.assertEqual(
            self.session.calls[3][2]["json"],
            {"group_by": "status", "metrics": [{"operation": "count", "field": "*"}]},
        )
        expected_claims = [
            ("db:write", "create"),
            ("db:read", "read"),
            ("db:write", "update"),
            ("db:read", "aggregate"),
            ("db:write", "delete"),
        ]
        for (_, _, kwargs), (expected_scope, expected_action) in zip(
            self.session.calls, expected_claims
        ):
            token = kwargs["headers"]["Authorization"].removeprefix("Bearer ")
            claims = jwt.decode(
                token,
                key=self.public_key,
                algorithms=["RS256"],
                audience="mentos-db",
                issuer="mentos-host",
            )
            self.assertEqual(claims["scope"], expected_scope)
            self.assertEqual(claims["resource"], "students")
            self.assertEqual(claims["action"], expected_action)
            self.assertLessEqual(claims["exp"] - claims["iat"], 120)
            self.assertEqual(kwargs["headers"]["Accept"], "application/json")

    def test_missing_signing_configuration_fails_explicitly(self) -> None:
        missing_config = Path(self.temp_dir.name) / "missing-config"
        with (
            patch("api.db.db.CONFIG_PATH", missing_config),
            patch("api.db.db.PROJECT_CONFIG_PATH", missing_config),
        ):
            db = DB(host="http://db.example.test", session=self.session)  # type: ignore[arg-type]
            with self.assertRaisesRegex(RuntimeError, "JWT_ISSUER"):
                db.read("students")

    def test_health_checks_use_liveness_and_signed_database_endpoint(self) -> None:
        from api.healthcheck import check_dbserv

        check_dbserv(self.db)

        self.assertEqual(
            [(method, url) for method, url, _ in self.session.calls],
            [
                ("GET", "http://db.example.test/db/health"),
                ("GET", "http://db.example.test/db/health/database"),
            ],
        )
        health_call = self.session.calls[1][2]
        token = health_call["headers"]["Authorization"].removeprefix("Bearer ")
        claims = jwt.decode(
            token,
            key=self.public_key,
            algorithms=["RS256"],
            audience="mentos-db",
            issuer="mentos-host",
        )
        self.assertEqual(claims["resource"], "database")
        self.assertEqual(claims["action"], "health")

    def test_google_auth_calls_use_distinct_scoped_capabilities(self) -> None:
        self.assertEqual(self.db.get_provider_config("google"), {"method": "GET"})
        self.assertEqual(
            self.db.exchange_provider_code("google", {"code": "one-time-code"}),
            {"method": "POST"},
        )

        self.assertEqual(
            [(method, url) for method, url, _ in self.session.calls],
            [
                ("GET", "http://db.example.test/auth/v1/providers/google/config"),
                ("POST", "http://db.example.test/auth/v1/providers/google/exchange"),
            ],
        )
        self.assertEqual(
            self.session.calls[1][2]["json"],
            {"code": "one-time-code"},
        )
        for (_, _, kwargs), expected_action in zip(
            self.session.calls,
            ("provider_config", "provider_exchange"),
        ):
            token = kwargs["headers"]["Authorization"].removeprefix("Bearer ")
            claims = jwt.decode(
                token,
                key=self.public_key,
                algorithms=["RS256"],
                audience="mentos-db",
                issuer="mentos-host",
            )
            self.assertEqual(claims["scope"], "auth:provider")
            self.assertEqual(claims["resource"], "authentication")
            self.assertEqual(claims["action"], expected_action)

    def test_project_env_file_configures_db_client(self) -> None:
        config_path = Path(self.temp_dir.name) / ".env"
        config_path.write_text(
            "\n".join(
                [
                    "DBSERV_URL=http://db.example.test",
                    f"JWT_PRIVATE_KEY_PATH={self.private_key_path}",
                    "JWT_KEY_ID=demo-key",
                    "JWT_ISSUER=mentos-host",
                    "JWT_AUDIENCE=mentos-db",
                    "JWT_TOKEN_TTL_SECONDS=180",
                ],
            ),
            encoding="utf-8",
        )
        with patch("api.db.db.PROJECT_CONFIG_PATH", config_path):
            db = DB(session=self.session)  # type: ignore[arg-type]

        self.assertEqual(db.host, "http://db.example.test")
        self.assertEqual(db.key_id, "demo-key")
        self.assertEqual(db.token_ttl_seconds, 180)

    def test_legacy_host_setting_is_supported_with_one_warning(self) -> None:
        missing_config = Path(self.temp_dir.name) / "missing-config"
        with (
            patch("api.db.db.CONFIG_PATH", missing_config),
            patch("api.db.db.PROJECT_CONFIG_PATH", missing_config),
            patch("api.db.db._legacy_dbserv_url_warning_emitted", False),
            patch.dict(os.environ, {"HOST": "http://legacy.example.test"}, clear=True),
            self.assertLogs("api.db.db", level="WARNING") as captured,
        ):
            legacy = DB(session=self.session)  # type: ignore[arg-type]
            DB(session=self.session)  # type: ignore[arg-type]

        self.assertEqual(legacy.host, "http://legacy.example.test")
        self.assertEqual(len(captured.records), 1)
        self.assertIn("DBSERV_URL", captured.records[0].getMessage())


if __name__ == "__main__":
    unittest.main()
