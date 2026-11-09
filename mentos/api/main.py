from pathlib import Path

from flask import Flask, jsonify


BASE_DIR = Path(__file__).resolve().parent
PROJECT_DIR = BASE_DIR.parent

app = Flask(__name__)


@app.get("/api/pages")
def pages():
    pages_root = PROJECT_DIR / "public" / "pages"
    page_names = []

    if pages_root.exists():
        page_names = sorted(
            page_dir.name
            for page_dir in pages_root.iterdir()
            if page_dir.is_dir() and (page_dir / "index.html").is_file()
        )

    if "Home" in page_names:
        page_names.remove("Home")
        page_names.insert(0, "Home")

    return jsonify({"pages": page_names})


if __name__ == "__main__":
    app.run(host="127.0.0.1", port=8000, debug=True)
