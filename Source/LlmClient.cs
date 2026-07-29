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
		private static int lastErrorLogTick;

		/// <summary>
		/// Отправляет батч на перевод. Возвращает карту id -> перевод или null при сбое.
		/// </summary>
		public static Dictionary<string, string> TranslateBatch(
			GATSettings s, string context, Dictionary<string, string> items)
		{
			if (items == null || items.Count == 0) return new Dictionary<string, string>();

			string body = BuildRequestBody(s, context, items);

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
					if (parsed.Count == 0)
					{
						GATLog.Warn("Не удалось разобрать JSON модели: " + Trim(content, 800));
						return null;
					}

					var normalized = new Dictionary<string, string>(parsed.Count, StringComparer.Ordinal);
					foreach (var kv in parsed)
					{
						normalized[kv.Key] = PlaceholderGuard.NormalizeFullwidth(kv.Value);
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

		private static string BuildRequestBody(GATSettings s, string context, Dictionary<string, string> items)
		{
			var sb = new StringBuilder(2048);
			sb.Append('{');
			sb.Append("\"model\":\"").Append(MiniJson.Escape(s.model)).Append("\",");
			sb.Append("\"temperature\":0,");
			sb.Append("\"stream\":false,");
			if (s.sendReasoningEffortNone) sb.Append("\"reasoning_effort\":\"none\",");
			if (s.requestJsonObject) sb.Append("\"response_format\":{\"type\":\"json_object\"},");
			sb.Append("\"messages\":[");
			sb.Append("{\"role\":\"system\",\"content\":\"").Append(MiniJson.Escape(Prompt.System)).Append("\"},");
			sb.Append("{\"role\":\"user\",\"content\":\"")
			  .Append(MiniJson.Escape(Prompt.BuildUserMessage(context, items)))
			  .Append("\"}");
			sb.Append("]}");
			return sb.ToString();
		}

		private static string Post(GATSettings s, string body)
		{
			var req = (HttpWebRequest)WebRequest.Create(s.endpoint);
			req.Method = "POST";
			req.ContentType = "application/json; charset=utf-8";
			req.Timeout = Math.Max(15, s.timeoutSeconds) * 1000;
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
