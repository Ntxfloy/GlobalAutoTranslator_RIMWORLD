using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using Verse;

namespace GlobalAutoTranslator
{
	/// <summary>
	/// Кэш переводов. Сознательно НЕ SQLite: System.Data.SQLite требует нативную dll,
	/// которая в Mono-сборке RimWorld часто не грузится и валит игру на старте.
	/// Вместо этого — шардированные TSV-файлы: быстро, без зависимостей, легко читается глазом.
	///
	/// Путь: %APPDATA%\..\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\GlobalTranslator\cache\
	/// </summary>
	public static class TranslationCache
	{
		private static readonly ConcurrentDictionary<string, string> map =
			new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

		/// <summary>Исходный текст по ключу. Нужен для экспорта и для плоского индекса.</summary>
		private static readonly ConcurrentDictionary<string, string> sources =
			new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

		/// <summary>Плоский индекс "исходная строка -> перевод" для слоя 3.
		/// Без MD5 и без контекста: в Widgets.Label нельзя тратить ни одной аллокации.</summary>
		private static readonly ConcurrentDictionary<string, string> flat =
			new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

		private static readonly ConcurrentDictionary<string, byte> flatNoFallback =
			new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

		public class TemplateRecord
		{
			public Regex Pattern;
			public string TargetTemplate;
			public int MinLength;
			public string Anchor;
		}

		private static readonly ConcurrentBag<TemplateRecord> templates = new ConcurrentBag<TemplateRecord>();
		private static readonly ConcurrentDictionary<string, byte> templateKeys = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
		private static readonly ConcurrentDictionary<string, int> templateNoMatch = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
		private static int templateCount;
		private static bool templateKeysLimitLogged;
		private static bool templateCountLimitLogged;

		private static readonly ConcurrentDictionary<string, byte> dirtyShards =
			new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

		private static readonly ConcurrentDictionary<string, byte> permanentFailed =
			new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
		private static bool permanentFailedLimitLogged;
		private const int MaxPermanentFailed = 20000;

		private static readonly object ioLock = new object();

		public static int Count { get { return map.Count; } }
		public static int PermanentFailedCount { get { return permanentFailed.Count; } }

		private static string ExpectedFailedHeader
		{
			get
			{
				string modelName = GATMod.Settings != null ? GATMod.Settings.model : "default";
				return "# v" + Prompt.PromptVersion + "\t" + modelName;
			}
		}

		public static bool IsPermanentFailed(string key)
		{
			return permanentFailed.ContainsKey(key);
		}

		public static void AddPermanentFailed(string key, string source)
		{
			if (string.IsNullOrEmpty(key)) return;
			if (permanentFailed.Count >= MaxPermanentFailed)
			{
				if (!permanentFailedLimitLogged)
				{
					permanentFailedLimitLogged = true;
					GATLog.Warn("Достигнут лимит " + MaxPermanentFailed + " записей в failed.tsv. Запись приостановлена.");
				}
				return;
			}

			// Защита от дубликатов
			if (!permanentFailed.TryAdd(key, 1)) return;

			try
			{
				lock (ioLock)
				{
					string path = Path.Combine(RootDir, "failed.tsv");
					if (!File.Exists(path))
					{
						File.WriteAllText(path, ExpectedFailedHeader + "\n", Encoding.UTF8);
					}
					File.AppendAllText(path, key + "\t" + EscapeCell(source ?? "") + "\n", Encoding.UTF8);
				}
			}
			catch (Exception e)
			{
				GATLog.Warn("Не удалось записать в failed.tsv: " + e.Message);
			}
		}

		public static void ClearPermanentFailed()
		{
			permanentFailed.Clear();
			permanentFailedLimitLogged = false;
			try
			{
				lock (ioLock)
				{
					string path = Path.Combine(RootDir, "failed.tsv");
					if (File.Exists(path)) File.Delete(path);
				}
				GATLog.Msg("Список окончательных отбраковок (failed.tsv) успешно очищен.");
			}
			catch (Exception e)
			{
				GATLog.Warn("Не удалось удалить failed.tsv: " + e.Message);
			}
		}

		private static void LoadPermanentFailed()
		{
			try
			{
				string path = Path.Combine(RootDir, "failed.tsv");
				if (!File.Exists(path)) return;

				string[] lines = File.ReadAllLines(path, Encoding.UTF8);
				if (lines.Length == 0) return;

				// Проверка заголовка версии промпта и модели
				string expectedHeader = ExpectedFailedHeader;
				if (!lines[0].StartsWith("# v") || !string.Equals(lines[0].Trim(), expectedHeader, StringComparison.Ordinal))
				{
					GATLog.Warn("Версия промпта или модели изменилась (в файле: " + lines[0] + ", ожидалось: " + expectedHeader + "). Файл failed.tsv сброшен.");
					ClearPermanentFailed();
					return;
				}

				int loaded = 0;
				for (int i = 1; i < lines.Length; i++)
				{
					string line = lines[i];
					if (string.IsNullOrWhiteSpace(line)) continue;
					string[] parts = line.Split('\t');
					if (parts.Length >= 1 && !string.IsNullOrEmpty(parts[0]))
					{
						if (permanentFailed.Count < MaxPermanentFailed)
						{
							permanentFailed[parts[0]] = 1;
							loaded++;
						}
					}
				}
				if (loaded > 0)
					GATLog.Msg("Загружен список окончательных отбраковок: " + loaded + " строк из failed.tsv");
			}
			catch (Exception e)
			{
				GATLog.Warn("Ошибка чтения failed.tsv: " + e.Message);
			}
		}

		public static string RootDir
		{
			get { return Path.Combine(GenFilePaths.SaveDataFolderPath, "GlobalTranslator"); }
		}

		public static string CacheDir { get { return Path.Combine(RootDir, "cache"); } }

		/// <summary>Ключ = MD5(context|source). MD5 здесь не криптография, а только хеш для кэша.</summary>
		public static string Key(string context, string source)
		{
			using (var md5 = MD5.Create())
			{
				byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes((context ?? "") + "|" + (source ?? "")));
				var sb = new StringBuilder(32);
				for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
				return sb.ToString();
			}
		}

		public static bool TryGet(string context, string source, out string translated)
		{
			return map.TryGetValue(Key(context, source), out translated);
		}

		public static bool TryGetByKey(string key, out string translated)
		{
			return map.TryGetValue(key, out translated);
		}

		public static bool TryGetFlat(string source, out string translated)
		{
			if (string.IsNullOrEmpty(source))
			{
				translated = null;
				return false;
			}

			if (flat.TryGetValue(source, out translated)) return true;
			if (flatNoFallback.ContainsKey(source))
			{
				translated = null;
				return false;
			}

			// 1. Двоеточие и пробелы на конце ("Label:" -> "Метка:")
			if (source.EndsWith(":"))
			{
				string trimmed = source.Substring(0, source.Length - 1).TrimEnd();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = translated + source.Substring(trimmed.Length);
					flat[source] = translated;
					return true;
				}
			}

			if (source.EndsWith(": "))
			{
				string trimmed = source.Substring(0, source.Length - 2).TrimEnd();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = translated + source.Substring(trimmed.Length);
					flat[source] = translated;
					return true;
				}
			}

			// 2. Числовой/юнитовый хвост после двоеточия ("Global Animation Speed: 100%" -> head "Global Animation Speed", tail ": 100%")
			int colonIdx = source.LastIndexOf(':');
			if (colonIdx > 0 && colonIdx < source.Length - 1)
			{
				string tail = source.Substring(colonIdx + 1);
				bool isTailNumeric = true;
				for (int i = 0; i < tail.Length; i++)
				{
					char c = tail[i];
					if (!char.IsDigit(c) && c != ' ' && c != '%' && c != '.' && c != ',' && c != '-' && c != '+' && c != '/' && c != 'x' && c != '×')
					{
						isTailNumeric = false;
						break;
					}
				}
				if (isTailNumeric)
				{
					string head = source.Substring(0, colonIdx).TrimEnd();
					if (flat.TryGetValue(head, out string headTrans))
					{
						translated = headTrans + source.Substring(head.Length);
						flat[source] = translated;
						return true;
					}
				}
			}

			// 3. Вопросительный знак на конце ("Enable?" -> "Включить?")
			if (source.EndsWith("?"))
			{
				string trimmed = source.Substring(0, source.Length - 1).TrimEnd();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = translated + "?";
					flat[source] = translated;
					return true;
				}
			}

			// 4. Многоточие на конце ("Loading..." -> "Загрузка...")
			if (source.EndsWith("..."))
			{
				string trimmed = source.Substring(0, source.Length - 3).TrimEnd();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = translated + "...";
					flat[source] = translated;
					return true;
				}
			}

			// 5. Круглые скобки вокруг ("(Default)" -> "(По умолчанию)")
			if (source.StartsWith("(") && source.EndsWith(")") && source.Length > 2)
			{
				string trimmed = source.Substring(1, source.Length - 2).Trim();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = "(" + translated + ")";
					flat[source] = translated;
					return true;
				}
			}

			// 6. Квадратные скобки вокруг ("[MOD]" -> "[МОД]")
			if (source.StartsWith("[") && source.EndsWith("]") && source.Length > 2)
			{
				string trimmed = source.Substring(1, source.Length - 2).Trim();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = "[" + translated + "]";
					flat[source] = translated;
					return true;
				}
			}

			flatNoFallback[source] = 1;
			translated = null;
			return false;
		}

		/// <summary>
		/// Поиск по скомпилированным шаблонам плейсхолдеров для разрешённых строк писем/уведомлений.
		/// НИКОГДА не вызывать во время OnGUI отрисовки.
		/// </summary>
		public static bool TryGetTemplated(string resolved, out string translated)
		{
			translated = null;
			if (string.IsNullOrEmpty(resolved) || templates.IsEmpty) return false;
			int currentTemplateCount = System.Threading.Volatile.Read(ref templateCount);
			if (templateNoMatch.TryGetValue(resolved, out int stored) && stored >= currentTemplateCount) return false;

			foreach (var t in templates)
			{
				if (resolved.Length < t.MinLength) continue;
				if (!string.IsNullOrEmpty(t.Anchor) && !resolved.Contains(t.Anchor)) continue;

				Match m;
				try
				{
					m = t.Pattern.Match(resolved);
				}
				catch (RegexMatchTimeoutException)
				{
					continue;
				}

				if (m.Success)
				{
					string res = t.TargetTemplate;
					for (int i = 1; i < m.Groups.Count; i++)
					{
						string marker = "\u0001" + (i - 1) + "\u0001";
						res = res.Replace(marker, m.Groups[i].Value);
					}
					translated = res;
					flat[resolved] = translated;
					return true;
				}
			}

			if (templateNoMatch.Count > 50000)
			{
				templateNoMatch.Clear();
				GATLog.Warn("UI Harvest: сброшен отрицательный кэш шаблонов (> 50000).");
			}
			templateNoMatch[resolved] = System.Threading.Volatile.Read(ref templateCount);
			return false;
		}

		private static void RegisterTemplateIfAny(string source, string translated)
		{
			if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translated)) return;
			
			if (templateKeys.Count >= 200000)
			{
				if (!templateKeysLimitLogged)
				{
					templateKeysLimitLogged = true;
					GATLog.Warn("UI Harvest: достигнут лимит 200000 уникальных строк для проверки шаблонов. Сбор приостановлен.");
				}
				return;
			}

			if (!templateKeys.TryAdd(source, 1)) return;

			MatchCollection matches = PlaceholderGuard.Ph.Matches(source);
			if (matches.Count == 0) return;

			var sbRegex = new StringBuilder("^");
			string anchor = "";
			int minLen = 0;

			var placeholderToGroupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
			var groupOrder = new List<string>();
			int currentGroup = 0;
			int lastPos = 0;

			foreach (Match m in matches)
			{
				if (m.Index > lastPos)
				{
					string lit = source.Substring(lastPos, m.Index - lastPos);
					string esc = Regex.Escape(lit);
					sbRegex.Append(esc);
					minLen += lit.Length;
					if (lit.Length > anchor.Length) anchor = lit;
				}

				if (m.Value == "\\n") { sbRegex.Append(@"(?:\\n|\r?\n)"); lastPos = m.Index + m.Length; continue; }

				string phVal = m.Value;
				if (!placeholderToGroupIndex.TryGetValue(phVal, out int groupNum))
				{
					currentGroup++;
					placeholderToGroupIndex[phVal] = currentGroup;
					groupOrder.Add(phVal);
					sbRegex.Append(@"([\s\S]+?)");
				}
				else
				{
					sbRegex.Append(@"\" + groupNum);
				}

				lastPos = m.Index + m.Length;
			}

			if (lastPos < source.Length)
			{
				string lit = source.Substring(lastPos);
				string esc = Regex.Escape(lit);
				sbRegex.Append(esc);
				minLen += lit.Length;
				if (lit.Length > anchor.Length) anchor = lit;
			}

			sbRegex.Append(@"\z");

			if (anchor.Length < 12) return;

			if (System.Threading.Volatile.Read(ref templateCount) >= 5000)
			{
				if (!templateCountLimitLogged)
				{
					templateCountLimitLogged = true;
					GATLog.Warn("UI Harvest: достигнут лимит 5000 шаблонов реверс-индекса. Сбор шаблонов приостановлен.");
				}
				return;
			}

			string ruPattern = translated;
			var sortedPairs = groupOrder.Select((val, idx) => new { val, idx }).OrderByDescending(x => x.val.Length).ToList();
			foreach (var pair in sortedPairs)
			{
				string marker = "\u0001" + pair.idx + "\u0001";
				ruPattern = ruPattern.Replace(pair.val, marker);
			}

			for (int i = 0; i < groupOrder.Count; i++)
			{
				if (!ruPattern.Contains("\u0001" + i + "\u0001")) return;
			}

			try
			{
				var record = new TemplateRecord
				{
					Pattern = new Regex(sbRegex.ToString(), RegexOptions.Singleline, TimeSpan.FromMilliseconds(10)),
					TargetTemplate = ruPattern,
					MinLength = minLen,
					Anchor = anchor
				};
				templates.Add(record);
				System.Threading.Interlocked.Increment(ref templateCount);
			}
			catch { }
		}

		public static void Put(string context, string source, string translated)
		{
			string key = Key(context, source);
			map[key] = translated;
			if (source != null)
			{
				sources[key] = source;
				if (context == "ui" || context == "title")
					flat[source] = translated;
				else
					flat.TryAdd(source, translated);

				byte ig;
				flatNoFallback.TryRemove(source, out ig);
				RegisterTemplateIfAny(source, translated);
			}
			dirtyShards[ShardOf(key)] = 1;
		}

		private static string ShardOf(string key)
		{
			return key.Substring(0, 2); // 256 файлов максимум, реально меньше
		}

		private static string EscapeCell(string s)
		{
			return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n");
		}

		private static string UnescapeCell(string s)
		{
			if (s == null || s.IndexOf('\\') < 0) return s;
			var sb = new StringBuilder(s.Length);
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
				char n = s[i + 1];
				if (n == 'n')       { sb.Append('\n'); i++; }
				else if (n == 't')  { sb.Append('\t'); i++; }
				else if (n == '\\') { sb.Append('\\'); i++; }
				else                { sb.Append(c); }
			}
			return sb.ToString();
		}

		public static void Load()
		{
			try
			{
				Directory.CreateDirectory(CacheDir);
				LoadPermanentFailed();
				int loaded = 0, legacy = 0;
				foreach (string file in Directory.GetFiles(CacheDir, "*.tsv"))
				{
					foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
					{
						if (line.Length == 0) continue;
						string[] parts = line.Split(new[] { '\t' }, 3);
						if (parts.Length == 3)
						{
							string k = parts[0];
							string srcText = UnescapeCell(parts[1]);
							string val = UnescapeCell(parts[2]);
							map[k] = val;
							if (srcText.Length > 0)
							{
								sources[k] = srcText;
								flat[srcText] = val;
								RegisterTemplateIfAny(srcText, val);
							}
							loaded++;
						}
						else if (parts.Length == 2)
						{
							// Старый формат без колонки источника.
							map[parts[0]] = UnescapeCell(parts[1]);
							loaded++;
							legacy++;
						}
					}
				}
				GATLog.Msg("Кэш загружен: " + loaded + " строк из " + CacheDir);
				if (legacy > 0)
					GATLog.Warn("Записей старого формата: " + legacy +
								". Слой 3 для них не работает, пока они не будут перезаписаны.");
			}
			catch (Exception e)
			{
				GATLog.Warn("Не удалось загрузить кэш: " + e);
			}
		}

		/// <summary>Сбрасывает на диск только изменённые шарды.</summary>
		public static void Flush()
		{
			if (dirtyShards.Count == 0) return;
			lock (ioLock)
			{
				// Повторная проверка под локом: пока мы ждали, другой поток мог всё сбросить.
				if (dirtyShards.Count == 0) return;

				var shards = new List<string>(dirtyShards.Keys);
				if (shards.Count == 0) return;

				// Снимаем флаги СРАЗУ под локом. Если Put() прилетит во время записи —
				// он пометит шард грязным заново, и новые данные уйдут следующим Flush().
				for (int i = 0; i < shards.Count; i++)
				{
					byte ignored;
					dirtyShards.TryRemove(shards[i], out ignored);
				}

				try
				{
					Directory.CreateDirectory(CacheDir);

					var buffers = new Dictionary<string, StringBuilder>(shards.Count, StringComparer.Ordinal);
					for (int i = 0; i < shards.Count; i++)
						buffers[shards[i]] = new StringBuilder(8192);

					foreach (var kv in map)
					{
						if (kv.Key == null || kv.Key.Length < 2) continue;

						StringBuilder sb;
						if (!buffers.TryGetValue(kv.Key.Substring(0, 2), out sb)) continue;

						string srcText;
						sources.TryGetValue(kv.Key, out srcText);
						sb.Append(kv.Key).Append('\t')
						  .Append(EscapeCell(srcText ?? "")).Append('\t')
						  .Append(EscapeCell(kv.Value))
						  .Append('\n');
					}

					var encoding = new UTF8Encoding(false);
					foreach (var kv in buffers)
					{
						string path = Path.Combine(CacheDir, kv.Key + ".tsv");
						string tmp = path + ".tmp";
						File.WriteAllText(tmp, kv.Value.ToString(), encoding);
						if (File.Exists(path)) File.Delete(path);
						File.Move(tmp, path);
					}
				}
				catch (Exception e)
				{
					// Возвращаем флаги обратно, чтобы при ошибке диск данные не потерялись
					for (int i = 0; i < shards.Count; i++) dirtyShards[shards[i]] = 1;
					GATLog.Warn("Не удалось сохранить кэш: " + e);
				}
			}
		}

		public static void Clear()
		{
			map.Clear();
			sources.Clear();
			flat.Clear();
			dirtyShards.Clear();
			try
			{
				if (Directory.Exists(CacheDir))
					foreach (string f in Directory.GetFiles(CacheDir, "*.tsv")) File.Delete(f);
			}
			catch (Exception e) { GATLog.Warn("Не удалось очистить кэш: " + e); }
		}

		/// <summary>Снимок всего кэша — нужен экспортёру.</summary>
		public static Dictionary<string, string> Snapshot()
		{
			return new Dictionary<string, string>(map, StringComparer.Ordinal);
		}
	}

	public static class GATLog
	{
		private const string Tag = "[GlobalAutoTranslator] ";
		public static void Msg(string s) { Log.Message(Tag + s); }
		public static void Warn(string s) { Log.Warning(Tag + s); }
		public static void Err(string s) { Log.Error(Tag + s); }
	}
}
