"""Manually seed the five fake development identities."""

from __future__ import annotations

import os
import re
from typing import Any

from api.db.db import db
from api.passwords import hash_password


_DEMO_ACCOUNTS = (
    ("student", "demo-student", "student@example.test", "Demo Student"),
    ("tutor", "demo-tutor", "tutor@example.test", "Demo Tutor"),
    ("tutee", "demo-tutee", "tutee@example.test", "Demo Tutee"),
    ("teacher", "demo-teacher", "teacher@example.test", "Demo Teacher"),
    ("admin", "demo-admin", "admin@example.test", "Demo Admin"),
)
_USERNAME = "demo-admin"
_PASSWORD_PATTERN = re.compile(r"^[\x20-\x7e]{12,128}$")


def seed_demo_accounts(database: Any, admin_password: str) -> None:
    if not isinstance(admin_password, str) or not _PASSWORD_PATTERN.fullmatch(admin_password):
        raise ValueError("DEMO_ADMIN_PASSWORD must be 12-128 printable ASCII characters.")

    role_rows = database.read("roles")
    role_ids = {
        row["role_key"]: row["id"]
        for row in role_rows
        if isinstance(row.get("role_key"), str) and isinstance(row.get("id"), int)
    }
    missing_roles = {role for role, *_ in _DEMO_ACCOUNTS} - role_ids.keys()
    if missing_roles:
        raise RuntimeError(
            "Apply the authentication migration before seeding demo roles."
        )

    admin_user_id: int | None = None
    for role, subject, email, display_name in _DEMO_ACCOUNTS:
        identities = database.read(
            "auth_identities",
            params={"provider": "dev", "provider_subject": subject},
        )
        if identities:
            user_id = identities[0]["user_id"]
            user = database.read("users", user_id)
            if not isinstance(user, dict):
                raise RuntimeError("Demo identity references a missing user.")
            database.update(
                "users",
                user_id,
                {
                    "email": email,
                    "display_name": display_name,
                    "account_status": "active",
                },
            )
        else:
            created = database.create(
                "users",
                {
                    "external_subject": None,
                    "email": email,
                    "display_name": display_name,
                    "account_status": "active",
                },
            )
            user_id = created.get("id")
            if not isinstance(user_id, int):
                raise RuntimeError("Database did not return the demo user ID.")
            database.create(
                "auth_identities",
                {
                    "user_id": user_id,
                    "provider": "dev",
                    "provider_subject": subject,
                },
            )

        assignments = database.read(
            "user_roles",
            params={"user_id": user_id, "role_id": role_ids[role]},
        )
        if not assignments:
            database.create(
                "user_roles",
                {"user_id": user_id, "role_id": role_ids[role]},
            )
        if role == "admin":
            admin_user_id = user_id

    if admin_user_id is None:
        raise RuntimeError("Demo admin account was not created.")
    credentials = database.read(
        "admin_credentials",
        params={"username": _USERNAME},
    )
    password_hash = hash_password(admin_password)
    if credentials:
        database.update(
            "admin_credentials",
            credentials[0]["id"],
            {"user_id": admin_user_id, "password_hash": password_hash},
        )
    else:
        database.create(
            "admin_credentials",
            {
                "user_id": admin_user_id,
                "username": _USERNAME,
                "password_hash": password_hash,
            },
        )


def main() -> None:
    password = os.environ.get("DEMO_ADMIN_PASSWORD", "")
    seed_demo_accounts(db, password)


if __name__ == "__main__":
    main()
