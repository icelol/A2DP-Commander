@echo off
setlocal enabledelayedexpansion

:: Setup logging
cd /d "%~dp0"
if not exist "temp" mkdir temp
set LOGFILE=%~dp0temp\build.log
echo A2DP Commander Build Log > "%LOGFILE%"
echo Started: %date% %time% >> "%LOGFILE%"
echo. >> "%LOGFILE%"

call :log "========================================"
call :log " A2DP Commander - Full Build Script"
call :log "========================================"
call :log ""

cd /d "%~dp0"
call :log "Working directory: %CD%"
call :log ""

set ERRORS=0

:: ========================================
:: 1. Build Rust Encoder Service
:: ========================================
call :log "[1/2] Building Rust Encoder Service..."
call :log ""

if exist "encoder-service\Cargo.toml" (
    where cargo >nul 2>&1
    if errorlevel 1 (
        call :log "WARNING: Cargo not found, skipping encoder-service build"
        call :log "Install Rust from https://rustup.rs/ to build encoder"
        set ENCODER_BUILT=0
    ) else (
        for /f "tokens=*" %%v in ('cargo --version 2^>^&1') do call :log "Cargo version: %%v"
        for /f "tokens=*" %%v in ('rustc --version 2^>^&1') do call :log "Rustc version: %%v"
        call :log ""
        pushd encoder-service
        call :log "Running: cargo build --release"
        cargo build --release >> "%LOGFILE%" 2>&1
        if errorlevel 1 (
            call :log "ERROR: Encoder service build failed"
            set ERRORS=1
            set ENCODER_BUILT=0
        ) else (
            call :log "Encoder service built successfully"
            set ENCODER_BUILT=1
        )
        popd
    )
) else (
    call :log "WARNING: encoder-service not found, skipping"
    set ENCODER_BUILT=0
)
call :log ""

:: ========================================
:: 2. Build C# Application
:: ========================================
call :log "[2/2] Building A2DP Commander (.NET)..."
call :log ""

where dotnet >nul 2>&1
if errorlevel 1 (
    call :log "ERROR: dotnet not found in PATH"
    set ERRORS=1
    goto :summary
)

for /f "tokens=*" %%v in ('dotnet --version 2^>^&1') do call :log "Dotnet version: %%v"
call :log ""
call :log "Running: dotnet build -c Release"
dotnet build "src\A2DPCommander\A2DPCommander.csproj" -c Release --nologo >> "%LOGFILE%" 2>&1
if errorlevel 1 (
    call :log "ERROR: A2DP Commander build failed"
    set ERRORS=1
) else (
    call :log "A2DP Commander built successfully"
)
call :log ""

:: ========================================
:: Summary
:: ========================================
:summary
call :log ""
call :log "========================================"
if %ERRORS%==0 (
    call :log " BUILD SUCCESSFUL"
) else (
    call :log " BUILD COMPLETED WITH ERRORS"
)
call :log "========================================"
call :log ""

call :log "Output files:"
call :log ""

if exist "src\A2DPCommander\bin\Release\net8.0-windows10.0.19041.0\A2DP-Commander.exe" (
    call :log "  [OK] A2DP Commander:"
    call :log "       src\A2DPCommander\bin\Release\net8.0-windows10.0.19041.0\A2DP-Commander.exe"
) else (
    call :log "  [--] A2DP Commander: not built"
)

if %ENCODER_BUILT%==1 (
    if exist "encoder-service\target\release\a2dp-encoder.exe" (
        call :log "  [OK] Encoder Service:"
        call :log "       encoder-service\target\release\a2dp-encoder.exe"
    )
) else (
    call :log "  [--] Encoder Service: skipped (Rust not installed)"
)

call :log ""
call :log "Finished: %date% %time%"
call :log "Log file: %LOGFILE%"

echo.
echo Log file saved to: %LOGFILE%
echo.
exit /b %ERRORS%

:: ========================================
:: Logging function - outputs to console and file
:: ========================================
:log
echo %~1
echo %~1 >> "%LOGFILE%"
goto :eof
