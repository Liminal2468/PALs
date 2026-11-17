#!/usr/bin/env bash
set -Eeuo pipefail

if [[ "${EUID}" -ne 0 ]]; then
    printf 'Run this installer with sudo.\n' >&2
    exit 1
fi

server_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
published_launcher="$server_root/run-published"
unit_path="/etc/systemd/system/palsys.service"
launcher_path="/usr/local/libexec/palsys-server"
state_root="/var/lib/palsys"

if [[ ! -x "$published_launcher" ]]; then
    printf 'Missing executable launcher: %s\n' "$published_launcher" >&2
    exit 1
fi

if [[ ! -x "$server_root/publish/Palsys.Server" ]]; then
    printf 'Missing published server: %s\nRun dotnet publish first.\n' "$server_root/publish/Palsys.Server" >&2
    exit 1
fi

if ! id palsys >/dev/null 2>&1; then
    printf 'The system user "palsys" does not exist. Create it before installing the service.\n' >&2
    exit 1
fi

install -d -o root -g root -m 0755 /usr/local/libexec
ln -sfn "$published_launcher" "$launcher_path"
install -d -o palsys -g palsys -m 0750 "$state_root"
install -d -o palsys -g palsys -m 0750 "$state_root/dataprotection"
install -o root -g root -m 0644 "$server_root/palsys.service" "$unit_path"
systemctl daemon-reload
systemctl enable palsys.service

printf 'Installed palsys.service for server directory: %s\n' "$server_root"
printf 'Published launcher link: %s -> %s\n' "$launcher_path" "$published_launcher"
printf 'Persistent state directory: %s\n' "$state_root"
printf 'Start it with: sudo systemctl restart palsys.service\n'
