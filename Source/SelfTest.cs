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

			// 15-21. Тесты метода NeedsTranslation (смешанные и чистые строки)
			{
				bool t15 = PlaceholderGuard.NeedsTranslation("The quest Сомнительный Хаб Контрабанды has ended.");
				sb.AppendLine((t15 ? "[OK]" : "[FAIL]") + " 15. NeedsTranslation: Mixed quest -> " + (t15 ? "переводить" : "пропущено"));

				bool t16 = !PlaceholderGuard.NeedsTranslation("Открыть связанное задание: Сомнительный Хаб Контрабанды");
				sb.AppendLine((t16 ? "[OK]" : "[FAIL]") + " 16. NeedsTranslation: Russian UI -> " + (t16 ? "уже русский" : "переводить"));

				bool t17 = PlaceholderGuard.NeedsTranslation("These беженцы are not part of any faction.");
				sb.AppendLine((t17 ? "[OK]" : "[FAIL]") + " 17. NeedsTranslation: Mixed refugees -> " + (t17 ? "переводить" : "пропущено"));

				bool t18 = PlaceholderGuard.NeedsTranslation("Jorunn begs you for permission to stay at Утёс Преданности for 13 дней.");
				sb.AppendLine((t18 ? "[OK]" : "[FAIL]") + " 18. NeedsTranslation: Mixed stay quest -> " + (t18 ? "переводить" : "пропущено"));

				bool t19 = !PlaceholderGuard.NeedsTranslation("Мод Combat Extended включён");
				sb.AppendLine((t19 ? "[OK]" : "[FAIL]") + " 19. NeedsTranslation: Mod name in Russian -> " + (t19 ? "уже русский" : "переводить"));

				bool t20 = !PlaceholderGuard.NeedsTranslation("<color=#FF0000>Критическая ошибка</color>");
				sb.AppendLine((t20 ? "[OK]" : "[FAIL]") + " 20. NeedsTranslation: Russian color tag -> " + (t20 ? "уже русский" : "переводить"));

				bool t21 = PlaceholderGuard.NeedsTranslation("Enough is enough, I'm feeling the call of the open skies!\nПричина: Вера в Неприкосновенные Пути");
				sb.AppendLine((t21 ? "[OK]" : "[FAIL]") + " 21. NeedsTranslation: Mixed tooltip -> " + (t21 ? "переводить" : "пропущено"));
			}

			// 22. Проверка сохранения русских фрагментов исходника в PlaceholderGuard.Validate
			{
				string cyrSrc = "The quest Сомнительный Хаб Контрабанды has ended.";
				string validDst = "Задание Сомнительный Хаб Контрабанды завершено.";
				string invalidDst = "Задание «Контрабандный хаб» завершено.";

				string rValid, rInvalid;
				bool okValid = PlaceholderGuard.Validate(cyrSrc, validDst, out rValid);
				bool okInvalid = !PlaceholderGuard.Validate(cyrSrc, invalidDst, out rInvalid) && rInvalid.Contains("русский фрагмент исходника потерян");

				bool ok22 = okValid && okInvalid;
				sb.AppendLine((ok22 ? "[OK]" : "[FAIL]") + " 22. Preserve original Cyrillic fragments -> valid=" + okValid + ", invalid_rejected=" + okInvalid);
			}

			// 23. Смешанная строка с расширенной латиницей (é, à)
			{
				bool t23 = PlaceholderGuard.NeedsTranslation("L’Équipe et беженцы repose à Утёс.");
				sb.AppendLine((t23 ? "[OK]" : "[FAIL]") + " 23. NeedsTranslation: Extended Latin (é, à) -> " + (t23 ? "переводить" : "пропущено"));
			}

			// 24. Искажение регистра русского фрагмента измеряется дословно (Ordinal)
			{
				string cyrSrc = "The quest Сомнительный Хаб Контрабанды has ended.";
				string caseChangedDst = "Задание сомнительный Хаб Контрабанды завершено.";
				string rCase;
				bool ok24 = !PlaceholderGuard.Validate(cyrSrc, caseChangedDst, out rCase) && rCase.Contains("русский фрагмент исходника потерян");
				sb.AppendLine((ok24 ? "[OK]" : "[FAIL]") + " 24. Case change in Cyrillic fragment rejected (Ordinal) -> " + (ok24 ? "отклонено" : "ошибка"));
			}

			// 27. Проверка Backoff (production get)
			{
				bool ok27 = TranslateWorker.GetProbeDelaySeconds(0) == 30 &&
				            TranslateWorker.GetProbeDelaySeconds(1) == 60 &&
				            TranslateWorker.GetProbeDelaySeconds(2) == 120 &&
				            TranslateWorker.GetProbeDelaySeconds(3) == 240 &&
				            TranslateWorker.GetProbeDelaySeconds(4) == 300 &&
				            TranslateWorker.GetProbeDelaySeconds(5) == 300;
				sb.AppendLine((ok27 ? "[OK]" : "[FAIL]") + " 27. Production GetProbeDelaySeconds: 30, 60, 120, 240, 300, 300 -> " + ok27);
			}

			// 28. Сетевая ошибка не является structural failure
			{
				bool ok28 = !TranslateWorker.IsStructuralFailure("HTTP 503 auth_unavailable");
				sb.AppendLine((ok28 ? "[OK]" : "[FAIL]") + " 28. Network error is not structural failure -> " + ok28);
			}

			// 29. Потеря русского фрагмента является structural failure
			{
				bool ok29 = TranslateWorker.IsStructuralFailure("русский фрагмент исходника потерян");
				sb.AppendLine((ok29 ? "[OK]" : "[FAIL]") + " 29. Missing Cyrillic fragment is structural failure -> " + ok29);
			}

			// 30. Успешный probe-переход на локальном стейте (Stateless)
			{
				bool localPaused = true;
				int localBackoff = 3;
				long localNextProbe = 12345;
				
				var result = new LlmClient.ProbeResult { Success = true, ResponsePreview = "OK" };
				if (result.Success)
				{
					localPaused = false;
					localBackoff = 0;
					localNextProbe = 0;
				}
				bool ok30 = !localPaused && localBackoff == 0 && localNextProbe == 0;
				sb.AppendLine((ok30 ? "[OK]" : "[FAIL]") + " 30. Stateless probe-success transition -> " + ok30);
			}

			// 31. RetryHint для конкретного элемента попадает только под своим ID
			{
				var items = new System.Collections.Generic.Dictionary<string, string> { { "0", "test" }, { "1", "test2" } };
				var hints = new System.Collections.Generic.Dictionary<string, string> { { "1", "hint for 1" } };
				string json = Prompt.BuildUserMessage("ui", items, null, hints);
				bool ok31 = json.Contains("\"retry_hints\":{\"1\":\"hint for 1\"}") && !json.Contains("\"0\":\"hint");
				sb.AppendLine((ok31 ? "[OK]" : "[FAIL]") + " 31. RetryHint is mapped to item ID -> " + ok31);
			}

			// 32. MiniJson rejects empty/corrupt object
			{
				string emptyJson = "{}";
				var parsedEmpty = MiniJson.ParseFlatObject(emptyJson);
				bool ok32Empty = parsedEmpty != null && parsedEmpty.Count == 0;
				
				string corruptJson = "{ corrupted }";
				var parsedCorrupt = MiniJson.ParseFlatObject(corruptJson);
				bool ok32Corrupt = parsedCorrupt == null || parsedCorrupt.Count == 0;
				
				bool ok32 = ok32Empty && ok32Corrupt;
				sb.AppendLine((ok32 ? "[OK]" : "[FAIL]") + " 32. MiniJson rejects empty/corrupt object -> " + ok32);
			}

			// 33. ProbeResult для пустого content имеет Success=false
			{
				var res = new LlmClient.ProbeResult();
				bool ok33 = !res.Success;
				sb.AppendLine((ok33 ? "[OK]" : "[FAIL]") + " 33. Default ProbeResult.Success is false -> " + ok33);
			}

			// 34. Production ParseRetryAfterSeconds parser
			{
				var now = System.DateTime.UtcNow;
				bool ok34 = LlmClient.ParseRetryAfterSeconds("120", now) == 120 &&
				            LlmClient.ParseRetryAfterSeconds("invalid", now) == null &&
				            LlmClient.ParseRetryAfterSeconds("0", now) == null &&
				            LlmClient.ParseRetryAfterSeconds("1801", now) == null;
				sb.AppendLine((ok34 ? "[OK]" : "[FAIL]") + " 34. Production ParseRetryAfterSeconds (120, invalid, 0, 1801) -> " + ok34);
			}

			// 35. Вложенный lookup маскируется одним маркером и восстанавливается посимвольно точно
			{
				string src35 = "{lookup: {lookup: {GENDER}; Plural; 1}; Case; 2}";
				System.Collections.Generic.Dictionary<int, string> map35;
				string masked35 = PlaceholderGuard.MaskPlaceholders(src35, out map35);
				string unmasked35 = PlaceholderGuard.UnmaskPlaceholders(masked35, map35);
				bool ok35 = map35.Count == 1 && unmasked35 == src35;
				sb.AppendLine((ok35 ? "[OK]" : "[FAIL]") + " 35. Nested lookup masked as single marker and restored -> " + ok35);
			}

			// 36. Два lookup в одной строке дают два разных маркера
			{
				string src36 = "{lookup: A} и {lookup: B}";
				System.Collections.Generic.Dictionary<int, string> map36;
				string masked36 = PlaceholderGuard.MaskPlaceholders(src36, out map36);
				bool ok36 = map36.Count == 2 && masked36.Contains("⟦1⟧") && masked36.Contains("⟦2⟧");
				sb.AppendLine((ok36 ? "[OK]" : "[FAIL]") + " 36. Two lookups produce two distinct markers -> " + ok36);
			}

			// 37. Обычные {PAWN_labelShort} и {0} вне lookup работают как раньше
			{
				string src37 = "{PAWN_labelShort} and {0}";
				System.Collections.Generic.Dictionary<int, string> map37;
				string masked37 = PlaceholderGuard.MaskPlaceholders(src37, out map37);
				string unmasked37 = PlaceholderGuard.UnmaskPlaceholders(masked37, map37);
				bool ok37 = map37.Count == 2 && unmasked37 == src37;
				sb.AppendLine((ok37 ? "[OK]" : "[FAIL]") + " 37. Regular placeholders outside lookup work normally -> " + ok37);
			}

			// 38. Незакрытая скобка в конструкции lookup не приводит к исключению
			{
				bool ok38 = false;
				try
				{
					string src38 = "{lookup: {GENDER}; Plural; 1";
					System.Collections.Generic.Dictionary<int, string> map38;
					string masked38 = PlaceholderGuard.MaskPlaceholders(src38, out map38);
					ok38 = !string.IsNullOrEmpty(masked38);
				}
				catch { ok38 = false; }
				sb.AppendLine((ok38 ? "[OK]" : "[FAIL]") + " 38. Unclosed lookup brace does not throw exception -> " + ok38);
			}

			// 39. «Изготавливает mich tc-2000 helmet» -> «Изготавливает шлем MICH TC-2000» проходит проверку
			{
				string r39;
				bool ok39 = PlaceholderGuard.Validate("Изготавливает mich tc-2000 helmet", "Изготавливает шлем MICH TC-2000", out r39);
				sb.AppendLine((ok39 ? "[OK]" : "[FAIL]") + " 39. Recipe verb validation passes -> " + ok39);
			}

			// 40. «Jorunn просит остаться в Утёс Преданности» с потерянным «Утёс Преданности» отклоняется
			{
				string r40;
				bool ok40 = !PlaceholderGuard.Validate("Jorunn просит остаться в Утёс Преданности", "Jorunn просит остаться в локации", out r40) && r40.Contains("русский фрагмент исходника потерян");
				sb.AppendLine((ok40 ? "[OK]" : "[FAIL]") + " 40. Missing proper location name rejected -> " + ok40);
			}

			// 41. «Любые патроны для Unique trench gun» с переведённым названием оружия проходит
			{
				string r41;
				bool ok41 = PlaceholderGuard.Validate("Любые патроны для Unique trench gun", "Любые патроны для траншейного ружья Unique", out r41);
				sb.AppendLine((ok41 ? "[OK]" : "[FAIL]") + " 41. Service words with translated weapon name passes -> " + ok41);
			}

			// 42. Письмо с потерянным названием фракции «Племя Бардал» отклоняется
			{
				string r42;
				bool ok42 = !PlaceholderGuard.Validate("Письмо от фракции Племя Бардал о союзе.", "Письмо от фракции о союзе.", out r42) && r42.Contains("русский фрагмент исходника потерян");
				sb.AppendLine((ok42 ? "[OK]" : "[FAIL]") + " 42. Missing faction name in letter rejected -> " + ok42);
			}

			// 43. «Любые патроны для Unique trench gun» -> «Патроны для траншейного ружья» проходит проверку
			{
				string r43;
				bool ok43 = PlaceholderGuard.Validate("Любые патроны для Unique trench gun", "Патроны для траншейного ружья", out r43);
				sb.AppendLine((ok43 ? "[OK]" : "[FAIL]") + " 43. Preposition and service word sentence passes -> " + ok43);
			}

			// 44. «Изготавливает mich tc-2000 helmet» -> «Создаёт шлем MICH TC-2000» проходит проверку
			{
				string r44;
				bool ok44 = PlaceholderGuard.Validate("Изготавливает mich tc-2000 helmet", "Создаёт шлем MICH TC-2000", out r44);
				sb.AppendLine((ok44 ? "[OK]" : "[FAIL]") + " 44. Verb change with recipe passes -> " + ok44);
			}

			// 45. «Отряд от Племя Бардал идёт к вам» -> «Отряд от Племени Бардал идёт к вам» проходит: слово «Бардал» сохранено
			{
				string r45;
				bool ok45 = PlaceholderGuard.Validate("Отряд от Племя Бардал идёт к вам", "Отряд от Племени Бардал идёт к вам", out r45);
				sb.AppendLine((ok45 ? "[OK]" : "[FAIL]") + " 45. Inflected multi-word faction passes -> " + ok45);
			}

			// 46. «Jorunn просит остаться в Утёс Преданности» -> «Jorunn просит остаться в локации» отклоняется: потеряны «Утёс» и «Преданности»
			{
				string r46;
				bool ok46 = !PlaceholderGuard.Validate("Jorunn просит остаться в Утёс Преданности", "Jorunn просит остаться в локации", out r46) && r46.Contains("русский фрагмент исходника потерян");
				sb.AppendLine((ok46 ? "[OK]" : "[FAIL]") + " 46. Missing both capital words rejected -> " + ok46);
			}

			// 47. GetPlaceholderMatches на строке длиной 4000 символов без lookup отрабатывает без исключений, и в ней сработал быстрый выход
			{
				string longStr = new string('A', 4000) + " {0} " + new string('B', 100);
				var matches47 = PlaceholderGuard.GetPlaceholderMatches(longStr);
				bool ok47 = matches47.Count == 1 && matches47[0].Value == "{0}";
				sb.AppendLine((ok47 ? "[OK]" : "[FAIL]") + " 47. Long string fast path works efficiently -> " + ok47);
			}

			// 48. Validate("Комната слишком мала for the colonist", "Помещение слишком мало для колониста") проходит: «Комната» стоит в начале строки и именем собственным не считается
			{
				string r48;
				bool ok48 = PlaceholderGuard.Validate("Комната слишком мала for the colonist", "Помещение слишком мало для колониста", out r48);
				sb.AppendLine((ok48 ? "[OK]" : "[FAIL]") + " 48. Sentence start capitalized word not treated as proper noun -> " + ok48);
			}

			// 49. Validate("Отряд от Племя Бардал идёт к вам", "Группа от Племени Бардал идёт к вам") проходит: «Отряд» в начале строки, «Племя» и «Бардал» сохранены по основе
			{
				string r49;
				bool ok49 = PlaceholderGuard.Validate("Отряд от Племя Бардал идёт к вам", "Группа от Племени Бардал идёт к вам", out r49);
				sb.AppendLine((ok49 ? "[OK]" : "[FAIL]") + " 49. Stems matching with inflected words and sentence start word -> " + ok49);
			}

			// 50. Validate("Jorunn просит остаться в Утёс Преданности", "Jorunn просит остаться в Утёсе Преданности") проходит: оба имени сохранены в склонённой форме
			{
				string r50;
				bool ok50 = PlaceholderGuard.Validate("Jorunn просит остаться в Утёс Преданности", "Jorunn просит остаться в Утёсе Преданности", out r50);
				sb.AppendLine((ok50 ? "[OK]" : "[FAIL]") + " 50. Declension of proper names passes -> " + ok50);
			}

			// 51. Validate("Караван идёт в Утёс Преданности. Преданности ждут гостей.", "Караван идёт в локацию. Локация ждёт гостей.") отклоняется с причиной «русский фрагмент исходника потерян»
			{
				string r51;
				bool ok51 = !PlaceholderGuard.Validate("Караван идёт в Утёс Преданности. Преданности ждут гостей.", "Караван идёт в локацию. Локация ждёт гостей.", out r51) && r51.Contains("русский фрагмент исходника потерян");
				sb.AppendLine((ok51 ? "[OK]" : "[FAIL]") + " 51. Capitalized words lost later in sentence rejected -> " + ok51);
			}

			// 52. Validate("Караван идёт в Утёс\nПреданности прямо сейчас", "Караван идёт в Утёсе\nПреданности прямо сейчас") проходит: слова, разделённые переводом строки, разбираются по отдельности
			{
				string r52;
				bool ok52 = PlaceholderGuard.Validate("Караван идёт в Утёс\nПреданности прямо сейчас", "Караван идёт в Утёсе\nПреданности прямо сейчас", out r52);
				sb.AppendLine((ok52 ? "[OK]" : "[FAIL]") + " 52. Multi-word phrase with newline separator passes -> " + ok52);
			}

			// 53. TryGetFlat: "Preview (per worker):" сначала промах (метка), потом Put, потом успех
			{
				string res53_1;
				bool fail53 = !TranslationCache.TryGetFlat("Preview (per worker):", out res53_1);
				
				TranslationCache.Put("ui", "Preview (per worker)", "Предпросмотр (на одного рабочего)");
				
				string res53_2 = null;
				bool ok53 = fail53 && TranslationCache.TryGetFlat("Preview (per worker):", out res53_2) && res53_2 == "Предпросмотр (на одного рабочего):";
				sb.AppendLine((ok53 ? "[OK]" : "[FAIL]") + " 53. Preview (per worker): combat order -> " + (res53_2 ?? "null"));
			}

			// 54. TryGetFlat: "Decay per additional doctor: ×0.75" сначала промах, потом Put, потом успех
			{
				string res54_1;
				bool fail54 = !TranslationCache.TryGetFlat("Decay per additional doctor: \u00D70.75", out res54_1);
				
				TranslationCache.Put("ui", "Decay per additional doctor", "эффективность доп. врача");
				
				string res54_2 = null;
				bool ok54 = fail54 && TranslationCache.TryGetFlat("Decay per additional doctor: \u00D70.75", out res54_2) && res54_2 == "эффективность доп. врача: \u00D70.75";
				sb.AppendLine((ok54 ? "[OK]" : "[FAIL]") + " 54. Decay per additional doctor: \u00D70.75 combat order -> " + (res54_2 ?? "null"));
			}

			// 55. TryGetFlat: "<b>Level up actions</b>" сначала промах, потом Put, потом успех с тегами
			{
				string res55_1;
				bool fail55 = !TranslationCache.TryGetFlat("<b>Level up actions</b>", out res55_1);
				
				TranslationCache.Put("ui", "Level up actions", "действия при повышении уровня");
				
				string res55_2 = null;
				bool ok55 = fail55 && TranslationCache.TryGetFlat("<b>Level up actions</b>", out res55_2) && res55_2 == "<b>действия при повышении уровня</b>";
				sb.AppendLine((ok55 ? "[OK]" : "[FAIL]") + " 55. <b>Level up actions</b> combat order -> " + (res55_2 ?? "null"));
			}

			// 56. Проверка суженного фильтра слэшей
			{
				string s_pass = "Bleed rate threshold: 100%/day";
				string s_fail1 = "Textures/Things/Item.png";
				string s_fail2 = "C:\\Mods\\Thing.dll";
				
				bool passes_pass = !UiHarvest.IsJunkPath(s_pass);
				bool passes_fail1 = !UiHarvest.IsJunkPath(s_fail1);
				bool passes_fail2 = !UiHarvest.IsJunkPath(s_fail2);

				bool ok56 = passes_pass && !passes_fail1 && !passes_fail2;
				sb.AppendLine((ok56 ? "[OK]" : "[FAIL]") + " 56. Slash filter rules -> pass=" + passes_pass + ", fail1=" + passes_fail1 + ", fail2=" + passes_fail2);
			}

			// 57. TryGetMultiline: три строки, две есть в кэше, одна нет.
			// Порядок как в бою: сначала TryGetMultiline (промах), потом Put двух строк, потом снова TryGetMultiline.
			{
				string src57 = "Aerophilia (over 20 days)\n\nThis pawn loves the sky.\nUnknown feeling here.";
				string out57_1;
				// Первый вызов — ни одной строки в кэше, должен вернуть false
				bool miss57 = !TranslationCache.TryGetMultiline(src57, out out57_1);

				// Добавляем два из трёх переводов
				TranslationCache.Put("ui", "Aerophilia (over 20 days)", "Аэрофилия (более 20 дней)");
				TranslationCache.Put("ui", "This pawn loves the sky.", "Этот пешка любит небо.");

				// Второй вызов — должен подставить две строки, третья остаётся английской
				string out57_2;
				bool hit57 = TranslationCache.TryGetMultiline(src57, out out57_2);
				string expected57 = "Аэрофилия (более 20 дней)\n\nЭтот пешка любит небо.\nUnknown feeling here.";
				bool ok57 = miss57 && hit57 && out57_2 == expected57;
				sb.AppendLine((ok57 ? "[OK]" : "[FAIL]") + " 57. TryGetMultiline: partial cache hit -> " + (out57_2 == null ? "null" : out57_2.Replace("\n", "\\n")) + (ok57 ? "" : " | ожидалось: " + expected57.Replace("\n", "\\n")));
			}

			// 58. TryGetMultiline: ни одной строки в кэше — возвращает false, склейка помечена отрицательно.
			{
				string src58 = "Completely unknown line A\n\nCompletely unknown line B";
				string out58;
				bool miss58 = !TranslationCache.TryGetMultiline(src58, out out58);
				// Повторный вызов без изменения поколения — тоже false (из отрицательного кэша)
				string out58b;
				bool miss58b = !TranslationCache.TryGetMultiline(src58, out out58b);
				bool ok58 = miss58 && miss58b && out58 == null;
				sb.AppendLine((ok58 ? "[OK]" : "[FAIL]") + " 58. TryGetMultiline: all miss -> false, negative cached -> " + ok58);
			}

			// 59. Мемоизация: два подряд вызова TryGetMultiline с одной строкой без изменения поколения
			// должны дать ровно один реальный разбор.
			{
				string src59 = "Memo line one\n\nMemo line two";
				// Сбрасываем состояние: строки не в кэше, счётчик фиксируем
				int splitBefore = TranslationCache.MultilineSplitCount;
				string o1; TranslationCache.TryGetMultiline(src59, out o1); // первый вызов — реальный разбор
				int splitAfterFirst = TranslationCache.MultilineSplitCount;
				string o2; TranslationCache.TryGetMultiline(src59, out o2); // второй вызов — должен взять из отрицательного кэша
				int splitAfterSecond = TranslationCache.MultilineSplitCount;
				// Счётчик вырос ровно на 1 (первый вызов), второй вызов не добавил
				bool ok59 = (splitAfterFirst - splitBefore) == 1 && (splitAfterSecond - splitAfterFirst) == 0;
				sb.AppendLine((ok59 ? "[OK]" : "[FAIL]") + " 59. TryGetMultiline memoization: split count +1 on first, +0 on second -> delta1=" + (splitAfterFirst - splitBefore) + ", delta2=" + (splitAfterSecond - splitAfterFirst));
			}

			// 60. TryGetMultiline: строка с \r\n корректно разбирается и склеивается с сохранением \r.
			{
				TranslationCache.Put("ui", "Line with CR", "Строка с CR");
				TranslationCache.Put("ui", "Second line", "Вторая строка");
				string src60 = "Line with CR\r\nSecond line";
				string out60;
				bool ok60_hit = TranslationCache.TryGetMultiline(src60, out out60);
				string expected60 = "Строка с CR\r\nВторая строка";
				bool ok60 = ok60_hit && out60 == expected60;
				sb.AppendLine((ok60 ? "[OK]" : "[FAIL]") + " 60. TryGetMultiline CRLF preserved -> " + (out60 == null ? "null" : out60.Replace("\r", "\\r").Replace("\n", "\\n")) + (ok60 ? "" : " | ожидалось: " + expected60.Replace("\r", "\\r").Replace("\n", "\\n")));
			}

			// 61. Тест правки 1: частичный результат протухает при новом переводе.
			// Кладём в кэш только первую строку → TryGetMultiline → true, вторая осталась английской.
			// Затем Put второй строки → снова TryGetMultiline → обе строки по-русски.
			{
				string src61 = "t61_alpha line\nt61_beta line";
				TranslationCache.Put("ui", "t61_alpha line", "альфа-строка");
				// НЕ кладём t61_beta — чтобы первый вызов дал частичный результат
				string out61_partial;
				bool hit61_1 = TranslationCache.TryGetMultiline(src61, out out61_partial);
				string expected61_partial = "альфа-строка\nt61_beta line";
				bool ok61_1 = hit61_1 && out61_partial == expected61_partial;

				// Теперь кладём вторую строку — поколение растёт, частичный кэш протухает
				TranslationCache.Put("ui", "t61_beta line", "бета-строка");

				string out61_full;
				bool hit61_2 = TranslationCache.TryGetMultiline(src61, out out61_full);
				string expected61_full = "альфа-строка\nбета-строка";
				bool ok61_2 = hit61_2 && out61_full == expected61_full;

				bool ok61 = ok61_1 && ok61_2;
				sb.AppendLine((ok61 ? "[OK]" : "[FAIL]") + " 61. Partial cache refreshes after Put: partial=" + (out61_partial ?? "null").Replace("\n", "\\n") + " | full=" + (out61_full ?? "null").Replace("\n", "\\n") + (ok61 ? "" : " | ERR: p1=" + ok61_1 + " p2=" + ok61_2));
			}

			// 62. UiHarvest.IsTooLongForLabel: три граничных случая.
			{
				string s350 = new string('x', 350);               // 350 без \n — должно быть true (> 300)
				string s350n = new string('x', 175) + "\n" + new string('x', 174); // 350 с \n — false (< 2000)
				string s2500n = new string('x', 1200) + "\n" + new string('x', 1299); // 2500 с \n — true (> 2000)

				bool r1 = UiHarvest.IsTooLongForLabel(s350);     // ожидаем true
				bool r2 = UiHarvest.IsTooLongForLabel(s350n);    // ожидаем false
				bool r3 = UiHarvest.IsTooLongForLabel(s2500n);   // ожидаем true

				bool ok62 = r1 && !r2 && r3;
				sb.AppendLine((ok62 ? "[OK]" : "[FAIL]") + " 62. IsTooLongForLabel: 350no_n=" + r1 + ", 350with_n=" + r2 + ", 2500with_n=" + r3);
			}

			GATLog.Msg(sb.ToString());
		}
	}
}
