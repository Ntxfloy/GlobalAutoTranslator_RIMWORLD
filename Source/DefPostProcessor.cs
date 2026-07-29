using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace GlobalAutoTranslator
{
	/// <summary>
	/// СЛОЙ 1 — самый дешёвый и самый полезный.
	/// Один раз после загрузки Defs проходим по всем базам и подменяем label/description
	/// напрямую в объектах. После этого игра работает со своими полями без оверхеда:
	/// ни одного перехвата на кадр.
	/// </summary>
	public static class DefPostProcessor
	{
		private static bool alreadyRan;

		private static readonly FieldInfo labelCapField =
			typeof(Def).GetField("cachedLabelCap", BindingFlags.Instance | BindingFlags.NonPublic);

		private static readonly ConcurrentQueue<Action> pendingApply = new ConcurrentQueue<Action>();

		/// <summary>Ставит изменение Def в очередь. Безопасно из любого потока.</summary>
		public static void QueueApply(Action a)
		{
			if (a != null) pendingApply.Enqueue(a);
		}

		/// <summary>Применяет отложенные изменения. ТОЛЬКО из главного потока Unity.</summary>
		public static void DrainApply(int maxPerCall = 200)
		{
			Action a;
			int n = 0;
			while (n < maxPerCall && pendingApply.TryDequeue(out a))
			{
				try { a(); } catch { }
				n++;
			}
		}

		static DefPostProcessor()
		{
			if (labelCapField == null)
				GATLog.Warn("Не найдено приватное поле cachedLabelCap в Verse.Def. Сброс кэша LabelCap работать не будет.");
		}

		private static void ResetLabelCap(Def def)
		{
			if (def == null || labelCapField == null) return;
			try { labelCapField.SetValue(def, default(TaggedString)); } catch { }
		}

		// Поля Def, которые имеет смысл переводить помимо label/description.
		private static readonly string[] ExtraStringFields =
		{
			"jobString", "gerund", "gerundLabel", "reportString", "verb",
			"letterLabel", "letterText", "pawnLabel", "pawnsPlural",
			"labelShort", "labelNoun", "labelPlural", "beginLetter", "beginLetterLabel",
			"baseDescription", "descriptionShort", "skillLabel", "headerTip",
		};

		public static void Run()
		{
			if (alreadyRan) return;
			alreadyRan = true;
			int applied, queued;
			Walk(true, out applied, out queued);
			GATLog.Msg("Пост-обработка Defs: применено из кэша " + applied + ", отправлено на перевод " + queued);
		}

		/// <summary>Кнопка в настройках: только набить очередь, не меняя объекты.</summary>
		public static int EnqueueAll()
		{
			int applied, queued;
			Walk(false, out applied, out queued);
			return queued;
		}

		private static void Walk(bool apply, out int applied, out int queued)
		{
			applied = 0;
			queued = 0;
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

				FieldInfo[] extras = ResolveExtras(defType);

				foreach (object o in allDefs)
				{
					var def = o as Def;
					if (def == null) continue;

					// label
					HandleString("label", def.label, apply, ref applied, ref queued,
						v => { def.label = v; ResetLabelCap(def); });

					// description
					if (s.translateDescriptions)
						HandleString("description", def.description, apply, ref applied, ref queued,
							v => def.description = v);

					// Сценарии (ScenarioDef): обработка полей встроенного сценария (name, summary, description)
					var scenDef = def as RimWorld.ScenarioDef;
					if (scenDef != null && scenDef.scenario != null)
					{
						var scen = scenDef.scenario;
						HandleString("label", scen.name, apply, ref applied, ref queued, v => scen.name = v);
						if (s.translateDescriptions)
						{
							HandleString("description", scen.summary, apply, ref applied, ref queued, v => scen.summary = v);
							HandleString("description", scen.description, apply, ref applied, ref queued, v => scen.description = v);
						}
					}

					// дополнительные строковые поля
					for (int i = 0; i < extras.Length; i++)
					{
						FieldInfo fi = extras[i];
						string cur;
						try { cur = fi.GetValue(def) as string; } catch { continue; }
						if (cur == null) continue;
						FieldInfo captured = fi;
						Def capturedDef = def;
						HandleString("label", cur, apply, ref applied, ref queued,
							v => { try { captured.SetValue(capturedDef, v); } catch { } });
					}
				}
			}
		}

		private static readonly Dictionary<Type, FieldInfo[]> extrasCache = new Dictionary<Type, FieldInfo[]>();

		private static FieldInfo[] ResolveExtras(Type defType)
		{
			FieldInfo[] cached;
			if (extrasCache.TryGetValue(defType, out cached)) return cached;

			var found = new List<FieldInfo>();
			foreach (string name in ExtraStringFields)
			{
				FieldInfo fi = defType.GetField(name,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (fi != null && fi.FieldType == typeof(string)) found.Add(fi);
			}
			cached = found.ToArray();
			extrasCache[defType] = cached;
			return cached;
		}

		private static void HandleString(string context, string src, bool apply,
			ref int applied, ref int queued, Action<string> setter)
		{
			if (!PlaceholderGuard.ShouldTranslate(src)) return;

			string cached;
			if (TranslationCache.TryGet(context, src, out cached))
			{
				if (apply) { setter(cached); applied++; }
				return;
			}

			TranslateWorker.Enqueue(context, src, v => { if (apply) QueueApply(() => setter(v)); });
			queued++;
		}
	}
}
