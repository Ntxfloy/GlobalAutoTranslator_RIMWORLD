@echo off
chcp 65001 >nul
REM =====================================================================
REM  Запуск CLIProxyAPI. Положи этот файл рядом с cli-proxy-api.exe
REM  или задай переменную PROXYDIR под свой путь.
REM =====================================================================

if "%PROXYDIR%"=="" set PROXYDIR=D:\Ayder_dontdelete\CLIPROXY
if not exist "%PROXYDIR%\cli-proxy-api.exe" set PROXYDIR=%~dp0..\..\CLIPROXY

if not exist "%PROXYDIR%\cli-proxy-api.exe" (
  echo [ОШИБКА] cli-proxy-api.exe не найден.
  echo Задай переменную окружения PROXYDIR или положи прокси рядом с папкой мода.
  pause
  exit /b 1
)

cd /d "%PROXYDIR%"
echo Запуск прокси на http://127.0.0.1:8317 ...
cli-proxy-api.exe --config config.yaml
pause
