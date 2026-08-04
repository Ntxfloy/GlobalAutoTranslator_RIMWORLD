using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;

namespace GlobalAutoTranslator
{
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
	/// достаёт любой перевод по ключу. TargetMethod вместо атрибута — чтобы мод не падал,
	/// если Ludeon поменяет сигнатуру в обновлении.
	/// </summary>
	[HarmonyPatch]
	public static class Patch_LoadedLanguage_TryGetTextFromKey
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден LoadedLanguage.TryGetTextFromKey — слой Keyed выключен. " +
			                        "Открой Assembly-CSharp.dll в ILSpy и сверь имя метода.");
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

			// Игра нашла перевод сама — не лезем.
			if (__result && !PlaceholderGuard.ShouldTranslate(current)) return;

			// Если перевода нет, RimWorld отдаёт сам ключ — такое переводить бессмысленно.
			if (!__result || string.IsNullOrEmpty(current) || current == key) return;
			if (!PlaceholderGuard.ShouldTranslate(current)) return;

			// Запоминаем связку key -> английский текст, чтобы потом собрать Keyed-XML.
			LanguageExporter.NoteKeyed(key, current);

			string cached;
			if (TranslationCache.TryGet("keyed", current, out cached))
			{
				translated = new TaggedString(cached);
				return;
			}

			// Не блокируем кадр: ставим в очередь и показываем оригинал.
			TranslateWorker.Enqueue("keyed", current);
		}
	}

	/// <summary>
	/// СЛОЙ 3 — аварийный захват того, что не прошло через слои 1-2 (хардкод в C# чужих модов).
	///
	/// КРИТИЧНО: Widgets.Label вызывается тысячи раз в секунду (IMGUI рисует каждый кадр,
	/// иногда по несколько раз за кадр). Здесь РАЗРЕШЕН только поиск в памяти.
	/// НИКАКИХ запросов, никакой записи в очередь, никаких регулярок — иначе микрофризы.
	/// </summary>
	[HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new Type[] { typeof(UnityEngine.Rect), typeof(string) })]
	public static class Patch_Widgets_Label
	{
		[HarmonyPrefix]
		public static void Prefix(ref string label)
		{
			var s = GATMod.Settings;
			if (s == null || !s.translateWidgets) return;
			if (string.IsNullOrEmpty(label) || label.Length > 200) return;

			string cached;
			if (TranslationCache.TryGetFlat(label, out cached))
				label = cached;
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

	/// <summary>Применяет отложенные изменения Defs и динамических объектов в главном потоке каждый кадр.</summary>
	[HarmonyPatch]
	public static class Patch_Root_Update
	{
		public static bool Prepare()
		{
			bool ok = TargetMethod() != null;
			if (!ok) GATLog.Warn("Не найден Verse.Root.Update — сброс Defs в главном потоке выключен.");
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
		}
	}

	/// <summary>Периодический сброс кэша на диск во время игры.</summary>
	[HarmonyPatch(typeof(TickManager), "DoSingleTick")]
	public static class Patch_TickManager_Autosave
	{
		private static int counter;

		[HarmonyPostfix]
		public static void Postfix()
		{
			if (++counter < 7200) return; // ~2 игровых часа
			counter = 0;
			try { TranslationCache.Flush(); } catch { }
		}
	}
}
