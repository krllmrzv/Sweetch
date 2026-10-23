using System.Text;
using static Sweetch.Core.Loc;

namespace Sweetch.Core.Backends;

/// <summary>
/// Contacts (.vcf, vCard 2.1–4.0) ↔ a table with one row per person. Reads phone exports in old Android
/// style too: quoted-printable values in any charset, grouped properties ("item1.TEL").
/// </summary>
public static class VCard
{
    // Column, vCard property, and words that find the column in someone else's table (Google, Outlook, Russian).
    private static readonly (string Column, string Property, string[] Words)[] Fields =
    [
        ("Name", "FN", ["name", "fn", "имя", "фио"]),
        ("Last name", "N.0", ["last", "family", "surname", "фамилия"]),
        ("First name", "N.1", ["first", "given", "имя"]),
        ("Phone", "TEL", ["phone", "tel", "mobile", "телефон"]),
        ("Email", "EMAIL", ["mail", "почта"]),
        ("Organization", "ORG", ["organization", "company", "org", "организация", "компания"]),
        ("Title", "TITLE", ["title", "job", "должность"]),
        ("Address", "ADR", ["address", "адрес"]),
        ("Birthday", "BDAY", ["birthday", "bday", "рождения"]),
        ("URL", "URL", ["url", "website", "web", "сайт"]),
        ("Note", "NOTE", ["note", "заметк", "примечани"]),
    ];

    public static List<string[]> ToRows(string text)
    {
        var cards = new List<Dictionary<string, List<string>>>();
        Dictionary<string, List<string>>? card = null;
        foreach (var line in Unfold(text))
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
                continue;
            var head = line[..colon].Split(';');
            var name = head[0][(head[0].LastIndexOf('.') + 1)..].ToUpperInvariant(); // drop "item1."
            var value = line[(colon + 1)..];
            if (name == "BEGIN")
                card = [];
            else if (name == "END" && card is not null)
                cards.Add(card);
            else if (card is not null && (name == "N" || Fields.Any(f => f.Property == name)))
            {
                var parameters = head.Skip(1).Select(p => p.ToUpperInvariant()).ToList();
                if (parameters.Any(p => p.Contains("QUOTED-PRINTABLE")))
                {
                    var charset = parameters.FirstOrDefault(p => p.StartsWith("CHARSET=", StringComparison.Ordinal))?[8..] ?? "UTF-8";
                    value = QuotedPrintable(value, charset);
                }
                var parts = SplitUnescaped(value, ';');
                if (name == "N")
                {
                    Add(card, "N.0", parts.ElementAtOrDefault(0));
                    Add(card, "N.1", parts.ElementAtOrDefault(1));
                }
                else
                {
                    Add(card, name, string.Join(name == "ADR" ? ", " : " ", parts.Where(p => p.Length > 0)));
                }
            }
        }
        // Only the columns somebody filled in.
        var used = Fields.Where(f => cards.Any(c => c.ContainsKey(f.Property))).ToList();
        return [[.. used.Select(f => f.Column)], .. cards.Select(c => used.Select(f => string.Join("; ", c.GetValueOrDefault(f.Property) ?? [])).ToArray())];
    }

    private static void Add(Dictionary<string, List<string>> card, string property, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (!card.TryGetValue(property, out var list))
            card[property] = list = [];
        list.Add(value.Trim());
    }

    /// <summary>Joins folded lines (a leading space continues the line) and quoted-printable soft breaks ("=" at the end).</summary>
    private static IEnumerable<string> Unfold(string text)
    {
        var current = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && raw[0] is ' ' or '\t' && current.Length > 0)
            {
                current.Append(raw, 1, raw.Length - 1);
                continue;
            }
            if (current.Length > 0 && current[^1] == '=' && current.ToString().Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
            {
                current.Length--; // the soft break
                current.Append(raw);
                continue;
            }
            if (current.Length > 0)
                yield return current.ToString();
            current.Clear().Append(raw);
        }
        if (current.Length > 0)
            yield return current.ToString();
    }

    private static string QuotedPrintable(string value, string charset)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
            }
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            encoding = Encoding.UTF8;
        }
        return encoding.GetString([.. bytes]);
    }

    /// <summary>Splits on a separator that isn't escaped, then undoes \, \; \n and \\.</summary>
    private static List<string> SplitUnescaped(string value, char separator)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else if (value[i] == separator)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        parts.Add(sb.ToString());
        return parts;
    }

    /// <summary>A table with a header row → vCard 3.0. Columns are matched by name in English or Russian.</summary>
    public static string FromRows(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0)
            throw new ConversionException(T("The table is empty.", "Таблица пустая."));
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();
        // A column goes to the field it names exactly, else the first whose words it contains; the full name is
        // tried last, so "First name" isn't taken for it. Google's "Phone 1 - Type" columns hold labels, not values.
        var byWords = Fields.OrderBy(f => f.Property == "FN").ToList();
        var map = header.Select(h => h.Contains("type") || h.Contains("label") || h.Contains("тип") ? null
            : Fields.FirstOrDefault(f => h == f.Column.ToLowerInvariant()).Property
              ?? byWords.FirstOrDefault(f => f.Words.Any(w => h.Contains(w, StringComparison.Ordinal))).Property).ToArray();
        if (!map.Any(p => p is "FN" or "N.0" or "N.1" or "TEL" or "EMAIL"))
            throw new ConversionException(T("The table has no name, phone or email column.", "В таблице нет колонок с именем, телефоном или почтой."));
        var sb = new StringBuilder();
        foreach (var row in rows.Skip(1))
        {
            var values = new Dictionary<string, List<string>>();
            for (var c = 0; c < map.Length && c < row.Length; c++)
            {
                if (map[c] is { } property && !string.IsNullOrWhiteSpace(row[c]))
                {
                    // One cell may hold several phones or emails: "+7 900…; +7 901…" or Google's " ::: ".
                    var many = property is "TEL" or "EMAIL" ? row[c].Split([";", ":::"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [row[c].Trim()];
                    foreach (var v in many)
                        Add(values, property, v);
                }
            }
            if (values.Count == 0)
                continue;
            string One(string p) => values.TryGetValue(p, out var l) ? l[0] : "";
            var full = One("FN").Length > 0 ? One("FN") : $"{One("N.1")} {One("N.0")}".Trim();
            if (full.Length == 0)
                full = One("TEL").Length > 0 ? One("TEL") : One("EMAIL");
            sb.Append("BEGIN:VCARD\r\nVERSION:3.0\r\n");
            sb.Append("FN:").Append(Escape(full)).Append("\r\n");
            sb.Append("N:").Append(Escape(One("N.0"))).Append(';').Append(Escape(One("N.1"))).Append(";;;\r\n");
            foreach (var (property, list) in values.Where(kv => kv.Key is not ("FN" or "N.0" or "N.1")))
            {
                foreach (var v in list)
                    sb.Append(property).Append(':').Append(property == "ADR" ? ";;" + Escape(v) + ";;;;" : Escape(v)).Append("\r\n");
            }
            sb.Append("END:VCARD\r\n");
        }
        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace(",", "\\,").Replace(";", "\\;").Replace("\r\n", "\\n").Replace("\n", "\\n");
}
