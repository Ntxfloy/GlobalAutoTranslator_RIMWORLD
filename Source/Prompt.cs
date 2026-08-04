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
		public const string System =
			"Ты переводчик игровой локализации RimWorld на русский язык.\n" +
			"0. Исходный язык ЛЮБОЙ: английский, китайский, японский, корейский, немецкий, " +
			"французский, испанский или любой другой. Определяй язык каждой строки сам. " +
			"Строки в одном запросе могут быть на разных языках. Результат ВСЕГДА на русском.\n" +
			"1. Переводи только значения, ключи не меняй.\n" +
			"2. Плейсхолдеры {PAWN_labelShort}, {0} и теги <color=#FF0000>, \\n копируй символ в символ.\n" +
			"3. ЗАПРЕЩЕНО создавать НОВЫЕ гендерные конструкции вида {X_gender ? a : b}. Но если такая конструкция УЖЕ ЕСТЬ в исходнике, ОБЯЗАТЕЛЬНО СОХРАНИ её формат и переведи оба варианта внутри (напал : напала), не выбрасывая её.\n" +
			"4. Род неизвестен: пиши окончание в скобках — повержен(а), готов(а).\n" +
			"5. Если после плейсхолдера нужен падеж, ставь двоеточие: " +
			"{PAWN} has been downed by {0} -> {PAWN} повержен(а). Причина: {0}\n" +
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
			"НИКОГДА не используй полноширинные знаки （） ｛｝ ， 。 「」 — движок игры их не понимает.\n";

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

		/// <summary>Собирает user-сообщение: {"context":..,"glossary":{..},"items":{..}}</summary>
		public static string BuildUserMessage(string context, Dictionary<string, string> items)
		{
			var sb = new StringBuilder(512);
			sb.Append("{\"context\":\"").Append(MiniJson.Escape(context)).Append("\",");

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
