# PALs


<div align="center">
  <img src="https://hermes.softshell-grue.ts.net/v1/media/4f106b08d1cb4c5bb571c3f173879b48.png" width="100%" />
</div>
<p align="center"><sub>A Peer-Assisted Learning system web application for STI College Cubao's SHS student population.</sub></p>

This is private contributed from STI College Cubao. Also to prevent Overflow in the main branch. <sub>if you require help with merging through git please refer to [Merge Branch](bindoc/merge-branch.md)</sub>

As of this moment some part has yet to be updated, such as [server](server/) as some are still a template.

> [!NOTE]
> THIS PROJECT REQUIRES INTERNET ACCESS TO REACT THE DATABASE SERVER <sup>[1](bindoc/maindoc/notes/apmain-whatis.md)</sup>

> [!IMPORTANT]
> The previous pals files are in [bindoc](bindoc/corefile)

## Installation

### Requirements

- Git
- Node.js 20 or newer and npm
- Python 3.12 or newer and [uv](https://docs.astral.sh/uv/)
- Access to the private PALs repository and, for database-backed features, the configured DBSERV tailnet/API

### Clone and run Mentos

Get the repository clone URL from the project owner. Because this repository is private, make sure your Git account has access before cloning.

```bash
git clone "PASTE_THE_REPOSITORY_CLONE_URL"
cd REPOSITORY_FOLDER/mentos
npm install
uv sync --project api
```

Replace the clone URL and `REPOSITORY_FOLDER` with the URL and folder name for your checkout.

Configure the HOST API using `mentos/api/.env.example` as a reference. Keep real `.env` values and signing keys private; do not commit them.

Start the Vite frontend and Flask HOST API together:

```bash
./run
```

Vite is normally available at `http://localhost:5173/`; the API listens on `127.0.0.1:8000`. Press Ctrl-C to stop both services. On Windows, use WSL or follow the platform-specific instructions in [Running Mentos](bindoc/maindoc/notes/mentos-whatis.md).

> [!NOTE]
> For Tailscale setup and access requirements, see [What is APMAIN?](bindoc/maindoc/notes/apmain-whatis.md). For creating a feature branch, merging work, and the basic CI/CD process, see [Create a branch](bindoc/create-branch.md), [Merge a branch](bindoc/merge-branch.md), and [Basic CI/CD flow](bindoc/basic-cicdflow.md).


---
**Current Contributors**

[![Liminal2468](https://img.shields.io/badge/GitHub-Liminal2468-181717?style=flat-square&logo=github)](https://github.com/Liminal2468)
[![KeiNerokami](https://img.shields.io/badge/GitHub-KeiNerokami-181717?style=flat-square&logo=github)](https://github.com/KeiNerokami)
