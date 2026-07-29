using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace GlobalAutoTranslator
{
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

	/// <summary>Применяет отложенные изменения Defs в главном потоке каждый кадр.</summary>
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
