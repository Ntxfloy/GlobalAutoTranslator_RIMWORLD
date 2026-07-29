using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Verse;

namespace GlobalAutoTranslator
{
	/// <summary>
	/// Экспорт накопленного кэша в нативные файлы локализации RimWorld.
	///
	/// Зачем: после экспорта перевод работает штатными средствами игры — без Harmony,
	/// без сети, без этого мода вообще. Самый чистый конечный результат.
	///
	/// Куда: <папка данных игры>/GlobalTranslator/GeneratedRussian/
	/// Дальше папку нужно скопировать в Mods/ и включить в списке модов САМЫМ НИЗКНИМ.
	/// </summary>
	public static class LanguageExporter
	{
		/// <summary>key -> английский текст. Копится по ходу игры из слоя 2.</summary>
		public static readonly ConcurrentDictionary<string, string> ObservedKeyed =
			new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

		public static void NoteKeyed(string key, string english)
		{
			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(english)) return;
			ObservedKeyed[key] = english;
		}

		public static string ExportAll()
		{
			string root = Path.Combine(TranslationCache.RootDir, "GeneratedRussian");
			string langDir = Path.Combine(root, Path.Combine("Languages", "Russian (Русский)"));
			string defInjected = Path.Combine(langDir, "DefInjected");
			string keyedDir = Path.Combine(langDir, "Keyed");

			try
			{
				Directory.CreateDirectory(defInjected);
				Directory.CreateDirectory(keyedDir);
				WriteAboutXml(root);

				int defCount = ExportDefInjected(defInjected);
				int keyedCount = ExportKeyed(keyedDir);

				GATLog.Msg("Экспорт завершён: DefInjected " + defCount + " строк, Keyed " + keyedCount + " строк -> " + root);
			}
			catch (Exception e)
			{
				GATLog.Err("Ошибка экспорта: " + e);
			}
			return root;
		}

		private static int ExportDefInjected(string outDir)
		{
			int total = 0;
			var s = GATMod.Settings;

			foreach (Type defType in GenDefDatabase.AllDefTypesWithDatabases())
			{
				IEnumerable allDefs;
				try
				{
					Type dbType = typeof(DefDatabase<>).MakeGenericType(defType);
					PropertyInfo prop = dbType.GetProperty("AllDefs", BindingFlags.Public | BindingFlags.Static);
					if (prop == null) continue;
					allDefs = prop.GetValue(null, null) as IEnumerable;
					if (allDefs == null) continue;
				}
				catch { continue; }

				var sb = new StringBuilder();
				int count = 0;

				foreach (object o in allDefs)
				{
					var def = o as Def;
					if (def == null || def.defName.NullOrEmpty()) continue;

					// На этот момент def.label уже может быть подменён слоем 1 —
					// тогда он уже по-русски и пишем его напрямую.
					count += TryWrite(sb, def.defName + ".label", def.label, "label");
					if (s.translateDescriptions)
						count += TryWrite(sb, def.defName + ".description", def.description, "description");
				}

				if (count == 0) continue;

				string dir = Path.Combine(outDir, defType.Name);
				Directory.CreateDirectory(dir);
				WriteLanguageData(Path.Combine(dir, defType.Name + "s.xml"), sb.ToString());
				total += count;
			}
			return total;
		}

		private static int ExportKeyed(string outDir)
		{
			var sb = new StringBuilder();
			int count = 0;
			foreach (var kv in ObservedKeyed)
			{
				string ru;
				if (!TranslationCache.TryGet("keyed", kv.Value, out ru)) continue;
				sb.Append("    <").Append(kv.Key).Append('>')
				  .Append(Xml(ru))
				  .Append("</").Append(kv.Key).Append(">\r\n");
				count++;
			}
			if (count > 0) WriteLanguageData(Path.Combine(outDir, "GAT_Generated.xml"), sb.ToString());
			return count;
		}

		private static int TryWrite(StringBuilder sb, string tag, string value, string context)
		{
			if (string.IsNullOrEmpty(value)) return 0;

			// Случай 1: в кэше есть перевод для этого английского текста.
			string ru;
			if (TranslationCache.TryGet(context, value, out ru))
			{
				sb.Append("    <").Append(tag).Append('>').Append(Xml(ru)).Append("</").Append(tag).Append(">\r\n");
				return 1;
			}

			// Случай 2: слой 1 уже подменил поле на русский — берём как есть.
			if (!PlaceholderGuard.ShouldTranslate(value))
			{
				bool looksRussian = false;
				for (int i = 0; i < value.Length; i++)
				{
					if (value[i] >= 0x0400 && value[i] <= 0x04FF) { looksRussian = true; break; }
				}
				if (looksRussian)
				{
					sb.Append("    <").Append(tag).Append('>').Append(Xml(value)).Append("</").Append(tag).Append(">\r\n");
					return 1;
				}
			}
			return 0;
		}

		/// <summary>RimWorld требует UTF-8. BOM допустим и спасает от кракозябр в редакторах Windows.</summary>
		private static void WriteLanguageData(string path, string inner)
		{
			var sb = new StringBuilder();
			sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n");
			sb.Append("<LanguageData>\r\n");
			sb.Append(inner);
			sb.Append("</LanguageData>\r\n");
			File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
		}

		private static void WriteAboutXml(string root)
		{
			string aboutDir = Path.Combine(root, "About");
			Directory.CreateDirectory(aboutDir);
			string xml =
				"<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
				"<ModMetaData>\r\n" +
				"  <packageId>ayder.generatedrussian</packageId>\r\n" +
				"  <name>Generated Russian (GAT)</name>\r\n" +
				"  <author>Global Auto Translator</author>\r\n" +
				"  <description>Автоматически сгенерированный русский перевод. Включать САМЫМ НИЗКНИМ в списке модов.</description>\r\n" +
				"  <supportedVersions><li>1.6</li></supportedVersions>\r\n" +
				"</ModMetaData>\r\n";
			File.WriteAllText(Path.Combine(aboutDir, "About.xml"), xml, new UTF8Encoding(true));
		}

		private static string Xml(string s)
		{
			if (string.IsNullOrEmpty(s)) return string.Empty;
			return s.Replace("&", "&amp;")
			        .Replace("<", "&lt;")
			        .Replace(">", "&gt;");
		}
	}
}
