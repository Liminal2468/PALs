from __future__ import annotations

import sys
from pathlib import Path
from typing import Any


ROOT_DIR = Path(__file__).resolve().parents[2]
if str(ROOT_DIR) not in sys.path:
    sys.path.insert(0, str(ROOT_DIR))

from mentos.api.back.auth.encryter import hash_password, verify_password
from utils.dbmgr import db_manager


def create_account(
    username: str,
    password: str,
    employee_id: int | None = None,
) -> int:
    normalized_username = username.strip()
    if not normalized_username:
        raise ValueError("Username cannot be empty.")

    try:
        with db_manager.connect() as connection:
            cursor = connection.execute(
                """
                INSERT INTO USERS (username, passwd, employee_id)
                VALUES (?, ?, ?)
                """,
                (normalized_username, hash_password(password), employee_id),
            )
            return int(cursor.lastrowid)
    except Exception as exc:
        if "UNIQUE constraint failed: USERS.username" in str(exc):
            raise ValueError(f"Username already exists: {normalized_username}") from exc
        raise


def list_accounts(include_inactive: bool = True) -> list[dict[str, Any]]:
    query = "SELECT user_id, username, employee_id, status, created_at, updated_at FROM USERS"
    parameters: tuple[Any, ...] = ()

    if not include_inactive:
        query += " WHERE status = ?"
        parameters = ("active",)

    query += " ORDER BY username"

    with db_manager.connect() as connection:
        rows = connection.execute(query, parameters).fetchall()
    return [dict(row) for row in rows]


def delete_account(username: str) -> bool:
    with db_manager.connect() as connection:
        cursor = connection.execute(
            "DELETE FROM USERS WHERE username = ?",
            (username.strip(),),
        )
    return cursor.rowcount > 0


def set_account_status(username: str, status: str) -> bool:
    normalized_status = status.strip().lower()
    if normalized_status not in {"active", "disabled"}:
        raise ValueError("Account status must be active or disabled.")

    with db_manager.connect() as connection:
        cursor = connection.execute(
            """
            UPDATE USERS
            SET status = ?, updated_at = CURRENT_TIMESTAMP
            WHERE username = ?
            """,
            (normalized_status, username.strip()),
        )
    return cursor.rowcount > 0


def update_password(username: str, password: str) -> bool:
    with db_manager.connect() as connection:
        cursor = connection.execute(
            """
            UPDATE USERS
            SET passwd = ?, updated_at = CURRENT_TIMESTAMP
            WHERE username = ?
            """,
            (hash_password(password), username.strip()),
        )
    return cursor.rowcount > 0


def authenticate(username: str, password: str) -> dict[str, Any] | None:
    with db_manager.connect() as connection:
        row = connection.execute(
            """
            SELECT user_id, username, passwd, employee_id, status
            FROM USERS
            WHERE username = ?
            """,
            (username.strip(),),
        ).fetchone()

    if row is None or row["status"] != "active":
        return None

    if not verify_password(password, row["passwd"]):
        return None

    return {
        "user_id": row["user_id"],
        "username": row["username"],
        "employee_id": row["employee_id"],
        "status": row["status"],
    }
