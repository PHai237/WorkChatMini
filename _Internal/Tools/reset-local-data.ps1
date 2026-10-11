$ErrorActionPreference = "Stop"
$path = Join-Path $env:LOCALAPPDATA "WorkChatMini"

if (Test-Path $path) {
    Write-Host "Deleting $path ..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $path
    Write-Host "Local session/settings deleted." -ForegroundColor Green
} else {
    Write-Host "No local WorkChat Mini data found."
}
