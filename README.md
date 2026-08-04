# Global Auto Translator (RimWorld 1.6 + локальный LLM / CLIProxyAPI)

Глобальный автопереводчик: переводит названия/описания любых модов, текст интерфейса, а также **динамические задания (Quests), письма и всплывающие ивенты (Letters)** на русский язык через OpenAI-совместимый эндпоинт (CLIProxyAPI, Ollama, LM Studio), кэширует всё локально и умеет экспортировать готовые XML локализации.

---

## Архитектура: слои перевода

| Слой | Перехват | Что ловит | Режим / Цена |
|---|---|---|---|
| **L1** | `DefPostProcessor` | `label`, `description` и ещё ~16 строковых полей всех Defs | При старте игры / 0 на кадрах |
| **L2** | `LoadedLanguage.TryGetTextFromKey` | Все Keyed-строки интерфейса | Один lookup по словарям |
| **L3** | `Widgets.Label` (выключен по умолч.) | Хардкод в C# чужих модов | Только чтение из памяти |
| **Dynamic** | `DynamicTranslator` (`QuestManager.Add`, `LetterStack.ReceiveLetter`, `MainTabWindow_Quests.PreOpen`) | Задания, описания квестов, письма и ивенты в реальном времени | Перевод на лету, не забивает кэш уникальными описаниями |

Критичное правило: **L3 никогда не шлёт сетевых запросов и не ставит в очередь.** `Widgets.Label` вызывается тысячи раз в секунду — любая регулярка там даст микрофризы.

Сеть живёт только в фоновых потоках (`TranslateWorker`). Главный поток никогда не ждёт ответа: если перевода ещё нет, игра показывает оригинал, при получении ответа текст обновляется автоматически.

---

## Контексты перевода (Contexts)

- `label` — названия предметов, зданий, существ (со строчной буквы, игра сама капитализирует).
- `title` — заголовки квестов, писем и ивентов (сохраняет заглавную букву в начале, как в оригинале).
- `description` — описания предметов, фракций, квестов и тексты писем (сохраняет регистр и форматирование оригинала).
- `keyed` — строки интерфейса RimWorld.

Динамические описания квестов и писем помечаются флагом `Volatile = true` — они перерабатываются моделью на лету, но не сохраняются в постоянный файл-кэш на диске во избежание разрастания файла кэша.

---

## Сборка

1. Установи **Visual Studio 2022 Community** → рабочая нагрузка «Разработка классических приложений .NET».
2. Открой `Source/GlobalAutoTranslator.csproj`.
3. Путь к игре определяется автоматически (типовые папки Steam на C:/D:/E:).
4. `Build` → результат ляжет в `Assemblies/GlobalAutoTranslator.dll`.

Или используй скрипт `build.ps1` — он линкует `0Harmony.dll` напрямую и деплоит мод в папку игры:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File build.ps1 -RimWorldRoot "C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
powershell -ExecutionPolicy Bypass -File build.ps1 -NoInstall
```

---

## Настройка прокси (CLIProxyAPI)

Дефолтная модель мода: `gemini-3.6-flash-high`.

Файл `proxy/config-payload-snippet.yaml` содержит необходимую конфигурацию для `config.yaml` CLIProxyAPI:
- Подавляет размышления (thinking tokens) для протокола `antigravity`, сокращая задержку 40-строчных батчей до ~3 секунд.

Запуск прокси: `start_cli_proxy.bat` или `proxy/run-proxy.bat`.
Проверка: `proxy/test-batch.bat` → в `usage` должно быть `prompt_tokens + completion_tokens == total_tokens` и никакой задержки на размышления.

---

## Кэш перевода

Кэш лежит здесь:
```
%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\GlobalTranslator\cache\
```
Это обычные TSV-файлы — можно открыть блокнотом и править руками.

---

## Защита от битых переводов

`PlaceholderGuard.Validate` отбрасывает строку, если:
- Набор `{...}` / `<...>` / `[...]` / `\n` не совпал с оригиналом до символа;
- Перевод длиннее оригинала больше чем в 3.5 раза;
- Есть markdown-блок ``` или префикс «Перевод:»;
- Не сходится количество `<color` и `</color>`.

---

## Где что лежит

| Файл | Отвечает за |
|---|---|
| `GATBoot.cs` | старт, Harmony, инициализация кэша |
| `GATMod.cs` | настройки и весь UI мода |
| `DefPostProcessor.cs` | слой 1 (Defs) |
| `Patches.cs` | слои 2, 3 и `DynamicTranslator` (Quests, Letters) |
| `TranslateWorker.cs` | очередь, батчи, дедупликация, `Volatile` режим, карантин |
| `LlmClient.cs` | HTTP, retry, разбор ответа |
| `Prompt.cs` | системный промпт, контекст `title`, глоссарий |
| `PlaceholderGuard.cs` | валидация плейсхолдеров |
| `TranslationCache.cs` | кэш на диске |
| `LanguageExporter.cs` | экспорт XML |
| `MiniJson.cs` | JSON без зависимостей |
