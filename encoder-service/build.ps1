# Build script for a2dp-encoder
# Run from PowerShell

$ErrorActionPreference = "Stop"

Write-Host "Building a2dp-encoder..." -ForegroundColor Cyan

# Ensure we're in the right directory
Set-Location $PSScriptRoot

# Build release
cargo build --release

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful!" -ForegroundColor Green
    Write-Host "Binary: target\release\a2dp-encoder.exe" -ForegroundColor Yellow
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
