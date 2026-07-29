@echo off
chcp 65001 >nul
REM =====================================================================
REM  Start CLIProxyAPI. Search order:
REM    1) PROXYDIR env var
REM    2) Current script dir
REM    3) CLIPROXY subfolders
REM =====================================================================

if defined PROXYDIR goto :check

for %%D in (
  "%~dp0."
  "%~dp0CLIPROXY"
  "%~dp0..\CLIPROXY"
  "%~dp0..\..\CLIPROXY"
  "D:\Ayder_dontdelete\CLIPROXY"
) do if not defined PROXYDIR if exist "%%~fD\cli-proxy-api.exe" set "PROXYDIR=%%~fD"

if not defined PROXYDIR set "PROXYDIR=%~dp0."

:check
if not exist "%PROXYDIR%\cli-proxy-api.exe" (
  echo [ERROR] cli-proxy-api.exe not found.
  echo Searched path: "%PROXYDIR%"
  echo Please set PROXYDIR environment variable or place cli-proxy-api.exe near this script.
  pause
  exit /b 1
)

cd /d "%PROXYDIR%"
echo Starting CLIProxyAPI on http://127.0.0.1:8317 ...
cli-proxy-api.exe --config config.yaml
pause
