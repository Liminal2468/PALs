$ErrorActionPreference = "Stop"
$serverRoot = Split-Path -Parent $PSScriptRoot
Set-Location $serverRoot
node src/runner.js
exit $LASTEXITCODE
