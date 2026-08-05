using System;
using HarmonyLib;
using Verse;

namespace GlobalAutoTranslator
{
	/// <summary>
	/// Точка входа. [StaticConstructorOnStartup] вызывается после того, как все Defs
	/// загружены и разрешены — именно то, что нужно для пост-обработки.
	/// </summary>
	[StaticConstructorOnStartup]
	public static class GATBoot
	{
		public static Harmony HarmonyInst;

		static GATBoot()
		{
			try
			{
				HarmonyInst = new Harmony("ayder.globalautotranslator");
				HarmonyInst.PatchAll();
			}
			catch (Exception e)
			{
				GATLog.Err("Harmony-патчи не применились: " + e);
			}

			try
			{
				TranslationCache.Load();
				TranslateWorker.Start();
			}
			catch (Exception e)
			{
				GATLog.Err("Не удалось запустить переводчик: " + e);
				return;
			}

			// Первый прогон: применяем то, что уже в кэше, остальное ставим в очередь.
			// Игра НЕ ждёт сеть — загрузка идёт дальше моментально.
			try
			{
				if (GATMod.Settings.translateDefs)
					DefPostProcessor.Run();
			}
			catch (Exception e)
			{
				GATLog.Err("Ошибка пост-обработки Defs: " + e);
			}

			GATLog.Msg("Запущен. Кэш: " + TranslationCache.RootDir);
			
			if (GATMod.Settings.verboseLogging)
			{
				SelfTest.Run();
			}
		}
	}

	/// <summary>Сброс кэша на диск при выходе из игры.</summary>
	[HarmonyPatch(typeof(Root), "Shutdown")]
	public static class Patch_Root_Shutdown
	{
		[HarmonyPrefix]
		public static void Prefix()
		{
			try { TranslateWorker.Stop(); } catch { }
		}
	}
}
