using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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

		private static readonly ConcurrentDictionary<string, byte> dirtyShards =
			new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

		private static readonly object ioLock = new object();

		public static int Count { get { return map.Count; } }

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

			// 1. Двоеточие и пробелы на конце ("Label:" -> "Метка:")
			if (source.EndsWith(":"))
			{
				string trimmed = source.Substring(0, source.Length - 1).TrimEnd();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = translated + ":";
					return true;
				}
			}

			if (source.EndsWith(": "))
			{
				string trimmed = source.Substring(0, source.Length - 2).TrimEnd();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = translated + ": ";
					return true;
				}
			}

			// 2. Вопросительный знак на конце ("Enable?" -> "Включить?")
			if (source.EndsWith("?"))
			{
				string trimmed = source.Substring(0, source.Length - 1).TrimEnd();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = translated + "?";
					return true;
				}
			}

			// 3. Многоточие на конце ("Loading..." -> "Загрузка...")
			if (source.EndsWith("..."))
			{
				string trimmed = source.Substring(0, source.Length - 3).TrimEnd();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = translated + "...";
					return true;
				}
			}

			// 4. Круглые скобки вокруг ("(Default)" -> "(По умолчанию)")
			if (source.StartsWith("(") && source.EndsWith(")") && source.Length > 2)
			{
				string trimmed = source.Substring(1, source.Length - 2).Trim();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = "(" + translated + ")";
					return true;
				}
			}

			// 5. Квадратные скобки вокруг ("[MOD]" -> "[МОД]")
			if (source.StartsWith("[") && source.EndsWith("]") && source.Length > 2)
			{
				string trimmed = source.Substring(1, source.Length - 2).Trim();
				if (flat.TryGetValue(trimmed, out translated))
				{
					translated = "[" + translated + "]";
					return true;
				}
			}

			translated = null;
			return false;
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
