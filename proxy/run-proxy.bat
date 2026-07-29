@echo off
chcp 65001 >nul
REM =====================================================================
REM  Запуск CLIProxyAPI. Прокси ищется в таком порядке:
REM    1) переменная окружения PROXYDIR
REM    2) папка этого батника
REM    3) подпапка CLIPROXY рядом с батником, на уровень выше, на два выше
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
  echo [ОШИБКА] cli-proxy-api.exe не найден.
  echo Искали в: "%PROXYDIR%"
  echo Задай переменную окружения PROXYDIR с путём к папке CLIProxyAPI,
  echo либо положи этот файл рядом с cli-proxy-api.exe.
  echo Если качал архив — распакуй его полностью, запуск прямо из ZIP не работает.
  pause
  exit /b 1
)

cd /d "%PROXYDIR%"
echo Запуск прокси на http://127.0.0.1:8317 ...
cli-proxy-api.exe --config config.yaml
pause
