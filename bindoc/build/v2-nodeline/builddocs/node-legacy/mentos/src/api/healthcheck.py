"""File: healthcheck.py
Description: Check DBSERV liveness and authenticated database readiness.
Version: 1.0.0
"""

from __future__ import annotations

import logging

import requests

from api.db.db import DB, db


def check_dbserv(database: DB) -> None:
    database.check_liveness()
    database.check_database_health()


def main() -> int:
    try:
        check_dbserv(db)
    except (requests.RequestException, RuntimeError):
        logging.getLogger(__name__).exception("DBSERV health check failed.")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
