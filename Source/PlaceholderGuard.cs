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

			string clean = Ph.Replace(s, " ");
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
			foreach (Match m in Ph.Matches(s))
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

		/// <summary>Стоит ли вообще отправлять строку на перевод. Исходный язык любой.</summary>
		public static bool ShouldTranslate(string src)
		{
			if (string.IsNullOrEmpty(src)) return false;
			if (src.Length > 4000) return false;                 // аномалия, не текст интерфейса
			if (src.Contains("->")) return false;                // синтаксис правил грамматики RimWorld

			int cyr, lat, cjk, other;
			CountScripts(src, out cyr, out lat, out cjk, out other);

			int foreign = lat + cjk + other;
			if (foreign == 0) return false;                      // цифры, символы, defName без букв
			if (cyr > 0 && cyr >= foreign) return false;         // строка уже преимущественно русская

			// Иероглифам и слоговому письму пробелы не нужны: 剑 — это уже слово.
			if (cjk > 0 || other > 0) return true;

			if (lat < 2) return false;                           // одна буква — не текст
			if (src.IndexOf(' ') < 0 && src.Length < 3) return false;
			return true;
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

			var matches = Ph.Matches(src);
			if (matches.Count == 0) return src;

			var sb = new StringBuilder(src.Length);
			int idx = 1;
			int prev = 0;
			foreach (Match m in matches)
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
