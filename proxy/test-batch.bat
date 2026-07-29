@echo off
chcp 65001 >nul
REM Проверка прокси и качества перевода. Запускать из папки с test.json.

echo --- Список моделей ---
curl -s http://127.0.0.1:8317/v1/models
echo.
echo.
echo --- Тестовый батч перевода ---
curl -s -X POST http://127.0.0.1:8317/v1/chat/completions -H "Content-Type: application/json" -d @test.json
echo.
echo.
echo Смотри usage: если reasoning_tokens больше 100 - thinking не задавлен.
pause
