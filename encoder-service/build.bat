@echo off
setlocal

echo ========================================
echo  A2DP Encoder Service - Build Script
echo ========================================
echo.

cd /d "%~dp0"

echo Checking Rust installation...
where cargo >nul 2>&1
if errorlevel 1 (
    echo ERROR: Cargo not found in PATH
    echo Please install Rust from https://rustup.rs/
    echo After installation, restart your terminal.
    pause
    exit /b 1
)

cargo --version
rustc --version
echo.

echo Building release version...
cargo build --release

if errorlevel 1 (
    echo.
    echo ========================================
    echo  BUILD FAILED
    echo ========================================
    pause
    exit /b 1
)

echo.
echo ========================================
echo  BUILD SUCCESSFUL
echo ========================================
echo.
echo Binary location:
echo   %~dp0target\release\a2dp-encoder.exe
echo.

if exist "target\release\a2dp-encoder.exe" (
    echo File size:
    for %%A in ("target\release\a2dp-encoder.exe") do echo   %%~zA bytes
)

echo.
pause
