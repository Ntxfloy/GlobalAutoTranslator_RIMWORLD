using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;

namespace GlobalAutoTranslator
{
	/// <summary>
	/// Безопасный сборщик UI-строк отрисовки (Слой 3 V2).
	/// Выполняет быструю фильтрацию без аллокаций во время OnGUI,
	/// а тяжелый разбор и постановку в очередь делает в Drain() главного потока.
	/// </summary>
	public static class UiHarvest
	{
		private static readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		private static readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();
		private static int newThisFrame;
		private static int lastFrameCount = -1;

		private static int enqueuedThisSession;
		private static int filteredCount;
		private static bool limitReachedLogged;

		private const int MaxEnqueuedPerSession = 100000;
		private const int MaxSeenCapacity = 100000;

		public static void Note(string s)
		{
			if (string.IsNullOrEmpty(s)) return;
			int len = s.Length;
			if (len < 2 || len > 300) return;

			// 1. Быстрый отбой по кириллице СРАЗУ в Note (разрывает петлю самоподачи IMGUI)
			for (int i = 0; i < len; i++)
			{
				char c = s[i];
				if (c >= '\u0400' && c <= '\u052F') return;
			}

			int currentFrame = UnityEngine.Time.frameCount;
			if (currentFrame != lastFrameCount)
			{
				lastFrameCount = currentFrame;
				newThisFrame = 0;
			}

			if (newThisFrame >= 4) return;
			if (seen.Count >= MaxSeenCapacity) return;

			lock (seen)
			{
				if (seen.Contains(s)) return;
				seen.Add(s);
			}

			newThisFrame++;
			pending.Enqueue(s);

			// Подстраховка: если строка с двоеточием ("Label:"), ставим в очередь и очищенную версию ("Label")
			if (s.EndsWith(":"))
			{
				string trimmed = s.Substring(0, s.Length - 1).TrimEnd();
				if (trimmed.Length >= 2)
				{
					lock (seen)
					{
						if (!seen.Contains(trimmed))
						{
							seen.Add(trimmed);
							pending.Enqueue(trimmed);
						}
					}
				}
			}
		}

		public static void Drain(int maxPerCall = 30)
		{
			if (enqueuedThisSession >= MaxEnqueuedPerSession)
			{
				if (!limitReachedLogged)
				{
					limitReachedLogged = true;
					GATLog.Warn("UI Harvest: достигнут лимит " + MaxEnqueuedPerSession + " строк за сессию. Сбор приостановлен.");
				}
				return;
			}

			string s;
			int examined = 0;
			while (examined < maxPerCall && pending.TryDequeue(out s))
			{
				examined++;

				if (!PlaceholderGuard.ShouldTranslate(s))
				{
					filteredCount++;
					continue;
				}

				// Фильтр путей, файлов и расширений
				if (s.Contains("/") || s.Contains("\\") ||
					s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
					s.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
					s.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
				{
					filteredCount++;
					continue;
				}

				// Проверка цифр
				int digits = 0;
				bool hasLetter = false;
				for (int i = 0; i < s.Length; i++)
				{
					char c = s[i];
					if (char.IsDigit(c)) digits++;
					if (char.IsLetter(c)) hasLetter = true;
				}

				if (!hasLetter) { filteredCount++; continue; }
				if (digits > 0 && s.Length < 25 && !s.EndsWith("%")) { filteredCount++; continue; }
				if (digits * 4 > s.Length && !s.EndsWith("%")) { filteredCount++; continue; }

				// CamelCase & Hex фильтр только для однословных строк без пробелов
				if (!s.Contains(" "))
				{
					if (IsCamelCaseOrCodeIdentifier(s))
					{
						filteredCount++;
						continue;
					}
				}

				TranslateWorker.Enqueue("ui", s, isVolatile: false);
				enqueuedThisSession++;

				if (enqueuedThisSession % 300 == 0)
				{
					GATLog.Msg("UI Harvest статус: поставлено в очередь " + enqueuedThisSession +
					           ", в seen " + seen.Count + ", отфильтровано мусора " + filteredCount);
				}
			}
		}

		private static bool IsCamelCaseOrCodeIdentifier(string s)
		{
			if (string.IsNullOrEmpty(s)) return false;
			if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return true;
			if (s.Contains("_") || s.Contains(".")) return true;

			if (s.Length >= 8)
			{
				bool allHex = true;
				for (int i = 0; i < s.Length; i++)
				{
					char c = s[i];
					if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') || c == '-'))
					{ allHex = false; break; }
				}
				if (allHex) return true;
			}

			bool hasLower = false;
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (char.IsLower(c)) hasLower = true;
				else if (hasLower && char.IsUpper(c)) return true;
			}
			return false;
		}
	}

	/// <summary>
	/// Вспомогательный класс для онлайн-перевода динамических объектов (Задания, Письма, Уведомления).
	/// </summary>
	public static class DynamicTranslator
	{
		private static readonly AccessTools.FieldRef<Letter, TaggedString> letterLabelRef;
		private static readonly AccessTools.FieldRef<ChoiceLetter, TaggedString> choiceLetterTextRef;

		static DynamicTranslator()
		{
			try
			{
				letterLabelRef = AccessTools.FieldRefAccess<Letter, TaggedString>("label");
			}
			catch (Exception e)
			{
				letterLabelRef = null;
				GATLog.Warn("Не удалось инициализировать FieldRef для Letter.label: " + e.Message);
			}

			try
			{
				choiceLetterTextRef = AccessTools.FieldRefAccess<ChoiceLetter, TaggedString>("text");
			}
			catch (Exception e)
			{
				choiceLetterTextRef = null;
				GATLog.Warn("Не удалось инициализировать FieldRef для ChoiceLetter.text: " + e.Message);
			}
		}

		public static void TranslateQuest(Quest quest)
		{
			if (quest == null) return;

			// 1. Имя квеста (заголовок) — регистр как в оригинале (context="title"), кэшируем на диск (isVolatile=false)
			if (!string.IsNullOrEmpty(quest.name) && PlaceholderGuard.ShouldTranslate(quest.name))
			{
				string cached;
				if (TranslationCache.TryGet("title", quest.name, out cached))
				{
					quest.name = cached;
				}
				else
				{
					string origName = quest.name;
					TranslateWorker.Enqueue("title", origName, v => {
						DefPostProcessor.QueueApply(() => { quest.name = v; });
					}, isVolatile: false);
				}
			}

			// 2. Описание квеста — уникальный текст с именами и числами, переводим без сохранения в постоянный диск-кэш (isVolatile=true)
			string descRaw = quest.description.RawText;
			if (!string.IsNullOrEmpty(descRaw) && PlaceholderGuard.ShouldTranslate(descRaw))
			{
				string cached;
				if (TranslationCache.TryGet("description", descRaw, out cached))
				{
					quest.description = new TaggedString(cached);
				}
				else
				{
					TranslateWorker.Enqueue("description", descRaw, v => {
						DefPostProcessor.QueueApply(() => { quest.description = new TaggedString(v); });
					}, isVolatile: true);
				}
			}
		}

		public static void TranslateLetter(Letter letter)
		{
			if (letter == null) return;

			// Заголовок письма на панели — заглавная буква (context="title")
			try
			{
				if (letterLabelRef != null)
				{
					string labelRaw = letterLabelRef(letter).RawText;
					if (!string.IsNullOrEmpty(labelRaw) && PlaceholderGuard.ShouldTranslate(labelRaw))
					{
						string cached;
						if (TranslationCache.TryGet("title", labelRaw, out cached))
						{
							letterLabelRef(letter) = new TaggedString(cached);
						}
						else
						{
							TranslateWorker.Enqueue("title", labelRaw, v => {
								DefPostProcessor.QueueApply(() => { letterLabelRef(letter) = new TaggedString(v); });
							}, isVolatile: false);
						}
					}
				}
			}
			catch { }

			// Заголовок и текст диалогового окна письма (ChoiceLetter)
			var cl = letter as ChoiceLetter;
			if (cl != null)
			{
				if (!string.IsNullOrEmpty(cl.title) && PlaceholderGuard.ShouldTranslate(cl.title))
				{
					string cached;
					if (TranslationCache.TryGet("title", cl.title, out cached))
					{
						cl.title = cached;
					}
					else
					{
						string origTitle = cl.title;
						TranslateWorker.Enqueue("title", origTitle, v => {
							DefPostProcessor.QueueApply(() => { cl.title = v; });
						}, isVolatile: false);
					}
				}

				try
				{
					if (choiceLetterTextRef != null)
					{
						string textRaw = choiceLetterTextRef(cl).RawText;
						if (!string.IsNullOrEmpty(textRaw) && PlaceholderGuard.ShouldTranslate(textRaw))
						{
							string cached;
							if (TranslationCache.TryGet("description", textRaw, out cached))
							{
								choiceLetterTextRef(cl) = new TaggedString(cached);
							}
							else
							{
								TranslateWorker.Enqueue("description", textRaw, v => {
									DefPostProcessor.QueueApply(() => { choiceLetterTextRef(cl) = new TaggedString(v); });
								}, isVolatile: true);
							}
						}
					}
				}
				catch { }

				if (cl.quest != null)
				{
					TranslateQuest(cl.quest);
				}
			}
		}
	}

	/// <summary>
	/// СЛОЙ 2 — Keyed-строки. Перехватываем тот самый метод, через который игра
	/// достаёт любой перевод по ключу.
	/// </summary>
	[HarmonyPatch]
	public static class Patch_LoadedLanguage_TryGetTextFromKey
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден LoadedLanguage.TryGetTextFromKey — слой Keyed выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(LoadedLanguage), "TryGetTextFromKey");
		}

		[HarmonyPostfix]
		public static void Postfix(string key, ref TaggedString translated, ref bool __result)
		{
			var s = GATMod.Settings;
			if (s == null || !s.translateKeyed) return;

			string current = translated.RawText;

			if (__result && !PlaceholderGuard.ShouldTranslate(current)) return;
			if (!__result || string.IsNullOrEmpty(current) || current == key) return;
			if (!PlaceholderGuard.ShouldTranslate(current)) return;

			LanguageExporter.NoteKeyed(key, current);

			string cached;
			if (TranslationCache.TryGet("keyed", current, out cached))
			{
				translated = new TaggedString(cached);
				return;
			}

			TranslateWorker.Enqueue("keyed", current);
		}
	}

	/// <summary>
	/// СЛОЙ 3 V2 — Перехват отрисовки текста Widgets.Label(Rect, string).
	/// </summary>
	[HarmonyPatch]
	public static class Patch_Widgets_Label_RectString
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден Widgets.Label(Rect, string) — перехват текста UI выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new Type[] { typeof(UnityEngine.Rect), typeof(string) });
		}

		[HarmonyPrefix]
		public static void Prefix(ref string label)
		{
			var s = GATMod.Settings;
			if (s == null || !s.translateWidgets) return;
			if (string.IsNullOrEmpty(label) || label.Length > 300) return;

			string cached;
			if (TranslationCache.TryGetFlat(label, out cached))
				label = cached;
			else
				UiHarvest.Note(label);
		}
	}

	/// <summary>
	/// СЛОЙ 3 V2 — Перехват отрисовки текста Widgets.Label(Rect, TaggedString).
	/// </summary>
	[HarmonyPatch]
	public static class Patch_Widgets_Label_RectTaggedString
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден Widgets.Label(Rect, TaggedString) — перехват текста TaggedString UI выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new Type[] { typeof(UnityEngine.Rect), typeof(TaggedString) });
		}

		[HarmonyPrefix]
		public static void Prefix(ref TaggedString label)
		{
			var s = GATMod.Settings;
			if (s == null || !s.translateWidgets) return;
			string raw = label.RawText;
			if (string.IsNullOrEmpty(raw) || raw.Length > 300) return;

			string cached;
			if (TranslationCache.TryGetFlat(raw, out cached))
				label = new TaggedString(cached);
			else
				UiHarvest.Note(raw);
		}
	}

	/// <summary>
	/// СЛОЙ 3 V2 — Перехват отрисовки текста Widgets.LabelFit(Rect, string).
	/// </summary>
	[HarmonyPatch]
	public static class Patch_Widgets_LabelFit
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден Widgets.LabelFit(Rect, string) — перехват подгоняемого текста UI выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(Widgets), nameof(Widgets.LabelFit), new Type[] { typeof(UnityEngine.Rect), typeof(string) });
		}

		[HarmonyPrefix]
		public static void Prefix(ref string label)
		{
			var s = GATMod.Settings;
			if (s == null || !s.translateWidgets) return;
			if (string.IsNullOrEmpty(label) || label.Length > 300) return;

			string cached;
			if (TranslationCache.TryGetFlat(label, out cached))
				label = cached;
			else
				UiHarvest.Note(label);
		}
	}

	/// <summary>
	/// СЛОЙ 3 V2 — Перехват текста кнопок Widgets.ButtonText (перегрузка 1).
	/// </summary>
	[HarmonyPatch]
	public static class Patch_Widgets_ButtonText
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден Widgets.ButtonText (перегрузка 1) — перехват текста кнопок UI выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonText), new Type[] {
				typeof(UnityEngine.Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(Nullable<UnityEngine.TextAnchor>)
			});
		}

		[HarmonyPrefix]
		public static void Prefix(ref string label)
		{
			var s = GATMod.Settings;
			if (s == null || !s.translateWidgets) return;
			if (string.IsNullOrEmpty(label) || label.Length > 300) return;

			string cached;
			if (TranslationCache.TryGetFlat(label, out cached))
				label = cached;
			else
				UiHarvest.Note(label);
		}
	}

	/// <summary>
	/// СЛОЙ 3 V2 — Перехват текста кнопок Widgets.ButtonText (перегрузка 2 с параметром Color).
	/// </summary>
	[HarmonyPatch]
	public static class Patch_Widgets_ButtonText_Color
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден Widgets.ButtonText (перегрузка 2 с Color) — перехват цветных кнопок UI выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonText), new Type[] {
				typeof(UnityEngine.Rect), typeof(string), typeof(bool), typeof(bool), typeof(UnityEngine.Color), typeof(bool), typeof(Nullable<UnityEngine.TextAnchor>)
			});
		}

		[HarmonyPrefix]
		public static void Prefix(ref string label)
		{
			var s = GATMod.Settings;
			if (s == null || !s.translateWidgets) return;
			if (string.IsNullOrEmpty(label) || label.Length > 300) return;

			string cached;
			if (TranslationCache.TryGetFlat(label, out cached))
				label = cached;
			else
				UiHarvest.Note(label);
		}
	}

	/// <summary>Перехват добавления нового задания в QuestManager.</summary>
	[HarmonyPatch]
	public static class Patch_QuestManager_Add
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден QuestManager.Add — динамический перехват квестов выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(QuestManager), "Add");
		}

		[HarmonyPostfix]
		public static void Postfix(Quest quest)
		{
			try { DynamicTranslator.TranslateQuest(quest); } catch { }
		}
	}

	/// <summary>Перехват открытия окна Заданий (MainTabWindow_Quests) — переводит все активные задания.</summary>
	[HarmonyPatch]
	public static class Patch_MainTabWindow_Quests_PreOpen
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден MainTabWindow_Quests.PreOpen — автоперехват списка квестов выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(MainTabWindow_Quests), "PreOpen");
		}

		[HarmonyPostfix]
		public static void Postfix()
		{
			try
			{
				if (Find.QuestManager != null && Find.QuestManager.QuestsListForReading != null)
				{
					var list = Find.QuestManager.QuestsListForReading;
					for (int i = 0; i < list.Count; i++)
					{
						DynamicTranslator.TranslateQuest(list[i]);
					}
				}
			}
			catch { }
		}
	}

	/// <summary>Перехват получения писем и всплывающих событий (LetterStack.ReceiveLetter).</summary>
	[HarmonyPatch]
	public static class Patch_LetterStack_ReceiveLetter
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден LetterStack.ReceiveLetter — перевод писем выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(LetterStack), "ReceiveLetter", new Type[] {
				typeof(Letter), typeof(string), typeof(int), typeof(bool)
			});
		}

		[HarmonyPrefix]
		public static void Prefix(Letter let)
		{
			try { DynamicTranslator.TranslateLetter(let); } catch { }
		}
	}

	/// <summary>Применяет отложенные изменения Defs, UI и динамических объектов в главном потоке каждый кадр.</summary>
	[HarmonyPatch]
	public static class Patch_Root_Update
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден Verse.Root.Update — сброс Defs и UI в главном потоке выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(Root), "Update");
		}

		[HarmonyPostfix]
		public static void Postfix()
		{
			try { DefPostProcessor.DrainApply(); } catch { }
			try { UiHarvest.Drain(); } catch { }
		}
	}

	/// <summary>Периодический сброс кэша на диск во время игры.</summary>
	[HarmonyPatch]
	public static class Patch_TickManager_Autosave
	{
		private static int counter;

		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден TickManager.DoSingleTick — автосохранение кэша выключено.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(TickManager), "DoSingleTick");
		}

		[HarmonyPostfix]
		public static void Postfix()
		{
			if (++counter < 7200) return; // ~2 игровых часа
			counter = 0;
			try { TranslationCache.Flush(); } catch { }
		}
	}

	/// <summary>
	/// СЛОЙ 3 V2 — Перехват всплывающих подсказок TooltipHandler.TipRegion(Rect, string).
	/// </summary>
	[HarmonyPatch]
	public static class Patch_TooltipHandler_TipRegion_String
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден TooltipHandler.TipRegion(Rect, string) — перехват подсказок выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(TooltipHandler), nameof(TooltipHandler.TipRegion), new Type[] { typeof(UnityEngine.Rect), typeof(string) });
		}

		[HarmonyPrefix]
		public static void Prefix(ref string text)
		{
			var s = GATMod.Settings;
			if (s == null || !s.translateWidgets) return;
			if (string.IsNullOrEmpty(text) || text.Length > 600) return;

			string cached;
			if (TranslationCache.TryGetFlat(text, out cached))
				text = cached;
			else
				UiHarvest.Note(text);
		}
	}

	/// <summary>
	/// СЛОЙ 3 V2 — Перехват всплывающих подсказок TooltipHandler.TipRegion(Rect, TaggedString).
	/// </summary>
	[HarmonyPatch]
	public static class Patch_TooltipHandler_TipRegion_TaggedString
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден TooltipHandler.TipRegion(Rect, TaggedString) — перехват TaggedString подсказок выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(TooltipHandler), nameof(TooltipHandler.TipRegion), new Type[] { typeof(UnityEngine.Rect), typeof(TaggedString) });
		}

		[HarmonyPrefix]
		public static void Prefix(ref TaggedString text)
		{
			var s = GATMod.Settings;
			if (s == null || !s.translateWidgets) return;
			string raw = text.RawText;
			if (string.IsNullOrEmpty(raw) || raw.Length > 600) return;

			string cached;
			if (TranslationCache.TryGetFlat(raw, out cached))
				text = new TaggedString(cached);
			else
				UiHarvest.Note(raw);
		}
	}

	/// <summary>
	/// СЛОЙ 3 V2 — Перехват подписей чекбоксов Widgets.CheckboxLabeled.
	/// </summary>
	[HarmonyPatch]
	public static class Patch_Widgets_CheckboxLabeled
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден Widgets.CheckboxLabeled — перехват чекбоксов выключен.");
			return ok;
		}

		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(Widgets), nameof(Widgets.CheckboxLabeled), new Type[] {
				typeof(UnityEngine.Rect), typeof(string), typeof(bool), typeof(bool), typeof(UnityEngine.Texture2D), typeof(UnityEngine.Texture2D), typeof(bool)
			});
		}

		[HarmonyPrefix]
		public static void Prefix(ref string label)
		{
			var s = GATMod.Settings;
			if (s == null || !s.translateWidgets) return;
			if (string.IsNullOrEmpty(label) || label.Length > 300) return;

			string cached;
			if (TranslationCache.TryGetFlat(label, out cached))
				label = cached;
			else
				UiHarvest.Note(label);
		}
	}
}
