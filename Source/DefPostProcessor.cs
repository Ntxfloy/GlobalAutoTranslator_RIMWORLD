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
		private static readonly object walkLock = new object();

		private static readonly FieldInfo labelCapField =
			typeof(Def).GetField("cachedLabelCap", BindingFlags.Instance | BindingFlags.NonPublic);

		private static readonly ConcurrentQueue<Action> pendingApply = new ConcurrentQueue<Action>();

		private static bool derivedDirty;
		private static float lastRefreshRealtime;

		public static void MarkDerivedDirty()
		{
			derivedDirty = true;
		}

		public static void CheckThrottledRefresh()
		{
			if (!derivedDirty) return;
			float now = UnityEngine.Time.realtimeSinceStartup;
			if (now - lastRefreshRealtime >= 2.0f)
			{
				lastRefreshRealtime = now;
				derivedDirty = false;
				RefreshDerivedDefLabels();
			}
		}

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
			bool anyApplied = false;
			while (n < maxPerCall && pendingApply.TryDequeue(out a))
			{
				try { a(); anyApplied = true; } catch { }
				n++;
			}
			if (anyApplied)
			{
				MarkDerivedDirty();
			}
			CheckThrottledRefresh();
		}

		public static bool IsFleshRace(RaceProperties race)
		{
			if (race == null) return false;
			try
			{
				return race.IsFlesh;
			}
			catch
			{
				return race.hasMeat;
			}
		}

		public static bool ShouldRefreshGeneratedMeatLabel(ThingDef source, ThingDef target)
		{
			if (source == null || target == null) return false;
			if (source.category != Verse.ThingCategory.Pawn) return false;
			if (source.race == null) return false;
			if (!IsFleshRace(source.race)) return false;
			if (source.race.useMeatFrom != null) return false;
			if (!string.IsNullOrEmpty(source.race.meatLabel)) return false;
			if (string.IsNullOrEmpty(source.defName)) return false;
			if (!string.Equals(target.defName, "Meat_" + source.defName, StringComparison.Ordinal)) return false;
			if (target.ingestible == null) return false;
			if (!object.ReferenceEquals(target.ingestible.sourceDef, source)) return false;
			return true;
		}

		public static bool ShouldRefreshGeneratedCorpseLabel(ThingDef source, ThingDef target)
		{
			if (source == null || target == null) return false;
			if (source.category != Verse.ThingCategory.Pawn || source.race == null) return false;
			if (string.IsNullOrEmpty(source.defName)) return false;
			if (!string.Equals(target.defName, "Corpse_" + source.defName, StringComparison.Ordinal)) return false;
			if (target.ingestible == null) return false;
			if (!object.ReferenceEquals(target.ingestible.sourceDef, source)) return false;
			return true;
		}

		/// <summary>
		/// Shared production kernel for applying a derived label to a target Def.
		/// formatter: takes source.label, returns the expected new label string.
		/// keyName: the translation key name (e.g. "MeatLabel") used only to reject untranslated keys.
		/// On exception from formatter: propagates to caller (no mutation of target).
		/// </summary>
		public static bool TryApplyDerivedLabelCore(ThingDef source, ThingDef target, System.Func<string, string> formatter, string keyName, ref int applied, ref int skipped)
		{
			string newLabel = formatter(source.label);
			if (newLabel == keyName || !newLabel.Contains(source.label))
				return false;

			if (target.label != newLabel)
			{
				target.label = newLabel;
				ResetLabelCap(target);
				applied++;
				return true;
			}
			else
			{
				skipped++;
				return false;
			}
		}

		// Production meat label refresh — uses RimWorld Translate, no fallback
		public static bool TryApplyMeatLabelRefresh(ThingDef source, ThingDef target, bool canMeatLabel, ref int meats, ref int skipped)
		{
			if (!canMeatLabel) return false;
			if (!ShouldRefreshGeneratedMeatLabel(source, target)) return false;
			return TryApplyDerivedLabelCore(source, target, (label) => "MeatLabel".Translate(label).RawText, "MeatLabel", ref meats, ref skipped);
		}

		// Production corpse label refresh — uses RimWorld Translate, no fallback
		public static bool TryApplyCorpseLabelRefresh(ThingDef source, ThingDef target, bool canCorpseLabel, ref int corpses, ref int skipped)
		{
			if (!canCorpseLabel) return false;
			if (!ShouldRefreshGeneratedCorpseLabel(source, target)) return false;
			return TryApplyDerivedLabelCore(source, target, (label) => "CorpseLabel".Translate(label).RawText, "CorpseLabel", ref corpses, ref skipped);
		}

		// Test overload: meat label refresh with injectable formatter
		public static bool TryApplyMeatLabelRefresh(ThingDef source, ThingDef target, bool canMeatLabel, ref int meats, ref int skipped, System.Func<string, string> formatter)
		{
			if (!canMeatLabel) return false;
			if (!ShouldRefreshGeneratedMeatLabel(source, target)) return false;
			return TryApplyDerivedLabelCore(source, target, formatter, "MeatLabel", ref meats, ref skipped);
		}

		// Test overload: corpse label refresh with injectable formatter
		public static bool TryApplyCorpseLabelRefresh(ThingDef source, ThingDef target, bool canCorpseLabel, ref int corpses, ref int skipped, System.Func<string, string> formatter)
		{
			if (!canCorpseLabel) return false;
			if (!ShouldRefreshGeneratedCorpseLabel(source, target)) return false;
			return TryApplyDerivedLabelCore(source, target, formatter, "CorpseLabel", ref corpses, ref skipped);
		}

		public static void RefreshDerivedDefLabels()
		{
			int recipes = 0, jobStrings = 0, blueprints = 0, frames = 0, corpses = 0, meats = 0, terrain = 0, skipped = 0, errors = 0;
			try
			{
				bool canRecipeMake = Verse.Translator.CanTranslate("RecipeMake");
				bool canRecipeMakeJob = Verse.Translator.CanTranslate("RecipeMakeJobString");

				bool canRecipeAdminister = Verse.Translator.CanTranslate("RecipeAdminister");
				bool canRecipeAdministerJob = Verse.Translator.CanTranslate("RecipeAdministerJobString");

				bool canBlueprintExtra = Verse.Translator.CanTranslate("BlueprintLabelExtra");
				string bpExtraText = canBlueprintExtra ? "BlueprintLabelExtra".Translate().RawText : null;
				if (bpExtraText == "BlueprintLabelExtra") canBlueprintExtra = false;

				bool canFrameExtra = Verse.Translator.CanTranslate("FrameLabelExtra");
				string frameExtraText = canFrameExtra ? "FrameLabelExtra".Translate().RawText : null;
				if (frameExtraText == "FrameLabelExtra") canFrameExtra = false;

				bool canCorpseLabel = Verse.Translator.CanTranslate("CorpseLabel");
				bool canMeatLabel = Verse.Translator.CanTranslate("MeatLabel");

				// 1. Пересборка рецептов Make_* и Administer_*
				var recipeDefs = DefDatabase<Verse.RecipeDef>.AllDefsListForReading;
				if (recipeDefs != null)
				{
					for (int i = 0; i < recipeDefs.Count; i++)
					{
						var r = recipeDefs[i];
						if (r == null || string.IsNullOrEmpty(r.defName)) continue;

						try
						{
							if (r.defName.StartsWith("Make_"))
							{
								var prod = r.ProducedThingDef;
								if (prod != null && !string.IsNullOrEmpty(prod.label))
								{
									if (canRecipeMake)
									{
										TaggedString newLabelTS = "RecipeMake".Translate(prod.label);
										string newLabel = newLabelTS.RawText;
										if (newLabel != "RecipeMake" && newLabel.Contains(prod.label))
										{
											if (r.label != newLabel)
											{
												r.label = newLabel;
												ResetLabelCap(r);
												recipes++;
											}
											else { skipped++; }
										}
										else { skipped++; }
									}

									if (canRecipeMakeJob)
									{
										TaggedString newJobTS = "RecipeMakeJobString".Translate(prod.label);
										string newJob = newJobTS.RawText;
										if (newJob != "RecipeMakeJobString" && newJob.Contains(prod.label))
										{
											if (r.jobString != newJob)
											{
												r.jobString = newJob;
												jobStrings++;
											}
											else { skipped++; }
										}
										else { skipped++; }
									}
								}
								else { skipped++; }
							}
							else if (r.defName.StartsWith("Administer_"))
							{
								ThingDef ing = null;
								if (r.ingredients != null && r.ingredients.Count > 0 && r.ingredients[0].filter != null)
								{
									var allowed = r.ingredients[0].filter.AllowedThingDefs;
									if (allowed != null)
									{
										foreach (var tDef in allowed) { ing = tDef; break; }
									}
								}
								if (ing == null) ing = r.ProducedThingDef;

								if (ing != null && !string.IsNullOrEmpty(ing.label))
								{
									if (canRecipeAdminister)
									{
										TaggedString newLabelTS = "RecipeAdminister".Translate(ing.label);
										string newLabel = newLabelTS.RawText;
										if (newLabel != "RecipeAdminister" && newLabel.Contains(ing.label))
										{
											if (r.label != newLabel)
											{
												r.label = newLabel;
												ResetLabelCap(r);
												recipes++;
											}
											else { skipped++; }
										}
										else { skipped++; }
									}

									if (canRecipeAdministerJob)
									{
										TaggedString newJobTS = "RecipeAdministerJobString".Translate(ing.label);
										string newJob = newJobTS.RawText;
										if (newJob != "RecipeAdministerJobString" && newJob.Contains(ing.label))
										{
											if (r.jobString != newJob)
											{
												r.jobString = newJob;
												jobStrings++;
											}
											else { skipped++; }
										}
										else { skipped++; }
									}
								}
								else { skipped++; }
							}
						}
						catch
						{
							errors++;
						}
					}
				}

				// 2. Чертежи, каркасы, трупы (corpseDef) и мясо (meatDef) для ThingDef
				var thingDefs = DefDatabase<ThingDef>.AllDefsListForReading;
				if (thingDefs != null)
				{
					for (int i = 0; i < thingDefs.Count; i++)
					{
						var t = thingDefs[i];
						if (t == null || string.IsNullOrEmpty(t.label)) continue;

						try
						{
							if (t.blueprintDef != null && canBlueprintExtra)
							{
								string expected = t.label + bpExtraText;
								if (t.blueprintDef.label != expected)
								{
									t.blueprintDef.label = expected;
									ResetLabelCap(t.blueprintDef);
									blueprints++;
								}
								else { skipped++; }
							}

							if (t.frameDef != null && canFrameExtra)
							{
								string expected = t.label + frameExtraText;
								if (t.frameDef.label != expected)
								{
									t.frameDef.label = expected;
									ResetLabelCap(t.frameDef);
									frames++;
								}
								else { skipped++; }
							}

							if (t.installBlueprintDef != null && canBlueprintExtra)
							{
								string expected = t.label + bpExtraText;
								if (t.installBlueprintDef.label != expected)
								{
									t.installBlueprintDef.label = expected;
									ResetLabelCap(t.installBlueprintDef);
									blueprints++;
								}
								else { skipped++; }
							}

							// Трупы (corpseDef)
							if (t.race != null)
							{
								TryApplyCorpseLabelRefresh(t, t.race.corpseDef, canCorpseLabel, ref corpses, ref skipped);
							}

							// Мясо (meatDef)
							if (t.race != null)
							{
								TryApplyMeatLabelRefresh(t, t.race.meatDef, canMeatLabel, ref meats, ref skipped);
							}
						}
						catch
						{
							errors++;
						}
					}
				}

				// 3. Чертежи и каркасы полов (TerrainDef)
				var terrainDefs = DefDatabase<Verse.TerrainDef>.AllDefsListForReading;
				if (terrainDefs != null)
				{
					for (int i = 0; i < terrainDefs.Count; i++)
					{
						var ter = terrainDefs[i];
						if (ter == null || string.IsNullOrEmpty(ter.label)) continue;

						try
						{
							if (ter.blueprintDef != null && canBlueprintExtra)
							{
								string expected = ter.label + bpExtraText;
								if (ter.blueprintDef.label != expected)
								{
									ter.blueprintDef.label = expected;
									ResetLabelCap(ter.blueprintDef);
									terrain++;
								}
								else { skipped++; }
							}

							if (ter.frameDef != null && canFrameExtra)
							{
								string expected = ter.label + frameExtraText;
								if (ter.frameDef.label != expected)
								{
									ter.frameDef.label = expected;
									ResetLabelCap(ter.frameDef);
									terrain++;
								}
								else { skipped++; }
							}
						}
						catch
						{
							errors++;
						}
					}
				}
			}
			catch (Exception e)
			{
				GATLog.Warn("Ошибка при выполнении RefreshDerivedDefLabels: " + e.Message);
				errors++;
			}

			int totalUpdated = recipes + jobStrings + blueprints + frames + corpses + meats + terrain;
			if (totalUpdated > 0)
			{
				GATLog.Msg("Derived labels refreshed: recipes=" + recipes + ", jobStrings=" + jobStrings + ", blueprints=" + blueprints + ", frames=" + frames + ", corpses=" + corpses + ", meats=" + meats + ", terrain=" + terrain + ", skipped=" + skipped + ", errors=" + errors + ".");
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
			lock (walkLock)
			{
				if (alreadyRan) return;
				alreadyRan = true;
				int applied, queued;
				Walk(true, out applied, out queued);
				GATLog.Msg("Пост-обработка Defs: применено из кэша " + applied + ", отправлено на перевод " + queued);
			}
		}

		/// <summary>Кнопка в настройках: только набить очередь, не меняя объекты.</summary>
		public static int EnqueueAll()
		{
			lock (walkLock)
			{
				int applied, queued;
				Walk(false, out applied, out queued);
				return queued;
			}
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

			if (apply)
			{
				RefreshDerivedDefLabels();
			}
		}

		private static readonly ConcurrentDictionary<Type, FieldInfo[]> extrasCache =
			new ConcurrentDictionary<Type, FieldInfo[]>();

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
			if (TranslationCache.TryGet(context, src, out cached) && !string.IsNullOrWhiteSpace(cached))
			{
				if (apply) { setter(cached); applied++; }
				return;
			}

			TranslateWorker.Enqueue(context, src, v => { if (apply) QueueApply(() => setter(v)); });
			queued++;
		}
	}
}
