using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GlobalAutoTranslator
{
	/// <summary>
	/// Минимальный JSON-хелпер без внешних зависимостей.
	/// RimWorld не поставляет Newtonsoft.Json, а тащить его в Assemblies рискованно
	/// (конфликты версий с другими модами). Формы JSON у нас строго контролируемые,
	/// поэтому хватает кодировщика строк + выдёргивания одного поля + разбора плоского объекта.
	/// </summary>
	public static class MiniJson
	{
		/// <summary>Экранирует строку для вставки в JSON (без внешних кавычек).</summary>
		public static string Escape(string s)
		{
			if (string.IsNullOrEmpty(s)) return string.Empty;
			var sb = new StringBuilder(s.Length + 16);
			foreach (char c in s)
			{
				switch (c)
				{
					case '"': sb.Append("\\\""); break;
					case '\\': sb.Append("\\\\"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					case '\b': sb.Append("\\b"); break;
					case '\f': sb.Append("\\f"); break;
					default:
						if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
						else sb.Append(c);
						break;
				}
			}
			return sb.ToString();
		}

		/// <summary>Раскодирует JSON-строку (содержимое между кавычками).</summary>
		public static string Unescape(string s)
		{
			if (string.IsNullOrEmpty(s)) return string.Empty;
			var sb = new StringBuilder(s.Length);
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c != '\\') { sb.Append(c); continue; }
				if (++i >= s.Length) break;
				switch (s[i])
				{
					case '"': sb.Append('"'); break;
					case '\\': sb.Append('\\'); break;
					case '/': sb.Append('/'); break;
					case 'n': sb.Append('\n'); break;
					case 'r': sb.Append('\r'); break;
					case 't': sb.Append('\t'); break;
					case 'b': sb.Append('\b'); break;
					case 'f': sb.Append('\f'); break;
					case 'u':
						if (i + 4 < s.Length)
						{
							string hex = s.Substring(i + 1, 4);
							int code;
							if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
							{
								sb.Append((char)code);
								i += 4;
							}
						}
						break;
					default: sb.Append(s[i]); break;
				}
			}
			return sb.ToString();
		}

		/// <summary>
		/// Достаёт значение первого строкового поля с указанным именем, корректно
		/// пропуская экранированные кавычки. Возвращает уже раскодированную строку.
		/// </summary>
		public static string ExtractStringField(string json, string fieldName)
		{
			if (string.IsNullOrEmpty(json)) return null;
			string needle = "\"" + fieldName + "\"";
			int at = 0;
			while (true)
			{
				at = json.IndexOf(needle, at, StringComparison.Ordinal);
				if (at < 0) return null;
				int i = at + needle.Length;
				while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
				if (i >= json.Length || json[i] != ':') { at += needle.Length; continue; }
				i++;
				while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
				if (i >= json.Length) return null;
				if (json[i] != '"') { at += needle.Length; continue; } // null или объект — не наш случай
				i++;
				int start = i;
				var sb = new StringBuilder();
				while (i < json.Length)
				{
					if (json[i] == '\\') { i += 2; continue; }
					if (json[i] == '"') break;
					i++;
				}
				if (i > json.Length) return null;
				return Unescape(json.Substring(start, i - start));
			}
		}

		/// <summary>
		/// Разбирает плоский объект вида {"1":"a","2":"b"}. Вложенность не поддерживается
		/// намеренно — модель обязана отдавать именно плоскую карту.
		/// </summary>
		public static Dictionary<string, string> ParseFlatObject(string json)
		{
			var result = new Dictionary<string, string>();
			if (string.IsNullOrEmpty(json)) return result;

			int open = json.IndexOf('{');
			int close = json.LastIndexOf('}');
			if (open < 0 || close <= open) return result;
			string body = json.Substring(open + 1, close - open - 1);

			int i = 0;
			while (i < body.Length)
			{
				while (i < body.Length && body[i] != '"') i++;
				if (i >= body.Length) break;
				i++;
				string key = ReadRawString(body, ref i);
				while (i < body.Length && body[i] != ':') i++;
				if (i >= body.Length) break;
				i++;
				while (i < body.Length && body[i] != '"') i++;
				if (i >= body.Length) break;
				i++;
				string val = ReadRawString(body, ref i);
				if (key != null) result[Unescape(key)] = Unescape(val ?? string.Empty);
			}
			return result;
		}

		private static string ReadRawString(string s, ref int i)
		{
			int start = i;
			while (i < s.Length)
			{
				if (s[i] == '\\') { i += 2; continue; }
				if (s[i] == '"') break;
				i++;
			}
			if (i > s.Length) return null;
			string raw = s.Substring(start, Math.Min(i, s.Length) - start);
			i++; // пропускаем закрывающую кавычку
			return raw;
		}
	}
}
