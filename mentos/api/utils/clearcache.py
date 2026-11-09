from __future__ import annotations

import shutil
from pathlib import Path


def clear_python_caches(root: Path | None = None) -> list[Path]:
    base_dir = (root or Path.cwd()).resolve()
    removed: list[Path] = []

    for path in base_dir.rglob("__pycache__"):
        if path.is_dir():
            shutil.rmtree(path)
            removed.append(path)

    for path in base_dir.rglob("*.pyc"):
        if path.is_file():
            path.unlink()
            removed.append(path)

    return removed


def main() -> None:
    removed = clear_python_caches()
    print(f"Removed {len(removed)} Python cache entries from {Path.cwd().resolve()}")


if __name__ == "__main__":
    main()
