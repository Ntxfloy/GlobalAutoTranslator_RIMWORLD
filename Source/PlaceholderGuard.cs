using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GlobalAutoTranslator
{
	/// <summary>
	/// Защита плейсхолдеров. Самая важная часть мода: битая строка вида
	/// "повержен(а): {0}" без {0} валит форматтер RimWorld и ломает письма/события.
	/// Любой перевод, не прошедший Validate, ОБЯЗАН быть отброшен.
	/// </summary>
	public static class PlaceholderGuard
	{
		// [[ count ]], {PAWN_labelShort}, {0}, <color=#FF0000>, </color>, [tag], \n
		public static readonly Regex Ph = new Regex(
			@"\[\[[^\]]*\]\]|\{[^{}]*\}|<[^<>]+>|\[[^\[\]]+\]|\\n",
			RegexOptions.Compiled);

		public struct PlaceholderMatch
		{
			public int Index;
			public int Length;
			public string Value;
		}

		public static List<PlaceholderMatch> GetPlaceholderMatches(string s)
		{
			var list = new List<PlaceholderMatch>();
			if (string.IsNullOrEmpty(s)) return list;

			int lookupIdx = s.IndexOf("{lookup:", StringComparison.OrdinalIgnoreCase);
			if (lookupIdx < 0)
			{
				foreach (Match m in Ph.Matches(s))
				{
					list.Add(new PlaceholderMatch { Index = m.Index, Length = m.Length, Value = m.Value });
				}
				return list;
			}

			int i = 0;
			while (i < s.Length)
			{
				int nextLookup = s.IndexOf("{lookup:", i, StringComparison.OrdinalIgnoreCase);

				// Распознаём конструкцию {lookup: ...} со сбалансированным подсчётом скобок
				if (nextLookup == i)
				{
					int braceCount = 1;
					int j = i + 8;
					while (j < s.Length && braceCount > 0)
					{
						if (s[j] == '{') braceCount++;
						else if (s[j] == '}') braceCount--;
						j++;
					}
					if (braceCount == 0)
					{
						int len = j - i;
						list.Add(new PlaceholderMatch { Index = i, Length = len, Value = s.Substring(i, len) });
						i = j;
						continue;
					}
					else
					{
						// Незакрытая скобка — конструкцию {lookup: не считаем плейсхолдером и идём дальше
						i += 8;
						continue;
					}
				}

				Match m = Ph.Match(s, i);
				if (m.Success && (nextLookup < 0 || m.Index <= nextLookup))
				{
					list.Add(new PlaceholderMatch { Index = m.Index, Length = m.Length, Value = m.Value });
					i = m.Index + m.Length;
					continue;
				}

				if (nextLookup >= 0)
				{
					i = nextLookup;
				}
				else if (m.Success)
				{
					i = m.Index;
				}
				else
				{
					break;
				}
			}
			return list;
		}

		public enum ScriptKind { None, Cyrillic, Latin, Cjk, Other }

		/// <summary>К какой письменности относится один символ.</summary>
		private static ScriptKind ClassifyChar(char c)
		{
			if (c == '\u00D7' || c == '\u00F7') return ScriptKind.None;   // знаки × и ÷, не буквы

			if (c >= '\u0400' && c <= '\u052F') return ScriptKind.Cyrillic;

			if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return ScriptKind.Latin;
			if (c >= '\u00C0' && c <= '\u024F') return ScriptKind.Latin;   // ä, é, ł, ş, ñ

			if (c >= '\u3040' && c <= '\u30FF') return ScriptKind.Cjk;     // хирагана и катакана
			if (c >= '\u3400' && c <= '\u4DBF') return ScriptKind.Cjk;     // ханьцзы, расширение A
			if (c >= '\u4E00' && c <= '\u9FFF') return ScriptKind.Cjk;     // основные ханьцзы и кандзи
			if (c >= '\uF900' && c <= '\uFAFF') return ScriptKind.Cjk;     // ханьцзы совместимости
			if (c >= '\uAC00' && c <= '\uD7AF') return ScriptKind.Cjk;     // хангыль, слоги
			if (c >= '\u1100' && c <= '\u11FF') return ScriptKind.Cjk;     // хангыль, чамо
			if (c >= '\u3130' && c <= '\u318F') return ScriptKind.Cjk;     // чамо совместимости

			if (c >= '\u0370' && c <= '\u03FF') return ScriptKind.Other;   // греческий
			if (c >= '\u0590' && c <= '\u05FF') return ScriptKind.Other;   // иврит
			if (c >= '\u0600' && c <= '\u06FF') return ScriptKind.Other;   // арабский
			if (c >= '\u0750' && c <= '\u077F') return ScriptKind.Other;   // арабский, дополнение
			if (c >= '\u0900' && c <= '\u097F') return ScriptKind.Other;   // деванагари
			if (c >= '\u0E00' && c <= '\u0E7F') return ScriptKind.Other;   // тайский

			return ScriptKind.None;
		}

		/// <summary>
		/// Считает буквы по письменностям. Плейсхолдеры и теги вырезаются ПЕРЕД подсчётом:
		/// иначе {PAWN_labelShort} даст пятнадцать латинских букв, и чисто китайская строка
		/// с одним плейсхолдером определится как латиница.
		/// </summary>
		public static void CountScripts(string s, out int cyr, out int lat, out int cjk, out int other)
		{
			cyr = 0; lat = 0; cjk = 0; other = 0;
			if (string.IsNullOrEmpty(s)) return;

			string clean = StripTagsAndPlaceholders(s);
			for (int i = 0; i < clean.Length; i++)
			{
				switch (ClassifyChar(clean[i]))
				{
					case ScriptKind.Cyrillic: cyr++;   break;
					case ScriptKind.Latin:    lat++;   break;
					case ScriptKind.Cjk:      cjk++;   break;
					case ScriptKind.Other:    other++; break;
				}
			}
		}

		/// <summary>Доминирующая письменность строки.</summary>
		public static ScriptKind DetectScript(string s)
		{
			int cyr, lat, cjk, other;
			CountScripts(s, out cyr, out lat, out cjk, out other);

			// Один иероглиф несёт примерно столько же смысла, сколько три латинские буквы,
			// поэтому сравниваем не сырые количества, а с поправкой.
			if (cjk > 0 && cjk * 3 >= lat) return ScriptKind.Cjk;

			int max = Math.Max(cyr, Math.Max(lat, Math.Max(cjk, other)));
			if (max == 0) return ScriptKind.None;
			if (max == cyr) return ScriptKind.Cyrillic;
			if (max == lat) return ScriptKind.Latin;
			if (max == cjk) return ScriptKind.Cjk;
			return ScriptKind.Other;
		}

		public static List<string> Placeholders(string s)
		{
			var list = new List<string>();
			if (string.IsNullOrEmpty(s)) return list;
			foreach (var m in GetPlaceholderMatches(s))
			{
				string val = m.Value;
				if (val.StartsWith("{") && val.EndsWith("}") && val.Contains("?"))
				{
					int q = val.IndexOf('?');
					val = val.Substring(0, q + 1); // "{PREDATOR_gender ?"
				}
				list.Add(val);
			}
			list.Sort(StringComparer.Ordinal);
			return list;
		}

		private static readonly HashSet<string> LatinStopList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"kg", "cm", "mm", "ms", "px", "hp", "mp", "xp", "fps", "tps", "hz", "ml", "kb", "mb", "gb", "id", "ui"
		};

		/// <summary>
		/// Единый метод проверки: нуждается ли строка в переводе на русский язык.
		/// Поддерживает чистые и смешанные строки (с исходными именами/русскими фразами).
		/// </summary>
		public static bool NeedsTranslation(string text)
		{
			if (string.IsNullOrEmpty(text)) return false;
			if (text.Length > 4000) return false;
			if (text.Contains("->")) return false; // грамматика RimWorld (RulePackDef)

			string clean = StripTagsAndPlaceholders(text);

			int cyr, lat, cjk, other;
			CountScripts(clean, out cyr, out lat, out cjk, out other);

			int foreign = lat + cjk + other;
			if (foreign == 0) return false; // нет латиницы, CJK или других букв

			// Чистый зарубежный текст (без кириллицы)
			if (cyr == 0)
			{
				if (cjk > 0 || other > 0) return true;
				if (lat < 2) return false;
				if (clean.IndexOf(' ') < 0 && clean.Length < 3) return false;
				return true;
			}

			// Смешанный текст (есть кириллица И зарубежное письмо)
			if (cjk >= 2 || other >= 4) return true;

			// Проверка латиницы в смешанном тексте через ClassifyChar (с поддержкой расширенной латиницы é, ä, ñ, ł, ş)
			int meaningfulLatinWords = 0;
			int lowercaseLatinWords = 0;
			int latinLetters = 0;
			int currentWordLen = 0;
			int wordStartIdx = 0;
			bool currentWordStartsLower = false;

			for (int i = 0; i < clean.Length; i++)
			{
				char c = clean[i];
				if (ClassifyChar(c) == ScriptKind.Latin)
				{
					if (currentWordLen == 0)
					{
						wordStartIdx = i;
						currentWordStartsLower = char.IsLower(c);
					}
					currentWordLen++;
				}
				else
				{
					if (currentWordLen > 0)
					{
						string word = clean.Substring(wordStartIdx, currentWordLen);
						if (currentWordLen >= 3 && !LatinStopList.Contains(word))
						{
							latinLetters += currentWordLen;
							meaningfulLatinWords++;
							if (currentWordStartsLower) lowercaseLatinWords++;
						}
						currentWordLen = 0;
					}
				}
			}
			if (currentWordLen > 0)
			{
				string word = clean.Substring(wordStartIdx, currentWordLen);
				if (currentWordLen >= 3 && !LatinStopList.Contains(word))
				{
					latinLetters += currentWordLen;
					meaningfulLatinWords++;
					if (currentWordStartsLower) lowercaseLatinWords++;
				}
			}

			// Если в смешанной строке есть хотя бы 1 нарицательное латинское слово со строчной буквы (например, "car", "wiring", "block") -> переводить!
			if (lowercaseLatinWords >= 1)
				return true;

			// Если латинских слов >= 3 и букв >= 12 (например, длинный английский фрагмент) -> переводить!
			if (meaningfulLatinWords >= 3 && latinLetters >= 12)
				return true;

			return false; // По умолчанию считаем строку уже русской (например "Мод Combat Extended включён", "Здоровье HP", "Вес: 2.5 kg")
		}

		/// <summary>Алиас для единообразия.</summary>
		public static bool ShouldTranslate(string src)
		{
			return NeedsTranslation(src);
		}

		private static readonly Regex TagRegex = new Regex(
			@"⟦[^⟧]*⟧|<[^>]*>|\{[^{}]*\}|\[\[[^\]]*\]\]|\[[^\]]*\]",
			RegexOptions.Compiled);

		public static string StripTagsAndPlaceholders(string s)
		{
			if (string.IsNullOrEmpty(s)) return string.Empty;
			if (s.IndexOf("{lookup:", StringComparison.OrdinalIgnoreCase) < 0)
			{
				return TagRegex.Replace(s, " ");
			}
			var matches = GetPlaceholderMatches(s);
			if (matches.Count == 0) return TagRegex.Replace(s, " ");

			var sb = new StringBuilder(s.Length);
			int prev = 0;
			foreach (var m in matches)
			{
				sb.Append(s, prev, m.Index - prev).Append(' ');
				prev = m.Index + m.Length;
			}
			sb.Append(s, prev, s.Length - prev);
			return TagRegex.Replace(sb.ToString(), " ");
		}

		private static readonly HashSet<string> CommonServiceWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Изготавливает", "Делает", "Создает", "Создаёт", "Постройка", "Любые", "Патроны", "Требуется", "Каждый", "Каждая", "Каждое", "Снаряды", "Рецепт", "Сборка",
			"для", "в", "во", "и", "с", "со", "на", "от", "из", "по", "к", "ко", "у", "о", "об", "за", "до", "при", "над", "под", "а", "но", "или", "не", "же", "ли", "что", "как", "это"
		};

		private static bool IsAtStartOfSentence(string s, int index)
		{
			if (index == 0) return true;
			int p = index - 1;
			while (p >= 0 && char.IsWhiteSpace(s[p]))
			{
				p--;
			}
			if (p < 0) return true;
			char c = s[p];
			return c == '.' || c == '!' || c == '?' || c == ':' || c == ';' || c == '\r' || c == '\n';
		}

		public static Dictionary<string, int> ExtractCyrillicFragments(string s)
		{
			var map = new Dictionary<string, int>(StringComparer.Ordinal);
			if (string.IsNullOrEmpty(s)) return map;

			// 1. Поиск многословных кириллических фраз (минимум 2 слова подряд)
			var multiMatches = Regex.Matches(s, @"[\u0400-\u052F]+(?:\s+[\u0400-\u052F]+)+");
			foreach (Match m in multiMatches)
			{
				string val = m.Value;
				int wordStartIdx = m.Index;
				string[] words = val.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

				foreach (string w in words)
				{
					int wIdx = s.IndexOf(w, wordStartIdx, StringComparison.Ordinal);
					if (wIdx >= 0)
					{
						wordStartIdx = wIdx + w.Length;

						if (w.Length >= 4 && char.IsUpper(w[0]) && !CommonServiceWords.Contains(w))
						{
							if (!IsAtStartOfSentence(s, wIdx))
							{
								int count;
								map.TryGetValue(w, out count);
								map[w] = count + 1;
							}
						}
					}
				}
			}

			// 2. Одиночные имена собственные: одно слово >= 6 символов, начинающееся с заглавной буквы, не входящее в служебные слова
			var singleMatches = Regex.Matches(s, @"[\u0400-\u052F]{6,}");
			foreach (Match m in singleMatches)
			{
				string val = m.Value;
				if (char.IsUpper(val[0]) && !CommonServiceWords.Contains(val))
				{
					if (!IsAtStartOfSentence(s, m.Index))
					{
						if (!map.ContainsKey(val))
						{
							int count;
							map.TryGetValue(val, out count);
							map[val] = count + 1;
						}
					}
				}
			}

			return map;
		}

		public static int CountOccurrencesOrdinal(string text, string fragment)
		{
			if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fragment)) return 0;
			int count = 0;
			int idx = 0;
			while ((idx = text.IndexOf(fragment, idx, StringComparison.Ordinal)) != -1)
			{
				count++;
				idx += fragment.Length;
			}
			return count;
		}

		/// <summary>
		/// Итоговая проверка перевода. Возвращает false, если строку нужно выбросить.
		/// </summary>
		public static bool Validate(string src, string dst, out string reason)
		{
			reason = null;

			if (string.IsNullOrWhiteSpace(dst)) { reason = "пусто"; return false; }

			int srcArrowCount = CountOccurrences(src, "->");
			if (srcArrowCount > 0)
			{
				if (CountOccurrences(dst, "->") != srcArrowCount)
				{
					reason = "утерян или изменен синтаксис грамматики (->)";
					return false;
				}
			}

			var a = Placeholders(src);
			var b = Placeholders(dst);
			if (!a.SequenceEqual(b, StringComparer.Ordinal))
			{
				reason = "плейсхолдеры не совпадают: [" + string.Join(", ", a) + "] -> [" + string.Join(", ", b) + "]";
				return false;
			}

			// Проверка сохранения русских фрагментов по основе слова (Rule 6)
			var srcCyrFrags = ExtractCyrillicFragments(src);
			if (srcCyrFrags.Count > 0)
			{
				foreach (var kv in srcCyrFrags)
				{
					string frag = kv.Key;
					int srcCount = kv.Value;

					string searchPattern = frag;
					if (frag.Length >= 5)
					{
						searchPattern = frag.Substring(0, frag.Length - 2);
					}

					int dstCount = CountOccurrencesOrdinal(dst, searchPattern);
					if (dstCount < srcCount)
					{
						reason = "русский фрагмент исходника потерян";
						return false;
					}
				}
			}

			// Иероглифическая строка при переводе на русский разрастается в разы,
			// поэтому предел длины зависит от исходной письменности.
			ScriptKind srcScript = DetectScript(src);
			double maxRatio = (srcScript == ScriptKind.Cjk) ? 9.0 : 3.5;
			int maxExtra   = (srcScript == ScriptKind.Cjk) ? 60  : 30;
			if (dst.Length > src.Length * maxRatio + maxExtra)
			{
				reason = "слишком длинно, модель начала объяснять";
				return false;
			}

			if (dst.Contains("```")) { reason = "markdown в ответе"; return false; }
			if (dst.StartsWith("Перевод:", StringComparison.Ordinal)) { reason = "префикс-болтовня"; return false; }
			if (dst.Contains(MarkerLeft) || dst.Contains(MarkerRight)) { reason = "в ответе остались нераспознанные маркеры"; return false; }

			int dCyr, dLat, dCjk, dOther;
			CountScripts(dst, out dCyr, out dLat, out dCjk, out dOther);

			// Ответ обязан быть на русском. Нет кириллицы, но буквы есть — модель вернула оригинал.
			if (dCyr == 0 && (dLat + dCjk + dOther) > 0)
			{
				reason = "в ответе нет кириллицы, строка не переведена";
				return false;
			}

			// Остатки иероглифики в русском тексте — почти всегда недоперевод.
			if (dCjk > 0)
			{
				reason = "в ответе остались иероглифы";
				return false;
			}

			// Незакрытые теги форматирования
			int openTags = CountOccurrences(dst, "<color");
			int closeTags = CountOccurrences(dst, "</color>");
			if (openTags != closeTags) { reason = "незакрытый тег color"; return false; }

			return true;
		}

		/// <summary>
		/// Приводит полноширинные знаки к ASCII: ｛０｝ -> {0}, （ -> (, идеографический пробел -> обычный.
		/// Вызывать ОБЯЗАТЕЛЬНО до Validate, иначе корректный перевод улетит в карантин
		/// из-за несовпадения плейсхолдеров.
		/// </summary>
		public static string NormalizeFullwidth(string s)
		{
			if (string.IsNullOrEmpty(s)) return s;

			bool need = false;
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c == '\u3000' || (c >= '\uFF01' && c <= '\uFF5E')) { need = true; break; }
			}
			if (!need) return s;

			var sb = new StringBuilder(s.Length);
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c == '\u3000') { sb.Append(' '); continue; }
				if (c >= '\uFF01' && c <= '\uFF5E') { sb.Append((char)(c - 0xFEE0)); continue; }
				sb.Append(c);
			}
			return sb.ToString();
		}

		private static int CountOccurrences(string s, string sub)
		{
			int n = 0, at = 0;
			while ((at = s.IndexOf(sub, at, StringComparison.OrdinalIgnoreCase)) >= 0) { n++; at += sub.Length; }
			return n;
		}

		// ────────────────────────────────────────────────────────────────
		//  Числовые маркеры ⟦N⟧  (U+27E6 / U+27E7)
		//  Каждое вхождение плейсхолдера (включая повторы) получает свой номер.
		// ────────────────────────────────────────────────────────────────

		public const string MarkerLeft  = "\u27E6";
		public const string MarkerRight = "\u27E7";

		/// <summary>
		/// Заменяет каждый плейсхолдер в строке на ⟦1⟧, ⟦2⟧, … нумеруя каждое
		/// вхождение отдельно (включая повторы одного плейсхолдера).
		/// ИСКЛЮЧЕНИЕ: Гендерные конструкции {X_gender ? a : b} НЕ маскируются —
		/// они отправляются в модель в открытом виде для перевода вариантов внутри (Правило 3).
		/// Возвращает замаскированную строку и словарь номер→оригинальный плейсхолдер.
		/// Если плейсхолдеров нет, map будет пустым, а строка — без изменений.
		/// </summary>
		public static string MaskPlaceholders(string src, out Dictionary<int, string> map)
		{
			map = new Dictionary<int, string>();
			if (string.IsNullOrEmpty(src)) return src;

			var matches = GetPlaceholderMatches(src);
			if (matches.Count == 0) return src;

			var sb = new StringBuilder(src.Length);
			int idx = 1;
			int prev = 0;
			foreach (var m in matches)
			{
				string val = m.Value;
				// Гендерные конструкции вида {PAWN_gender ? attacked : attacked} НЕ маскируем
				if (val.StartsWith("{") && val.EndsWith("}") && val.Contains("?"))
				{
					sb.Append(src, prev, m.Index + m.Length - prev);
					prev = m.Index + m.Length;
					continue;
				}

				sb.Append(src, prev, m.Index - prev);
				sb.Append(MarkerLeft).Append(idx).Append(MarkerRight);
				map[idx] = val;
				idx++;
				prev = m.Index + m.Length;
			}
			sb.Append(src, prev, src.Length - prev);
			return sb.ToString();
		}

		/// <summary>
		/// Подставляет обратно оригинальные плейсхолдеры по номерам маркеров.
		/// Порядок в переводе может отличаться от исходника — подставляет строго по номеру.
		/// </summary>
		public static string UnmaskPlaceholders(string translated, Dictionary<int, string> map)
		{
			if (string.IsNullOrEmpty(translated) || map == null || map.Count == 0) return translated;

			var sb = new StringBuilder(translated.Length + 64);
			int i = 0;
			while (i < translated.Length)
			{
				int start = translated.IndexOf(MarkerLeft, i, StringComparison.Ordinal);
				if (start < 0) { sb.Append(translated, i, translated.Length - i); break; }

				sb.Append(translated, i, start - i);
				int end = translated.IndexOf(MarkerRight, start + MarkerLeft.Length, StringComparison.Ordinal);
				if (end < 0) { sb.Append(translated, start, translated.Length - start); break; }

				string numStr = translated.Substring(start + MarkerLeft.Length, end - start - MarkerLeft.Length);
				int num;
				string orig;
				if (int.TryParse(numStr, out num) && map.TryGetValue(num, out orig))
					sb.Append(orig);
				else
					sb.Append(translated, start, end + MarkerRight.Length - start); // неизвестный маркер — оставляем как есть

				i = end + MarkerRight.Length;
			}
			return sb.ToString();
		}

		/// <summary>
		/// Проверяет, что набор маркеров ⟦N⟧ в ответе совпадает с ожидаемым.
		/// </summary>
		public static bool ValidateMarkers(string masked, Dictionary<int, string> map, out string reason)
		{
			reason = null;
			if (map == null || map.Count == 0) return true;

			var found = new HashSet<int>();
			int i = 0;
			while (i < masked.Length)
			{
				int start = masked.IndexOf(MarkerLeft, i, StringComparison.Ordinal);
				if (start < 0) break;
				int end = masked.IndexOf(MarkerRight, start + MarkerLeft.Length, StringComparison.Ordinal);
				if (end < 0) break;
				string numStr = masked.Substring(start + MarkerLeft.Length, end - start - MarkerLeft.Length);
				int num;
				if (int.TryParse(numStr, out num)) found.Add(num);
				i = end + MarkerRight.Length;
			}

			var missing = new List<int>();
			var extra   = new List<int>();
			foreach (int key in map.Keys)
				if (!found.Contains(key)) missing.Add(key);
			foreach (int key in found)
				if (!map.ContainsKey(key)) extra.Add(key);

			if (missing.Count == 0 && extra.Count == 0) return true;

			var sb = new StringBuilder("маркеры не совпадают:");
			if (missing.Count > 0) { sb.Append(" утеряны "); missing.Sort(); foreach (int m in missing) sb.Append(MarkerLeft).Append(m).Append(MarkerRight).Append(' '); }
			if (extra.Count > 0)   { sb.Append(" лишние ");  extra.Sort();   foreach (int e in extra)   sb.Append(MarkerLeft).Append(e).Append(MarkerRight).Append(' '); }
			reason = sb.ToString();
			return false;
		}

		/// <summary>
		/// Строит корректирующую строку для повторной попытки:
		/// перечисляет обязательные маркеры, которые модель должна сохранить.
		/// </summary>
		public static string BuildRetryHint(Dictionary<int, string> map)
		{
			if (map == null || map.Count == 0) return "";
			var sb = new StringBuilder("ОБЯЗАТЕЛЬНЫЕ маркеры (перенеси все в перевод без изменений): ");
			foreach (var kv in map)
				sb.Append(MarkerLeft).Append(kv.Key).Append(MarkerRight).Append(' ');
			return sb.ToString().TrimEnd();
		}
	}
}
