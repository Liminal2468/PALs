from __future__ import annotations

import argparse
import difflib
import platform
import shutil
from collections import defaultdict
from datetime import datetime
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[1]
LOG_DIR = Path(__file__).resolve().parent
REPORT_FILE = LOG_DIR / "localreport.log"
SHADOW_DIR = LOG_DIR / ".shadow"
USER_FILE = LOG_DIR / ".rep_user"

EXCLUDED_PARTS = {
    ".git",
    ".pytest_cache",
    "__pycache__",
    ".venv",
    "dist",
    "node_modules",
    "webenv",
}

EXCLUDED_FILES = {
    REPORT_FILE.resolve(),
    USER_FILE.resolve(),
}


def timestamp() -> str:
    return datetime.now().strftime("%p %I-%M-%S;%m-%d-%y")


def default_user() -> str:
    return platform.node() or "PCNAME"


def get_user() -> str:
    if USER_FILE.exists():
        value = USER_FILE.read_text(encoding="utf-8").strip()
        if value:
            return value

    return default_user()


def set_user(value: str | None) -> str:
    user = value.strip() if value else default_user()
    USER_FILE.write_text(f"{user}\n", encoding="utf-8")
    return user


def should_track(path: Path) -> bool:
    resolved = path.resolve()

    if resolved in EXCLUDED_FILES:
        return False

    if SHADOW_DIR.resolve() in resolved.parents or resolved == SHADOW_DIR.resolve():
        return False

    relative = path.relative_to(ROOT_DIR)
    return not any(part in EXCLUDED_PARTS for part in relative.parts)


def iter_files(root: Path) -> list[Path]:
    files = []

    for path in root.rglob("*"):
        if path.is_file() and should_track(path):
            files.append(path)

    return sorted(files)


def read_lines(path: Path) -> list[str]:
    try:
        return path.read_text(encoding="utf-8").splitlines()
    except UnicodeDecodeError:
        return []


def count_changed_lines(current: Path, shadow: Path) -> int:
    current_lines = read_lines(current) if current.exists() else []
    shadow_lines = read_lines(shadow) if shadow.exists() else []

    if not current_lines and not shadow_lines:
        return 0

    changed = 0
    matcher = difflib.SequenceMatcher(None, shadow_lines, current_lines)

    for tag, old_start, old_end, new_start, new_end in matcher.get_opcodes():
        if tag == "equal":
            continue

        changed += max(old_end - old_start, new_end - new_start)

    return changed


def folder_name(path: Path) -> str:
    relative = path.relative_to(ROOT_DIR)
    return relative.parts[0] if len(relative.parts) > 1 else "."


def collect_changes() -> dict[str, list[tuple[Path, int]]]:
    current_files = {path.relative_to(ROOT_DIR): path for path in iter_files(ROOT_DIR)}
    shadow_files = {}

    if SHADOW_DIR.exists():
        for path in SHADOW_DIR.rglob("*"):
            if path.is_file():
                shadow_files[path.relative_to(SHADOW_DIR)] = path

    changes = defaultdict(list)

    for relative in sorted(current_files.keys() | shadow_files.keys()):
        current = current_files.get(relative)
        shadow = shadow_files.get(relative)

        if current and shadow and current.read_bytes() == shadow.read_bytes():
            continue

        line_count = count_changed_lines(
            current if current else ROOT_DIR / relative,
            shadow if shadow else SHADOW_DIR / relative,
        )
        changes[folder_name(ROOT_DIR / relative)].append((relative, line_count))

    return dict(changes)


def refresh_shadow() -> None:
    if SHADOW_DIR.exists():
        shutil.rmtree(SHADOW_DIR)

    for file_path in iter_files(ROOT_DIR):
        target = SHADOW_DIR / file_path.relative_to(ROOT_DIR)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(file_path, target)


def append_entry(entry: str) -> None:
    REPORT_FILE.parent.mkdir(parents=True, exist_ok=True)
    with REPORT_FILE.open("a", encoding="utf-8") as report:
        report.write(entry.rstrip())
        report.write("\n")


def log_report(kind: str, content: str) -> None:
    safe_content = content.replace('"', '\\"')
    append_entry(f'[{timestamp()}] :: ({kind}): "{safe_content}" - {get_user()}')
    refresh_shadow()


def log_changes() -> None:
    changes = collect_changes()
    file_count = sum(len(files) for files in changes.values())
    lines = [f"[{timestamp()}] :: (changes): {file_count} files"]

    for folder in sorted(changes):
        files = changes[folder]
        folder_change_count = sum(line_count for _, line_count in files)
        lines.append(f"  - {folder}({folder_change_count})")

        for relative, line_count in files:
            lines.append(f"\t- {relative.name} ({line_count})")

    append_entry("\n".join(lines))
    refresh_shadow()


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Local version tracker for the mentos workspace.",
        epilog=(
            "... if you're facing trouble feel free to email me in "
            "reighlyt05@gmail.com, i'll help with your problem! ..."
        ),
    )
    parser.add_argument("--issue", metavar="content", help="reports an issue.")
    parser.add_argument("--fixed", metavar="content", help="reports a fix.")
    parser.add_argument(
        "--set-user",
        nargs="?",
        const=default_user(),
        metavar="user",
        help="sets user (default: {PCNAME}).",
    )
    parser.add_argument("--changes", action="store_true", help="logs changes.")
    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()

    did_work = False

    if args.set_user is not None:
        user = set_user(args.set_user)
        print(f"User set to {user}")
        did_work = True

    if args.issue:
        log_report("issue", args.issue)
        did_work = True

    if args.fixed:
        log_report("fixed", args.fixed)
        did_work = True

    if args.changes:
        log_changes()
        did_work = True

    if not did_work:
        parser.print_help()


if __name__ == "__main__":
    main()
