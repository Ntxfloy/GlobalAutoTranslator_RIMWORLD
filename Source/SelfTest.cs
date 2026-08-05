using System.Collections.Generic;
using System.Text;
using Verse;

namespace GlobalAutoTranslator
{
	public static class SelfTest
	{
		public static void Run()
		{
			var sb = new StringBuilder();
			sb.AppendLine("=== GlobalAutoTranslator SelfTest ===");

			string reason;
			
			// 1. PlaceholderGuard пропускает steel longsword
			bool ok1 = PlaceholderGuard.Validate("steel longsword", "стальной длинный меч", out reason);
			sb.AppendLine((ok1 ? "[OK]" : "[FAIL]") + " 1. steel longsword" + (ok1 ? "" : " <- " + reason));

			// 2. PlaceholderGuard пропускает {PAWN_labelShort} has been downed by {0}.
			bool ok2 = PlaceholderGuard.Validate("{PAWN_labelShort} has been downed by {0}.", "{PAWN_labelShort} сбит(а) {0}.", out reason);
			sb.AppendLine((ok2 ? "[OK]" : "[FAIL]") + " 2. {PAWN_labelShort} has been downed by {0}." + (ok2 ? "" : " <- " + reason));

			// 3. PlaceholderGuard отклоняет ответ, где [PAWN_pronoun] превратился в {PAWN_gender ? он : она}
			bool ok3 = !PlaceholderGuard.Validate("[PAWN_pronoun]", "{PAWN_gender ? он : она}", out reason);
			sb.AppendLine((ok3 ? "[OK]" : "[FAIL]") + " 3. [PAWN_pronoun] -> {PAWN_gender ? он : она}" + (ok3 ? "" : " <- ложно пропущено"));

			// 4. PlaceholderGuard отклоняет ответ, где переведено содержимое [[ count ]]
			bool ok4 = !PlaceholderGuard.Validate("[[ count ]]", "[[ количество ]]", out reason);
			sb.AppendLine((ok4 ? "[OK]" : "[FAIL]") + " 4. [[ count ]] -> [[ количество ]]" + (ok4 ? "" : " <- ложно пропущено"));

			// 5. теги <color=#FF0000>Critical</color> failure сохраняются символ в символ
			bool ok5 = PlaceholderGuard.Validate("<color=#FF0000>Critical</color> failure", "<color=#FF0000>Критический</color> сбой", out reason);
			sb.AppendLine((ok5 ? "[OK]" : "[FAIL]") + " 5. <color=#FF0000>Critical</color> failure" + (ok5 ? "" : " <- " + reason));

			// 6. TryGetFlat: Put("ui", "Global Animation Speed", "Скорость анимации"), запрос "Global Animation Speed: 100%"
			TranslationCache.Put("ui", "Global Animation Speed", "Скорость анимации");
			string res6;
			bool ok6 = TranslationCache.TryGetFlat("Global Animation Speed: 100%", out res6) && res6 == "Скорость анимации: 100%";
			sb.AppendLine((ok6 ? "[OK]" : "[FAIL]") + " 6. Global Animation Speed: 100% -> " + (res6 ?? "null") + (ok6 ? "" : " (ожидалось: Скорость анимации: 100%)"));

			// 7. TryGetFlat: Put("ui", "Loading", "Загрузка"), запрос "Loading..."
			TranslationCache.Put("ui", "Loading", "Загрузка");
			string res7;
			bool ok7 = TranslationCache.TryGetFlat("Loading...", out res7) && res7 == "Загрузка...";
			sb.AppendLine((ok7 ? "[OK]" : "[FAIL]") + " 7. Loading... -> " + (res7 ?? "null") + (ok7 ? "" : " (ожидалось: Загрузка...)"));

			// 8. TryGetTemplated: реальное письмо головорезов из кэша (двойные переносы, плейсхолдеры).
			TranslationCache.Put("title", 
				"A band of thugs sent by {0} call you from nearby. \n\n They offer to keep your colony safe from any threats, even the ones potentially caused by them, as long as you pay a fee of {1} silver. \n\n Be warned - refusal can anger the thugs. ", 
				"С вами связывается группа головорезов, отправленная фракцией {0}.\n\nОни предлагают защитить вашу колонию от любых угроз, включая те, которые могут исходить от них самих, если вы заплатите им {1} серебра.\n\nИмейте в виду: отказ может разозлить головорезов.");
			string res8;
			bool ok8 = TranslationCache.TryGetTemplated(
				"A band of thugs sent by Племя Бардал call you from nearby. \n\n They offer to keep your colony safe from any threats, even the ones potentially caused by them, as long as you pay a fee of 350 silver. \n\n Be warned - refusal can anger the thugs. ", 
				out res8);
			bool match8 = res8 == "С вами связывается группа головорезов, отправленная фракцией Племя Бардал.\n\nОни предлагают защитить вашу колонию от любых угроз, включая те, которые могут исходить от них самих, если вы заплатите им 350 серебра.\n\nИмейте в виду: отказ может разозлить головорезов.";
			sb.AppendLine((ok8 && match8 ? "[OK]" : "[FAIL]") + " 8. Thugs letter -> " + (res8 == null ? "null" : res8.Replace("\n", "\\n")));

			// 9. TryGetTemplated: похожий якорь, но другая структура.
			string res9;
			bool ok9 = !TranslationCache.TryGetTemplated("A band of thugs sent by Племя Бардал call you from nearby. \n\n They want to chat.", out res9);
			sb.AppendLine((ok9 ? "[OK]" : "[FAIL]") + " 9. Non-matching thugs letter -> " + (res9 == null ? "отклонено" : "пропущено: " + res9.Replace("\n", "\\n")));

			// 10. повторный плейсхолдер.
			TranslationCache.Put("title", "{0} and again and again {0}", "{0} и снова и снова {0}");
			string res10;
			bool ok10 = TranslationCache.TryGetTemplated("Test and again and again Test", out res10);
			bool match10 = res10 == "Test и снова и снова Test";
			sb.AppendLine((ok10 && match10 ? "[OK]" : "[FAIL]") + " 10. Repeated placeholder -> " + (res10 ?? "null") + (ok10 && match10 ? "" : " (ожидалось: Test и снова и снова Test)"));

			// 11. короткий якорь отклоняется (длина литерала меньше 12)
			TranslationCache.Put("title", "abc {0} def", "абв {0} где");
			string res11;
			bool ok11 = !TranslationCache.TryGetTemplated("abc 123 def", out res11);
			sb.AppendLine((ok11 ? "[OK]" : "[FAIL]") + " 11. Short anchor rejected -> " + (res11 == null ? "отклонено" : "ошибка: " + res11));

			// 12. грамматические правила отклоняются
			bool ok12 = !PlaceholderGuard.ShouldTranslate("tradeAdj_fem->luxurious");
			sb.AppendLine((ok12 ? "[OK]" : "[FAIL]") + " 12. Grammar rule rejected -> " + (ok12 ? "отклонено" : "пропущено"));

			// 13. Валидация и маскировка маркеров (3 честные проверки)
			{
				string maskSrc = "{PAWN_labelShort} was downed by {0}. [PAWN_pronoun] screamed.";
				Dictionary<int, string> maskMap;
				string masked = PlaceholderGuard.MaskPlaceholders(maskSrc, out maskMap); // ⟦1⟧ was downed by ⟦2⟧. ⟦3⟧ screamed.

				// Проверка 13a: корректный ответ модели проходит круг
				string validModelReply = "⟦1⟧ повержен(а) ⟦2⟧. ⟦3⟧ закричал(а).";
				string r1;
				bool ok13a = PlaceholderGuard.ValidateMarkers(validModelReply, maskMap, out r1);
				string unmasked13a = PlaceholderGuard.UnmaskPlaceholders(validModelReply, maskMap);
				bool pass13a = ok13a && unmasked13a.Contains("{PAWN_labelShort}") && unmasked13a.Contains("{0}") && unmasked13a.Contains("[PAWN_pronoun]");

				// Проверка 13b: ответ с выброшенным маркером ⟦2⟧ отклоняется
				string missingModelReply = "⟦1⟧ повержен(а). ⟦3⟧ закричал(а).";
				string r2;
				bool ok13b = !PlaceholderGuard.ValidateMarkers(missingModelReply, maskMap, out r2);

				// Проверка 13c: ответ с придуманным маркером ⟦4⟧ отклоняется по Validate (нераспознанные маркеры)
				string extraModelReply = "⟦1⟧ повержен(а) ⟦2⟧. ⟦3⟧ закричал(а) ⟦4⟧.";
				string r3;
				PlaceholderGuard.ValidateMarkers(extraModelReply, maskMap, out r3);
				string unmasked13c = PlaceholderGuard.UnmaskPlaceholders(extraModelReply, maskMap);
				string valReason;
				bool ok13c = !PlaceholderGuard.Validate(maskSrc, unmasked13c, out valReason) && valReason.Contains("нераспознанные маркеры");

				bool ok13 = pass13a && ok13b && ok13c;
				sb.AppendLine((ok13 ? "[OK]" : "[FAIL]") + " 13. Marker validation (full round-trip, missing rejected, extra rejected)");
			}

			// 14. Неприкосновенность гендерных тернарников в MaskPlaceholders
			{
				string ternarySrc = "{PREDATOR} {PREDATOR_gender ? attacked : attacked} {PREY_labelShort}";
				Dictionary<int, string> map14;
				string masked14 = PlaceholderGuard.MaskPlaceholders(ternarySrc, out map14);
				bool hasTernary = masked14.Contains("{PREDATOR_gender ? attacked : attacked}");
				bool ok14 = map14.Count == 2 && hasTernary; // {PREDATOR} → ⟦1⟧, {PREY_labelShort} → ⟦2⟧, тернарник не замаскирован
				sb.AppendLine((ok14 ? "[OK]" : "[FAIL]") + " 14. Gender ternary preserved in MaskPlaceholders -> " + (hasTernary ? "сохранён" : "испорчен"));
			}

			GATLog.Msg(sb.ToString());
		}
	}
}
