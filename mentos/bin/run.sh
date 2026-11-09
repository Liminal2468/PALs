#!/bin/sh
set -e
cd "$(dirname "$0")/.."
exec node bin/runner.js
