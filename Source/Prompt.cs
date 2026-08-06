using System;
using System.Collections.Generic;
using System.Text;

namespace GlobalAutoTranslator
{
	/// <summary>
	/// Сборка системного и пользовательского сообщения.
	/// Правила выверены на живых тестах: без п. 3 модель изобретает тернарники
	/// вида {PAWN_gender ? повержен : повержена}, которых движок не понимает.
	/// </summary>
	public static class Prompt
	{
		public const string PromptVersion = "27.0";

		public const string System =
			"Ты переводчик игровой локализации RimWorld на русский язык.\n" +
			"0. Исходный язык ЛЮБОЙ: английский, китайский, японский, корейский, немецкий, " +
			"французский, испанский или любой другой. Определяй язык каждой строки сам. " +
			"Строки в одном запросе могут быть на разных языках. Результат ВСЕГДА на русском.\n" +
			"1. Переводи только значения, ключи не меняй.\n" +
			"2. Пронумерованные маркеры ⟦1⟧, ⟦2⟧, ⟦3⟧ и теги <color=#FF0000>, \\n — это подстановки движка. Перенеси ВСЕ маркеры в перевод, ровно по одному разу каждый. Не переводи и не изменяй их. Порядок маркеров в переводе может отличаться от исходника.\n" +
			"3. ЗАПРЕЩЕНО создавать НОВЫЕ гендерные конструкции вида {X_gender ? a : b}. Но если такая конструкция УЖЕ ЕСТЬ в исходнике, ОБЯЗАТЕЛЬНО СОХРАНИ её формат и переведи оба варианта внутри (напал : напала), не выбрасывая её. Число и порядок конструкций в ответе обязаны совпадать с исходником. Не добавляй ни одной новой.\n" +
			"4. Род неизвестен: пиши окончание в скобках — повержен(а), готов(а).\n" +
			"5. Если после маркера или подстановки нужен падеж, ставь двоеточие: ⟦1⟧ has been downed by ⟦2⟧ -> ⟦1⟧ повержен(а). Причина: ⟦2⟧\n" +
			"6. НЕ размышляй, НЕ перебирай варианты, НЕ объясняй. Сразу результат.\n" +
			"7. Стиль: сухой игровой интерфейс, без канцелярита и без отсебятины.\n" +
			"8. Для context=label все значения пиши СО СТРОЧНОЙ буквы (игра сама капитализирует). " +
			"Для context=title и context=ui сохраняй регистр как в оригинале. " +
			"Для context=description и context=keyed сохраняй регистр как в оригинале.\n" +
			"9. Если строка — технический идентификатор или не требует перевода, верни её без изменений.\n" +
			"10. Ответ: только JSON-объект {\"id\":\"перевод\"}, без markdown, без комментариев. " +
			"Ключи в ответе должны точно совпадать с ключами в items.\n" +
			"11. В ответе не должно остаться иероглифов, кандзи, каны, хангыля и других знаков исходного письма. " +
			"Если точное значение неясно, переводи по смыслу, но обязательно по-русски.\n" +
			"12. Не транслитерируй. 长剑 — это \"длинный меч\", а не \"чанцзянь\". " +
			"Транслитерация допустима только для имён собственных, названий фракций и вымышленных названий.\n" +
			"13. Пунктуация только русская и только ASCII-символами: круглые скобки ( ), кавычки, запятые, точки. " +
			"НИКОГДА не используй полноширинные знаки （） ｛｝ ， 。 「」 — движок игры их не понимает.\n" +
			"14. Если маркер или плейсхолдер встречается в исходнике несколько раз, в переводе он должен встретиться ровно столько же раз. НЕВЕРНО: «⟦1⟧ attacked ⟦2⟧ and ⟦1⟧» → «⟦1⟧ напал на ⟦2⟧». ВЕРНО: «⟦1⟧ attacked ⟦2⟧ and ⟦1⟧» → «⟦1⟧ напал на ⟦2⟧ и ⟦1⟧».\n" +
			"15. Если исходная строка уже содержит кириллические фрагменты, это готовые имена пешек, фракций, поселений, квестов, идеологий, предметов или ранее переведённые подстановки. Сохраняй их в переводе дословно, символ в символ. Не переводи повторно, не меняй регистр, не склоняй и не заменяй синонимами. Переведи окружающий текст на русский.\n" +
			"16. Если в запросе присутствует блок `retry_hints`, он содержит обязательные подсказки для конкретных ключей. Подсказка `retry_hints[id]` относится ТОЛЬКО к строке `items[id]` и обязательна для исполнения.\n";

		/// <summary>Глоссарий каноничных терминов RimWorld. Можно расширять.</summary>
		public static readonly Dictionary<string, string> Glossary = new Dictionary<string, string>
		{
			{ "pawn", "пешка" },
			{ "colonist", "колонист" },
			{ "colony", "колония" },
			{ "hediff", "состояние" },
			{ "mech", "механоид" },
			{ "mechanoid", "механоид" },
			{ "raid", "набег" },
			{ "downed", "повержен(а)" },
			{ "steel", "сталь" },
			{ "plasteel", "пласталь" },
			{ "component", "компонент" },
			{ "muffalo", "муффало" },
			{ "thrumbo", "трамбо" },
			{ "boomalope", "бумалопа" },
			{ "longsword", "длинный меч" },
			{ "parka", "парка" },
			{ "duster", "плащ" },
			{ "flak vest", "бронежилет" },
			{ "mood", "настроение" },
			{ "trait", "черта характера" },
			{ "skill", "навык" },
			{ "research", "исследование" },
			{ "quest", "задание" },
			{ "faction", "фракция" },
			{ "caravan", "караван" },
			{ "stockpile", "склад" },
			{ "bill", "заказ" },
			{ "blueprint", "чертёж" },
		};

		/// <summary>Собирает user-сообщение: {"context":..,"glossary":{..},"items":{..},"retry_hints":{..}}</summary>
		public static string BuildUserMessage(
			string context, Dictionary<string, string> items,
			Dictionary<string, Dictionary<int, string>> requiredMarkers = null,
			Dictionary<string, string> retryHints = null)
		{
			var sb = new StringBuilder(512);
			sb.Append("{\"context\":\"").Append(MiniJson.Escape(context)).Append("\",");

			if (retryHints != null && retryHints.Count > 0)
			{
				sb.Append("\"retry_hints\":{");
				bool firstH = true;
				foreach (var kv in retryHints)
				{
					if (string.IsNullOrEmpty(kv.Value)) continue;
					if (!firstH) sb.Append(',');
					firstH = false;
					sb.Append('"').Append(MiniJson.Escape(kv.Key)).Append("\":\"").Append(MiniJson.Escape(kv.Value)).Append('"');
				}
				sb.Append("},");
			}

			if (requiredMarkers != null && requiredMarkers.Count > 0)
			{
				sb.Append("\"required\":{");
				bool firstR = true;
				foreach (var kv in requiredMarkers)
				{
					if (kv.Value == null || kv.Value.Count == 0) continue;
					if (!firstR) sb.Append(',');
					firstR = false;
					var mList = new StringBuilder();
					foreach (int num in kv.Value.Keys)
						mList.Append(PlaceholderGuard.MarkerLeft).Append(num).Append(PlaceholderGuard.MarkerRight).Append(' ');
					sb.Append('"').Append(MiniJson.Escape(kv.Key)).Append("\":\"").Append(MiniJson.Escape(mList.ToString().TrimEnd())).Append('"');
				}
				sb.Append("},");
			}

			// В глоссарий кладём только термины, реально встретившиеся в батче —
			// иначе на каждый запрос жжём сотни лишних prompt-токенов.
			var joined = new StringBuilder();
			foreach (var v in items.Values) joined.Append(v).Append('\n');
			string hay = joined.ToString().ToLowerInvariant();

			sb.Append("\"glossary\":{");
			bool firstG = true;
			foreach (var kv in Glossary)
			{
				if (!hay.Contains(kv.Key)) continue;
				if (!firstG) sb.Append(',');
				firstG = false;
				sb.Append('"').Append(MiniJson.Escape(kv.Key)).Append("\":\"").Append(MiniJson.Escape(kv.Value)).Append('"');
			}
			sb.Append("},");

			sb.Append("\"items\":{");
			bool first = true;
			foreach (var kv in items)
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append('"').Append(MiniJson.Escape(kv.Key)).Append("\":\"").Append(MiniJson.Escape(kv.Value)).Append('"');
			}
			sb.Append("}}");
			return sb.ToString();
		}
	}
}
