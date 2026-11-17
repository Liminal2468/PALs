from __future__ import annotations

import argparse
import getpass
import sys
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[2]
if str(ROOT_DIR) not in sys.path:
    sys.path.insert(0, str(ROOT_DIR))

from back.auth.auth import (
    create_account,
    delete_account,
    list_accounts,
    set_account_status,
    update_password,
)


def read_password(value: str | None, confirm: bool = False) -> str:
    if value is not None:
        return value

    password = getpass.getpass("Password: ")
    if confirm:
        repeated = getpass.getpass("Confirm password: ")
        if password != repeated:
            raise ValueError("Passwords do not match.")
    return password


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="data.py",
        description="Manage mentos user accounts in the local database.",
    )
    commands = parser.add_subparsers(dest="command", required=True)

    add_parser = commands.add_parser("add", help="Create an account.")
    add_parser.add_argument("-u", "--username", required=True)
    add_parser.add_argument("-p", "--password")
    add_parser.add_argument("--employee-id", type=int)

    list_parser = commands.add_parser("list", help="List accounts.")
    list_parser.add_argument(
        "--active-only",
        action="store_true",
        help="Only show active accounts.",
    )

    delete_parser = commands.add_parser("delete", help="Delete an account.")
    delete_parser.add_argument("-u", "--username", required=True)
    delete_parser.add_argument(
        "-f", "--force", action="store_true", help="Skip confirmation."
    )

    password_parser = commands.add_parser(
        "password", help="Change an account password."
    )
    password_parser.add_argument("-u", "--username", required=True)
    password_parser.add_argument("-p", "--password")

    status_parser = commands.add_parser("status", help="Enable or disable an account.")
    status_parser.add_argument("-u", "--username", required=True)
    status_parser.add_argument("--value", choices=("active", "disabled"), required=True)

    return parser


def print_accounts(accounts: list[dict[str, object]]) -> None:
    if not accounts:
        print("No accounts found.")
        return

    for account in accounts:
        print(
            f"{account['user_id']}\t{account['username']}\t"
            f"{account['status']}\temployee_id={account['employee_id']}"
        )


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()

    try:
        if args.command == "add":
            user_id = create_account(
                args.username,
                read_password(args.password, confirm=True),
                args.employee_id,
            )
            print(f"Created account {args.username} (id={user_id}).")
            return

        if args.command == "list":
            print_accounts(list_accounts(include_inactive=not args.active_only))
            return

        if args.command == "delete":
            if not args.force:
                answer = input(f'Delete account "{args.username}"? [y/N]: ')
                if answer.strip().lower() not in {"y", "yes"}:
                    print("Nothing deleted.")
                    return

            if delete_account(args.username):
                print(f"Deleted account {args.username}.")
            else:
                print(f"Account not found: {args.username}")
            return

        if args.command == "password":
            changed = update_password(
                args.username,
                read_password(args.password, confirm=True),
            )
            print("Password updated." if changed else f"Account not found: {args.username}")
            return

        if args.command == "status":
            changed = set_account_status(args.username, args.value)
            print(
                f"Account status set to {args.value}."
                if changed
                else f"Account not found: {args.username}"
            )
    except (ValueError, OSError) as exc:
        parser.error(str(exc))


if __name__ == "__main__":
    main()
