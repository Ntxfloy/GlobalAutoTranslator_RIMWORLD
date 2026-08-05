using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace GlobalAutoTranslator
{
	/// <summary>Задание на перевод одной строки.</summary>
	public sealed class TranslateJob
	{
		public string Context;   // label / description / keyed
		public string Source;
		public string Key;
		public Action<string> OnDone; // ОСТОРОЖНО: вызывается из рабочего потока, не трогать Unity API
		public int NetworkRetries;      // сколько раз уже повторяли из-за сетевой ошибки
		public bool Volatile;           // если true — переводить, но не сохранять в постоянный кэш на диск
		public bool PlaceholderRetried; // true — это уже повторная попытка после отбраковки по плейсхолдерам
	}

	/// <summary>
	/// Фоновый воркер: собирает строки в батчи, бьёт в LLM, валидирует, кладёт в кэш.
	/// НИКОГДА не блокирует главный поток: если перевода ещё нет, игра показывает оригинал.
	/// </summary>
	public static class TranslateWorker
	{
		private static readonly ConcurrentQueue<TranslateJob> queue = new ConcurrentQueue<TranslateJob>();
		private static readonly ConcurrentDictionary<string, byte> inFlight =
			new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
		private static readonly ConcurrentDictionary<string, byte> quarantine =
			new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

		private static Thread[] threads;
		private static volatile bool running;
		private static int flushCounter;
		private static int consecutiveFailedBatches;

		// Номер поколения пула. Каждый Start() инкрементит. Поток, чьё поколение
		// устарело, обязан умереть, даже если running успел стать true заново.
		private static int generation;

		/// <summary>Сколько рабочих потоков реально живо. 0 — пул остановлен.</summary>
		public static int ActiveThreads
		{
			get { var t = threads; return t == null ? 0 : t.Length; }
		}

		public static int Pending { get { return queue.Count; } }
		public static int InFlight { get { return inFlight.Count; } }
		public static int Failed { get { return quarantine.Count; } }
		public static int TranslatedThisSession;
		public static bool Paused;

		public static void Start()
		{
			if (running) return;

			// Подстраховка: если прошлый Stop() не дождался потоков, добиваем здесь,
			// иначе получим два пула, жующих одну очередь.
			JoinThreads(1000);

			running = true;
			Paused = false;
			Interlocked.Exchange(ref consecutiveFailedBatches, 0);

			int myGen = Interlocked.Increment(ref generation);

			var s = GATMod.Settings;
			int n = Math.Max(1, Math.Min(4, s.maxConcurrent));
			var pool = new Thread[n];
			for (int i = 0; i < n; i++)
			{
				int threadIdx = i;
				pool[i] = new Thread(() => Loop(myGen));
				pool[i].IsBackground = true;
				pool[i].Name = "GAT-Worker-" + myGen + "-" + threadIdx;
			}
			threads = pool;
			for (int i = 0; i < n; i++) pool[i].Start();

			GATLog.Msg("Запущено потоков перевода: " + n + " (поколение " + myGen + ")");
		}

		public static void Stop()
		{
			if (!running)
			{
				TranslationCache.Flush();
				return;
			}

			running = false;                       // volatile — потоки увидят на следующей итерации Loop
			Interlocked.Increment(ref generation); // осиротевшие потоки не оживут, даже если Start() вернёт running = true
			JoinThreads(2000);                     // общий бюджет ожидания, не на каждый поток
			inFlight.Clear();                      // очищаем невыполненные ключи, чтобы они не блокировали переповтор
			TranslationCache.Flush();
			GATLog.Msg("Пул потоков перевода остановлен.");
		}

		/// <summary>Перезапуск с текущими настройками (например, после смены maxConcurrent).</summary>
		public static void Restart()
		{
			Stop();
			Start();
		}

		/// <summary>Дожидается завершения потоков в пределах общего бюджета в миллисекундах.</summary>
		private static void JoinThreads(int totalBudgetMs)
		{
			var local = threads;
			threads = null;
			if (local == null) return;

			var sw = System.Diagnostics.Stopwatch.StartNew();
			for (int i = 0; i < local.Length; i++)
			{
				Thread t = local[i];
				if (t == null) continue;
				int left = totalBudgetMs - (int)sw.ElapsedMilliseconds;
				if (left <= 0) break;
				try { if (t.IsAlive) t.Join(left); }
				catch { }
			}
		}

		/// <summary>Ставит строку в очередь, если её ещё нет в кэше и не в работе.</summary>
		public static void Enqueue(string context, string source, Action<string> onDone = null, bool isVolatile = false)
		{
			if (!PlaceholderGuard.ShouldTranslate(source)) return;

			string key = TranslationCache.Key(context, source);
			if (quarantine.ContainsKey(key) || TranslationCache.IsPermanentFailed(key)) return;

			string cached;
			if (TranslationCache.TryGetByKey(key, out cached))
			{
				if (onDone != null) onDone(cached);
				return;
			}

			if (context != "ui" && TranslationCache.TryGetTemplated(source, out cached))
			{
				TranslationCache.Put(context, source, cached);
				if (onDone != null) onDone(cached);
				return;
			}

			if (!inFlight.TryAdd(key, 1)) return; // уже в очереди — дедупликация

			queue.Enqueue(new TranslateJob
			{
				Context = context,
				Source = source,
				Key = key,
				OnDone = onDone,
				Volatile = isVolatile,
			});
		}

		private static void Loop(int myGen)
		{
			while (running && Volatile.Read(ref generation) == myGen)
			{
				try
				{
					if (Paused) { NapUntil(500, myGen); continue; }

					var batch = DrainBatch();
					if (batch == null) { NapUntil(400, myGen); continue; }

					ProcessBatch(batch, myGen);

					// Периодический сброс кэша на диск, чтобы не потерять работу при вылете.
					if (Interlocked.Increment(ref flushCounter) % 5 == 0)
						TranslationCache.Flush();
				}
				catch (ThreadAbortException)
				{
					return; // Unity гасит поток при выходе из игры — это норма, молчим
				}
				catch (Exception e)
				{
					GATLog.Warn("Сбой в воркере: " + e);
					NapUntil(1000, myGen);
				}
			}
		}

		/// <summary>Сон, который просыпается на остановке пула. Возвращает false, если пора умирать.</summary>
		private static bool NapUntil(int ms, int myGen)
		{
			const int step = 200;
			int slept = 0;
			while (slept < ms)
			{
				if (!running || Volatile.Read(ref generation) != myGen) return false;
				int chunk = Math.Min(step, ms - slept);
				Thread.Sleep(chunk);
				slept += chunk;
			}
			return running && Volatile.Read(ref generation) == myGen;
		}

		/// <summary>Набирает батч с одинаковым context и одинаковым статусом повтора.</summary>
		private static List<TranslateJob> DrainBatch()
		{
			TranslateJob first;
			if (!queue.TryDequeue(out first)) return null;

			int size = Math.Max(5, Math.Min(120, GATMod.Settings.batchSize));
			var batch = new List<TranslateJob>(size) { first };
			var postpone = new List<TranslateJob>();

			while (batch.Count < size)
			{
				TranslateJob j;
				if (!queue.TryDequeue(out j)) break;
				if (j.Context == first.Context && j.PlaceholderRetried == first.PlaceholderRetried) batch.Add(j);
				else postpone.Add(j);
				if (postpone.Count > size) break;
			}
			foreach (var j in postpone) queue.Enqueue(j);
			return batch;
		}

		private static void ProcessBatch(List<TranslateJob> batch, int myGen)
		{
			var s = GATMod.Settings;
			var items = new Dictionary<string, string>(batch.Count);
			for (int i = 0; i < batch.Count; i++)
				items[i.ToString()] = batch[i].Source;

			bool isRetry = batch[0].PlaceholderRetried;
			var result = LlmClient.TranslateBatch(s, batch[0].Context, items, isRetry);

			if (result == null)
			{
				int fails = Interlocked.Increment(ref consecutiveFailedBatches);

				const int maxNetworkRetries = 3;
				foreach (var j in batch)
				{
					byte ignored;
					inFlight.TryRemove(j.Key, out ignored);
					j.NetworkRetries++;
					if (j.NetworkRetries <= maxNetworkRetries)
					{
						if (s.verboseLogging)
							GATLog.Msg("Сетевая ошибка, повтор " + j.NetworkRetries + "/" + maxNetworkRetries + ": " + j.Source.Substring(0, Math.Min(60, j.Source.Length)));
						inFlight.TryAdd(j.Key, 1);
						queue.Enqueue(j);
					}
					else
					{
						GATLog.Warn("Карантин после " + maxNetworkRetries + " сетевых ошибок: " + j.Source.Substring(0, Math.Min(60, j.Source.Length)));
						quarantine[j.Key] = 1;
					}
				}

				if (fails >= 6)
				{
					Paused = true;
					GATLog.Err("Прокси недоступен (6 батчей подряд с ошибкой). Воркер остановлен. " +
					           "Проверь, запущен ли CLIProxyAPI, и нажми кнопку проверки связи в настройках мода.");
					return;
				}

				int delay = 2000 * (int)Math.Pow(2, Math.Min(4, fails - 1));
				NapUntil(delay, myGen);
				return;
			}

			Interlocked.Exchange(ref consecutiveFailedBatches, 0);

			int ok = 0, untouched = 0, bad = 0;
			for (int i = 0; i < batch.Count; i++)
			{
				var job = batch[i];
				byte ignored;
				inFlight.TryRemove(job.Key, out ignored);

				string dst;
				if (!result.TryGetValue(i.ToString(), out dst))
				{
					bad++;
					quarantine[job.Key] = 1; // Только временный карантин в памяти, в failed.tsv не пишем
					continue;
				}

				// Модель вернула строку без перевода (имя мода, DLC, технический идентификатор).
				// Правило 9 промпта разрешает это. Кэшируем ОРИГИНАЛ, а не ответ модели:
				// при context=label она опускает регистр, и "Core" превратилось бы в "core".
				if (!string.IsNullOrEmpty(dst) && string.Equals(dst.Trim(), job.Source.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					if (!job.Volatile)
						TranslationCache.Put(job.Context, job.Source, job.Source);
					untouched++;
					continue;
				}

				string reason;
				if (!PlaceholderGuard.Validate(job.Source, dst, out reason))
				{
					string srcPreview = job.Source.Length > 200 ? job.Source.Substring(0, 200) + "..." : job.Source;
					if (s.verboseLogging)
						GATLog.Warn("PlaceholderGuard fail (" + reason + ")\n  SRC: " + srcPreview + "\n  DST: " + dst);

					// Одна повторная попытка для отказов по плейсхолдерам/маркерам (не для сетевых)
					if (!job.PlaceholderRetried && reason != null && (reason.Contains("плейсхолдер") || reason.Contains("маркер")))
					{
						job.PlaceholderRetried = true;
						byte ign;
						inFlight.TryRemove(job.Key, out ign);
						inFlight.TryAdd(job.Key, 1);
						queue.Enqueue(job);
						if (s.verboseLogging)
							GATLog.Msg("Повторная попытка (маркеры/плейсхолдеры, temp=0.3): " + srcPreview);
						continue;
					}

					bad++;
					quarantine[job.Key] = 1;
					// Пожизненная блокировка ТОЛЬКО при повторном отказе по маркерам/плейсхолдерам
					if (job.PlaceholderRetried && reason != null && (reason.Contains("плейсхолдер") || reason.Contains("маркер")))
					{
						TranslationCache.AddPermanentFailed(job.Key, job.Source);
						if (s.verboseLogging)
							GATLog.Warn("Окончательный отказ по маркерам/плейсхолдерам (записано в failed.tsv): " + srcPreview);
					}
					continue;
				}

				if (!job.Volatile)
					TranslationCache.Put(job.Context, job.Source, dst);
				Interlocked.Increment(ref TranslatedThisSession);
				ok++;
				if (job.OnDone != null)
				{
					try { job.OnDone(dst); } catch { }
				}
			}

			if (s.verboseLogging)
				GATLog.Msg("Батч [" + batch[0].Context + "]" + (isRetry ? " (RETRY)" : "") + ": принято " + ok + ", без перевода " + untouched +
				           ", отброшено " + bad + ", в очереди " + queue.Count);
		}

		public static void ClearQuarantine()
		{
			quarantine.Clear();
			Interlocked.Exchange(ref consecutiveFailedBatches, 0);
			Paused = false;
		}
	}
}
