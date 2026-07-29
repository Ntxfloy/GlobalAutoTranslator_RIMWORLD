@echo off
chcp 65001 >nul
REM =====================================================================
REM  Запуск CLIProxyAPI. Положи этот файл рядом с cli-proxy-api.exe
REM  и правь PROXYDIR под свой путь, если запускаешь из другого места.
REM =====================================================================

set PROXYDIR=D:\Ayder_dontdelete\CLIPROXY

cd /d "%PROXYDIR%"
if not exist cli-proxy-api.exe (
  echo [ОШИБКА] cli-proxy-api.exe не найден в %PROXYDIR%
  echo Распакуй архив полностью, запуск прямо из ZIP не работает.
  pause
  exit /b 1
)

echo Запуск прокси на http://127.0.0.1:8317 ...
cli-proxy-api.exe --config config.yaml
pause
