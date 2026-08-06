using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace GlobalAutoTranslator
{
	/// <summary>
	/// Клиент к OpenAI-совместимому эндпоинту (CLIProxyAPI, Ollama, LM Studio, llama.cpp).
	/// Сознательно используется HttpWebRequest, а не HttpClient: Mono в RimWorld старый,
	/// и HttpWebRequest ведёт себя предсказуемее без дополнительных сборок.
	/// Все вызовы идут СТРОГО не из главного потока Unity.
	/// </summary>
	public static class LlmClient
	{
		public struct ProbeResult
		{
			public bool Success;
			public int HttpCode;
			public string Error;
			public int? RetryAfterSeconds;
			public string ResponsePreview;
		}

		/// <summary>
		/// Отправляет батч на перевод. Возвращает карту id -> перевод или null при сбое.
		/// </summary>
		public static Dictionary<string, string> TranslateBatch(
			GATSettings s, string context, Dictionary<string, string> items, bool isRetry = false, Dictionary<string, string> retryHints = null)
		{
			if (items == null || items.Count == 0) return new Dictionary<string, string>();

			// Маскируем плейсхолдеры: {PAWN_labelShort} → ⟦1⟧, [PAWN_pronoun] → ⟦2⟧ и т.д.
			var maskedItems = new Dictionary<string, string>(items.Count, StringComparer.Ordinal);
			var markerMaps  = new Dictionary<string, Dictionary<int, string>>(items.Count, StringComparer.Ordinal);
			foreach (var kv in items)
			{
				Dictionary<int, string> map;
				maskedItems[kv.Key] = PlaceholderGuard.MaskPlaceholders(kv.Value, out map);
				if (map.Count > 0) markerMaps[kv.Key] = map;
			}

			float temperature = isRetry ? 0.3f : 0f;
			var requiredMarkers = isRetry ? markerMaps : null;
			string body = BuildRequestBody(s, context, maskedItems, temperature, requiredMarkers, retryHints);

			for (int attempt = 1; attempt <= 3; attempt++)
			{
				try
				{
					string raw = Post(s, body);
					if (raw == null) return null;

					// Берём СТРОГО message.content. reasoning_content — это мысли модели,
					// его нельзя парсить как результат.
					string content = MiniJson.ExtractStringField(raw, "content");
					if (string.IsNullOrEmpty(content))
					{
						GATLog.Warn("Пустой content в ответе. Сырой ответ: " + Trim(raw, 800));
						return null;
					}

					var parsed = MiniJson.ParseFlatObject(content);
					if (parsed == null || parsed.Count == 0)
					{
						GATLog.Warn("Не удалось разобрать JSON модели: " + Trim(content, 800));
						return null;
					}

					var normalized = new Dictionary<string, string>(parsed.Count, StringComparer.Ordinal);
					foreach (var kv in parsed)
					{
						string val = PlaceholderGuard.NormalizeFullwidth(kv.Value);
						// Диагностический лог: валидация маркеров ПЕРЕД обратной подстановкой
						Dictionary<int, string> map;
						if (markerMaps.TryGetValue(kv.Key, out map) && map.Count > 0)
						{
							string markerReason;
							if (!PlaceholderGuard.ValidateMarkers(val, map, out markerReason))
							{
								if (s.verboseLogging)
									GATLog.Warn("ValidateMarkers fail (" + markerReason + ") key=" + kv.Key);
							}
							val = PlaceholderGuard.UnmaskPlaceholders(val, map);
						}
						normalized[kv.Key] = val;
					}
					return normalized;
				}
				catch (WebException we)
				{
					int code = 0;
					string detail = we.Message;
					var resp = we.Response as HttpWebResponse;
					if (resp != null)
					{
						code = (int)resp.StatusCode;
						try
						{
							using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
								detail = Trim(sr.ReadToEnd(), 500);
						}
						catch { }
					}

					bool retriable = code == 429 || code == 408 || code >= 500 || code == 0;
					GATLog.Warn("HTTP " + code + " (попытка " + attempt + "/3): " + detail);

					if (!retriable || attempt == 3) return null;
					Thread.Sleep(1500 * attempt * attempt); // 1.5s, 6s
				}
				catch (Exception e)
				{
					GATLog.Warn("Ошибка запроса (попытка " + attempt + "/3): " + e.Message);
					if (attempt == 3) return null;
					Thread.Sleep(1500 * attempt);
				}
			}
			return null;
		}

		private static string BuildRequestBody(
			GATSettings s, string context, Dictionary<string, string> items,
			float temperature = 0f, Dictionary<string, Dictionary<int, string>> requiredMarkers = null,
			Dictionary<string, string> retryHints = null)
		{
			var sb = new StringBuilder(2048);
			sb.Append('{');
			sb.Append("\"model\":\"").Append(MiniJson.Escape(s.model)).Append("\",");
			sb.Append("\"temperature\":").Append(temperature.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(",");
			sb.Append("\"stream\":false,");
			if (s.sendReasoningEffortNone) sb.Append("\"reasoning_effort\":\"none\",");
			if (s.requestJsonObject) sb.Append("\"response_format\":{\"type\":\"json_object\"},");
			sb.Append("\"messages\":[");
			sb.Append("{\"role\":\"system\",\"content\":\"").Append(MiniJson.Escape(Prompt.System)).Append("\"},");
			sb.Append("{\"role\":\"user\",\"content\":\"")
			  .Append(MiniJson.Escape(Prompt.BuildUserMessage(context, items, requiredMarkers, retryHints)))
			  .Append("\"}");
			sb.Append("]}");
			return sb.ToString();
		}

		private static string Post(GATSettings s, string body, int timeoutOverrideSeconds = 0)
		{
			var req = (HttpWebRequest)WebRequest.Create(s.endpoint);
			req.Method = "POST";
			req.ContentType = "application/json; charset=utf-8";
			req.Timeout = (timeoutOverrideSeconds > 0 ? timeoutOverrideSeconds : Math.Max(15, s.timeoutSeconds)) * 1000;
			req.ReadWriteTimeout = req.Timeout;
			req.KeepAlive = true;
			req.Proxy = null; // не тащимся через системный прокси к localhost
			if (!string.IsNullOrEmpty(s.apiKey))
				req.Headers["Authorization"] = "Bearer " + s.apiKey;

			byte[] payload = Encoding.UTF8.GetBytes(body);
			req.ContentLength = payload.Length;
			using (var st = req.GetRequestStream()) st.Write(payload, 0, payload.Length);

			using (var resp = (HttpWebResponse)req.GetResponse())
			using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
				return sr.ReadToEnd();
		}

		public static int? ParseRetryAfterSeconds(string value, DateTime utcNow)
		{
			if (string.IsNullOrEmpty(value)) return null;
			int parsed;
			if (int.TryParse(value.Trim(), out parsed))
			{
				if (parsed > 0 && parsed <= 1800)
					return parsed;
			}
			return null;
		}

		public static ProbeResult Probe(GATSettings s)
		{
			var result = new ProbeResult { Success = false };
			string body = BuildProbeRequestBody(s);
			try
			{
				string raw = Post(s, body, 20);
				result.HttpCode = 200;
				
				string content = MiniJson.ExtractStringField(raw, "content");
				if (string.IsNullOrEmpty(content))
				{
					result.Error = "Empty content";
				}
				else
				{
					result.Success = true;
					result.ResponsePreview = Trim(content, 100);
				}
			}
			catch (WebException we)
			{
				var resp = we.Response as HttpWebResponse;
				if (resp != null)
				{
					result.HttpCode = (int)resp.StatusCode;
					if (result.HttpCode == 429)
					{
						string retryAfterStr = resp.Headers["Retry-After"];
						result.RetryAfterSeconds = ParseRetryAfterSeconds(retryAfterStr, DateTime.UtcNow);
					}
					try
					{
						using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
							result.Error = Trim(sr.ReadToEnd(), 500);
					}
					catch { result.Error = we.Message; }
				}
				else
				{
					result.Error = we.Message;
				}
			}
			catch (Exception e)
			{
				result.Error = e.Message;
			}
			return result;
		}

		private static string BuildProbeRequestBody(GATSettings s)
		{
			var sb = new StringBuilder(256);
			sb.Append('{');
			sb.Append("\"model\":\"").Append(MiniJson.Escape(s.model)).Append("\",");
			sb.Append("\"stream\":false,");
			if (s.sendReasoningEffortNone) sb.Append("\"reasoning_effort\":\"none\",");
			sb.Append("\"messages\":[{\"role\":\"user\",\"content\":\"Reply only OK\"}]}");
			return sb.ToString();
		}

		/// <summary>Проверка связи для кнопки в настройках. Запускать в отдельном потоке.</summary>
		public static string SelfTest(GATSettings s)
		{
			var probe = new Dictionary<string, string>
			{
				{ "1", "steel longsword" },
				{ "2", "{PAWN_labelShort} has been downed by {0}." },
				{ "3", "<color=#FF0000>Critical</color> failure" },
				{ "4", "Muffalo wool parka" },
				{ "5", "钢制长剑" },
				{ "6", "{PAWN_labelShort}被{0}击倒了。" },
				{ "7", "ムファロの毛皮のパーカ" },
				{ "8", "Stahllangschwert" },
				{ "9", "{PREDATOR} {PREDATOR_gender ? attacked : attacked} {PREY_labelShort}" }
			};
			var res = TranslateBatch(s, "label", probe);
			if (res == null) return "ОШИБКА: нет ответа. Смотри лог игры (Ctrl+F12 — окно ошибок).";

			var sb = new StringBuilder();
			foreach (var kv in probe)
			{
				string got;
				res.TryGetValue(kv.Key, out got);
				string reason = null;
				bool ok = got != null && PlaceholderGuard.Validate(kv.Value, got, out reason);
				if (!ok) reason = reason ?? "нет ключа в ответе";
				sb.Append(ok ? "[OK] " : "[FAIL] ").Append(got ?? "(null)");
				if (!ok) sb.Append("  <- ").Append(reason);
				sb.Append('\n');
			}
			return sb.ToString();
		}

		private static string Trim(string s, int max)
		{
			if (string.IsNullOrEmpty(s)) return string.Empty;
			return s.Length <= max ? s : s.Substring(0, max) + "...";
		}
	}
}
