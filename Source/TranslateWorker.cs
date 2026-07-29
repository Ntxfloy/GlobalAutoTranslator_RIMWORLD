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

		public static int Pending { get { return queue.Count; } }
		public static int InFlight { get { return inFlight.Count; } }
		public static int Failed { get { return quarantine.Count; } }
		public static int TranslatedThisSession;
		public static bool Paused;

		public static void Start()
		{
			if (running) return;
			running = true;
			var s = GATMod.Settings;
			int n = Math.Max(1, Math.Min(4, s.maxConcurrent));
			threads = new Thread[n];
			for (int i = 0; i < n; i++)
			{
				threads[i] = new Thread(Loop);
				threads[i].IsBackground = true;
				threads[i].Name = "GAT-Worker-" + i;
				threads[i].Start();
			}
			GATLog.Msg("Запущено потоков перевода: " + n);
		}

		public static void Stop()
		{
			running = false;
			TranslationCache.Flush();
		}

		/// <summary>Ставит строку в очередь, если её ещё нет в кэше и не в работе.</summary>
		public static void Enqueue(string context, string source, Action<string> onDone = null)
		{
			if (!PlaceholderGuard.ShouldTranslate(source)) return;

			string key = TranslationCache.Key(context, source);
			if (quarantine.ContainsKey(key)) return;

			string cached;
			if (TranslationCache.TryGetByKey(key, out cached))
			{
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
			});
		}

		private static void Loop()
		{
			while (running)
			{
				try
				{
					if (Paused) { Thread.Sleep(500); continue; }

					var batch = DrainBatch();
					if (batch == null) { Thread.Sleep(400); continue; }

					ProcessBatch(batch);

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
					Thread.Sleep(1000);
				}
			}
		}

		/// <summary>Набирает батч с одинаковым context.</summary>
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
				if (j.Context == first.Context) batch.Add(j);
				else postpone.Add(j);
				if (postpone.Count > size) break;
			}
			foreach (var j in postpone) queue.Enqueue(j);
			return batch;
		}

		private static void ProcessBatch(List<TranslateJob> batch)
		{
			var s = GATMod.Settings;
			var items = new Dictionary<string, string>(batch.Count);
			for (int i = 0; i < batch.Count; i++)
				items[i.ToString()] = batch[i].Source;

			var result = LlmClient.TranslateBatch(s, batch[0].Context, items);

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
				Thread.Sleep(delay);
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
					quarantine[job.Key] = 1;
					continue;
				}

				// Модель вернула строку без перевода (имя мода, DLC, технический идентификатор).
				// Правило 9 промпта разрешает это. Кэшируем ОРИГИНАЛ, а не ответ модели:
				// при context=label она опускает регистр, и "Core" превратилось бы в "core".
				if (!string.IsNullOrEmpty(dst) && string.Equals(dst.Trim(), job.Source.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					TranslationCache.Put(job.Context, job.Source, job.Source);
					untouched++;
					continue;
				}

				string reason;
				if (!PlaceholderGuard.Validate(job.Source, dst, out reason))
				{
					bad++;
					quarantine[job.Key] = 1;
					if (s.verboseLogging)
						GATLog.Warn("PlaceholderGuard fail (" + reason + ")\n  SRC: " + job.Source + "\n  DST: " + dst);
					continue;
				}

				TranslationCache.Put(job.Context, job.Source, dst);
				Interlocked.Increment(ref TranslatedThisSession);
				ok++;
				if (job.OnDone != null)
				{
					try { job.OnDone(dst); } catch { }
				}
			}

			if (s.verboseLogging)
				GATLog.Msg("Батч [" + batch[0].Context + "]: принято " + ok + ", без перевода " + untouched +
				           ", отброшено " + bad + ", в очереди " + queue.Count);
		}

		public static void ClearQuarantine() { quarantine.Clear(); }
	}
}
