$ErrorActionPreference = "Stop"
$mentosRoot = Split-Path -Parent $PSScriptRoot
Set-Location $mentosRoot
node bin/runner.js
exit $LASTEXITCODE
