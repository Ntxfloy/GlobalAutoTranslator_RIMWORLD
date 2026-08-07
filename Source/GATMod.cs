using System.Threading;
using RimWorld;
using UnityEngine;
using Verse;

namespace GlobalAutoTranslator
{
	public class GATSettings : ModSettings
	{
		// Подключение
		public string endpoint = "http://127.0.0.1:8317/v1/chat/completions";
		public string model = "gemini-3.6-flash-high";
		public string apiKey = "";
		public int timeoutSeconds = 120;

		// Поведение
		public int batchSize = 25;
		public int maxConcurrent = 2;
		public bool sendReasoningEffortNone = true;
		public bool requestJsonObject = true;

		// Слои
		public bool translateDefs = true;      // L1
		public bool translateKeyed = true;     // L2
		public bool translateWidgets = true;   // L3 — перехват интерфейса на лету
		public bool translateDescriptions = true;
		public bool autoFitLabels = true;      // Автоподгонка шрифта длинного текста

		public bool verboseLogging = true;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref endpoint, "endpoint", "http://127.0.0.1:8317/v1/chat/completions");
			Scribe_Values.Look(ref model, "model", "gemini-3.6-flash-high");
			Scribe_Values.Look(ref apiKey, "apiKey", "");
			Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 120);
			Scribe_Values.Look(ref batchSize, "batchSize", 40);
			Scribe_Values.Look(ref maxConcurrent, "maxConcurrent", 2);
			Scribe_Values.Look(ref sendReasoningEffortNone, "sendReasoningEffortNone", true);
			Scribe_Values.Look(ref requestJsonObject, "requestJsonObject", true);
			Scribe_Values.Look(ref translateDefs, "translateDefs", true);
			Scribe_Values.Look(ref translateKeyed, "translateKeyed", true);
			Scribe_Values.Look(ref translateWidgets, "translateWidgets", true);
			Scribe_Values.Look(ref translateDescriptions, "translateDescriptions", true);
			Scribe_Values.Look(ref autoFitLabels, "autoFitLabels", true);
			Scribe_Values.Look(ref verboseLogging, "verboseLogging", true);
		}
	}

	public class GATMod : Mod
	{
		public const string ModVersion = "31.0";
		public static GATSettings Settings;
		private static string selfTestResult = "";

		public GATMod(ModContentPack content) : base(content)
		{
			Settings = GetSettings<GATSettings>();
			
			// Выводим версию и информацию в лог при старте
			System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
			string asmVersion = asm.GetName().Version.ToString();
			System.IO.FileInfo fi = new System.IO.FileInfo(asm.Location);
			GATLog.Msg("Started: v" + ModVersion + " (Asm: " + asmVersion + ", Date: " + fi.LastWriteTime.ToString("o") + ")");
			GATLog.Msg("Prompt v" + Prompt.PromptVersion + ", Model: " + Settings.model);
		}

		public override string SettingsCategory()
		{
			return "Global Auto Translator";
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			var list = new Listing_Standard();
			list.Begin(inRect);

			list.Label("Эндпоинт (OpenAI-совместимый):");
			Settings.endpoint = list.TextEntry(Settings.endpoint);

			list.Label("Модель:");
			Settings.model = list.TextEntry(Settings.model);

			list.Label("API-ключ (можно пусто для локального прокси):");
			Settings.apiKey = list.TextEntry(Settings.apiKey);

			list.Gap(8f);
			Settings.batchSize = (int)list.SliderLabeled(
				"Строк в одном запросе: " + Settings.batchSize, Settings.batchSize, 5f, 120f);
			Settings.maxConcurrent = (int)list.SliderLabeled(
				"Параллельных запросов: " + Settings.maxConcurrent +
				(TranslateWorker.ActiveThreads > 0 && TranslateWorker.ActiveThreads != Mathf.Clamp(Settings.maxConcurrent, 1, 4)
					? "  (сейчас работает " + TranslateWorker.ActiveThreads + ", применится при закрытии окна)"
					: ""),
				Settings.maxConcurrent, 1f, 4f);
			Settings.timeoutSeconds = (int)list.SliderLabeled(
				"Таймаут, сек: " + Settings.timeoutSeconds, Settings.timeoutSeconds, 15f, 300f);

			list.Gap(8f);
			list.CheckboxLabeled("Переводить Defs (названия предметов, зданий, существ)", ref Settings.translateDefs);
			list.CheckboxLabeled("Переводить описания (дорого по токенам)", ref Settings.translateDescriptions);
			list.CheckboxLabeled("Переводить Keyed-строки (текст интерфейса)", ref Settings.translateKeyed);
			list.CheckboxLabeled("Слой 3: перехват интерфейса на лету (UI / кнопки / диалоги; может дать просадку кадров)", ref Settings.translateWidgets);
			list.CheckboxLabeled("Уменьшать шрифт, если перевод не влезает", ref Settings.autoFitLabels);
			list.CheckboxLabeled("Подробный лог", ref Settings.verboseLogging);
			list.CheckboxLabeled("Отправлять reasoning_effort=none", ref Settings.sendReasoningEffortNone);
			list.CheckboxLabeled("Требовать response_format=json_object", ref Settings.requestJsonObject);

			list.Gap(10f);
			list.Label("Статус: в кэше " + TranslationCache.Count +
			           " | в очереди " + TranslateWorker.Pending +
			           " | переведено за сессию " + TranslateWorker.TranslatedThisSession +
			           " | отброшено " + TranslateWorker.Failed +
			           (TranslateWorker.Paused ? "  [ПАУЗА: прокси недоступен]" : ""));

			list.Gap(6f);
			if (list.ButtonText("Проверить соединение с ИИ сейчас"))
			{
				selfTestResult = "Проверка соединения...";
				var t = new Thread(() =>
				{
					try
					{
						selfTestResult = TranslateWorker.RunProbe(ignoreCooldown: true);
					}
					catch (System.Exception e)
					{
						selfTestResult = "Ошибка проверки: " + e.Message;
					}
				});
				t.IsBackground = true;
				t.Name = "GAT-Manual-Probe";
				t.Start();
			}
			if (list.ButtonText("Проверить качество перевода (расширенный тест)"))
			{
				selfTestResult = "Запрос отправлен, жди...";
				var t = new Thread(() => { selfTestResult = LlmClient.SelfTest(Settings); });
				t.IsBackground = true;
				t.Start();
			}
			if (list.ButtonText("Запустить самотест"))
			{
				SelfTest.Run();
				GATLog.Msg("Самотест выполнен вручную из настроек мода.");
				Messages.Message("Самотест выполнен. Результаты в логе (Ctrl+F12)", MessageTypeDefOf.TaskCompletion, false);
			}
			if (list.ButtonText("Перевести все Defs сейчас (поставить в очередь)"))
			{
				int n = DefPostProcessor.EnqueueAll();
				Messages.Message("В очередь добавлено строк: " + n, MessageTypeDefOf.TaskCompletion, false);
			}
			if (list.ButtonText("Экспортировать кэш в мод с XML-переводом"))
			{
				string path = LanguageExporter.ExportAll();
				Messages.Message("Готово: " + path, MessageTypeDefOf.TaskCompletion, false);
			}
			if (list.ButtonText("Сбросить карантин (повторить ошибочные строки)"))
			{
				TranslateWorker.ClearQuarantine();
				Messages.Message("Карантин очищен", MessageTypeDefOf.TaskCompletion, false);
			}
			if (list.ButtonText("Очистить список окончательных отбраковок (" + TranslationCache.PermanentFailedCount + ")"))
			{
				TranslationCache.ClearPermanentFailed();
				Messages.Message("Список окончательных отбраковок очищен", MessageTypeDefOf.TaskCompletion, false);
			}
			if (list.ButtonText("Сохранить кэш на диск"))
			{
				TranslationCache.Flush();
				Messages.Message("Кэш сохранён: " + TranslationCache.CacheDir, MessageTypeDefOf.TaskCompletion, false);
			}

			if (!selfTestResult.NullOrEmpty())
			{
				list.Gap(8f);
				list.Label(selfTestResult);
			}

			list.End();
			base.DoSettingsWindowContents(inRect);
		}

		public override void WriteSettings()
		{
			base.WriteSettings();
			TranslationCache.Flush();

			// Число потоков читается только в Start(), поэтому пул надо пересобрать.
			int want = Mathf.Clamp(Settings.maxConcurrent, 1, 4);
			if (TranslateWorker.ActiveThreads > 0 && TranslateWorker.ActiveThreads != want)
			{
				Messages.Message("Пул потоков перезапускается: " + want, MessageTypeDefOf.TaskCompletion, false);
				var t = new System.Threading.Thread(TranslateWorker.Restart);
				t.IsBackground = true;
				t.Name = "GAT-Restart";
				t.Start();
			}
		}
	}
}
