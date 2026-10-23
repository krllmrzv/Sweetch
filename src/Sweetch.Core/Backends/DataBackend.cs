using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using static Sweetch.Core.Loc;

namespace Sweetch.Core.Backends;

/// <summary>
/// JSON, JSON Lines, YAML, TOML and XML into each other through one tree (System.Text.Json nodes), plus
/// tables (CSV/XLSX) from lists of records and back, and vCard contacts ↔ CSV.
/// </summary>
public static class DataBackend
{
    public static JsonNode? Read(string path, string format)
    {
        var text = SubtitleBackend.ReadText(path);
        try
        {
            return format switch
            {
                "json" => JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }),
                "jsonl" => new JsonArray([.. text.Split('\n').Where(l => l.Trim().Length > 0).Select(l => JsonNode.Parse(l))]),
                "yaml" => FromYaml(text),
                "toml" => FromToml(TomlSerializer.Deserialize<TomlTable>(text)!),
                "xml" => FromXml(XDocument.Parse(text).Root!),
                _ => throw new ConversionException(T($"Unsupported data format: {format}", $"Неподдерживаемый формат данных: {format}")),
            };
        }
        catch (Exception ex) when (ex is JsonException or YamlException or TomlException or XmlException)
        {
            throw new ConversionException(T($"The {Formats.Display(format)} file has an error: {ex.Message}", $"В файле {Formats.Display(format)} ошибка: {ex.Message}"), ex);
        }
    }

    public static string Write(JsonNode? node, string format) => format switch
    {
        "json" => Json(node, indented: true) + "\n",
        "jsonl" => node is JsonArray list ? string.Concat(list.Select(n => Json(n, indented: false) + "\n")) : Json(node, indented: false) + "\n",
        "yaml" => ToYaml(node),
        "toml" => TomlSerializer.Serialize(ToToml(node)),
        "xml" => ToXml(node),
        _ => throw new ConversionException(T($"Unsupported data format: {format}", $"Неподдерживаемый формат данных: {format}")),
    };

    public static Task<string> ConvertAsync(FileItem item, string format, OperationContext ctx) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp => Task.Run(() =>
            File.WriteAllText(tmp, Write(Read(item.Path, item.Format), format), new UTF8Encoding(false)), ctx.CancellationToken));

    // ---------------------------------------------------------------- JSON

    private static string Json(JsonNode? node, bool indented)
    {
        using var stream = new MemoryStream();
        // Relaxed escaping keeps Cyrillic and other letters readable instead of ж.
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            if (node is null)
                writer.WriteNullValue();
            else
                node.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A plain value as the most specific JSON type: number, bool or string.</summary>
    private static JsonNode? Scalar(object? value) => value switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        long l => JsonValue.Create(l),
        int i => JsonValue.Create(i),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create((double)f),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    private static object? Primitive(JsonValue value) => value.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetValue<long>(out var l) => l,
        JsonValueKind.Number => value.GetValue<double>(),
        JsonValueKind.Null => null,
        _ => value.ToString(),
    };

    // ---------------------------------------------------------------- YAML

    private static JsonNode? FromYaml(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count == 1)
            return FromYaml(stream.Documents[0].RootNode);
        return new JsonArray([.. stream.Documents.Select(d => FromYaml(d.RootNode))]);
    }

    private static JsonNode? FromYaml(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(kv => KeyValuePair.Create(((YamlScalarNode)kv.Key).Value ?? "", FromYaml(kv.Value)))),
        YamlSequenceNode list => new JsonArray([.. list.Children.Select(FromYaml)]),
        YamlScalarNode { Style: ScalarStyle.Plain } s => Scalar(Typed(s.Value)),
        YamlScalarNode s => JsonValue.Create(s.Value),
        _ => null,
    };

    /// <summary>YAML 1.2 core schema for unquoted values: null, true/false, integers and decimals.</summary>
    private static object? Typed(string? value) => value switch
    {
        null or "" or "~" or "null" or "Null" or "NULL" => null,
        "true" or "True" or "TRUE" => true,
        "false" or "False" or "FALSE" => false,
        _ when long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l) => l,
        _ when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !value.StartsWith('.') => d,
        _ => value,
    };

    private static string ToYaml(JsonNode? node)
    {
        var writer = new StringWriter { NewLine = "\n" };
        new YamlStream(new YamlDocument(ToYamlNode(node))).Save(writer, assignAnchors: false);
        // YamlStream ends every document with "..."; one document doesn't need it.
        return writer.ToString().TrimEnd().TrimEnd('.').TrimEnd() + "\n";
    }

    private static YamlNode ToYamlNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var map = new YamlMappingNode();
                foreach (var (key, value) in obj)
                    map.Add(new YamlScalarNode(key), ToYamlNode(value));
                return map;
            case JsonArray list:
                return new YamlSequenceNode(list.Select(ToYamlNode));
            case JsonValue value when Primitive(value) is string s:
                // A string that would read back as a number, bool or null gets quotes; long text a block.
                return new YamlScalarNode(s)
                {
                    Style = s.Contains('\n') ? ScalarStyle.Literal : Typed(s) is not string || s != s.Trim() ? ScalarStyle.DoubleQuoted : ScalarStyle.Any,
                };
            case JsonValue value:
                return new YamlScalarNode(value.GetValueKind() == JsonValueKind.Null ? "null" : value.ToString());
            default:
                return new YamlScalarNode("null");
        }
    }

    // ---------------------------------------------------------------- TOML

    private static JsonNode? FromToml(object? value) => value switch
    {
        TomlTable table => new JsonObject(table.Select(kv => KeyValuePair.Create(kv.Key, FromToml(kv.Value)))),
        TomlTableArray tables => new JsonArray([.. tables.Select(FromToml)]),
        TomlArray list => new JsonArray([.. list.Select(FromToml)]),
        _ => Scalar(value),
    };

    /// <summary>TOML's top level is always a table and it has no null: a bare list goes under "items", nulls are left out.</summary>
    private static TomlTable ToToml(JsonNode? node) =>
        node is JsonObject obj ? Table(obj) : new TomlTable { ["items"] = TomlValue(node) ?? new TomlArray() };

    private static TomlTable Table(JsonObject obj)
    {
        var table = new TomlTable();
        foreach (var (key, value) in obj)
        {
            if (TomlValue(value) is { } v)
                table[key] = v;
        }
        return table;
    }

    private static object? TomlValue(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                return Table(obj);
            case JsonArray list when list.Count > 0 && list.All(n => n is JsonObject):
                var tables = new TomlTableArray();
                foreach (var n in list)
                    tables.Add(Table((JsonObject)n!));
                return tables;
            case JsonArray list:
                var array = new TomlArray();
                foreach (var n in list)
                {
                    if (TomlValue(n) is { } v)
                        array.Add(v);
                }
                return array;
            case JsonValue value:
                return Primitive(value);
            default:
                return null;
        }
    }

    // ---------------------------------------------------------------- XML

    /// <summary>
    /// &lt;a x="1"&gt;&lt;b&gt;2&lt;/b&gt;&lt;b&gt;3&lt;/b&gt;&lt;/a&gt; → {"a": {"@x": "1", "b": ["2", "3"]}}: attributes get "@",
    /// repeated elements become a list, text beside child elements goes to "#text".
    /// </summary>
    private static JsonNode FromXml(XElement root) => new JsonObject { [XmlConvert.DecodeName(root.Name.LocalName)] = XmlValue(root) };

    private static JsonNode? XmlValue(XElement element)
    {
        var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
        if (attributes.Count == 0 && !element.HasElements)
            return element.Value.Length == 0 ? null : JsonValue.Create(element.Value);
        var obj = new JsonObject();
        foreach (var a in attributes)
            obj["@" + XmlConvert.DecodeName(a.Name.LocalName)] = a.Value;
        foreach (var group in element.Elements().GroupBy(e => XmlConvert.DecodeName(e.Name.LocalName)))
        {
            var values = group.Select(XmlValue).ToList();
            obj[group.Key] = values.Count == 1 ? values[0] : new JsonArray([.. values]);
        }
        var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (text.Length > 0)
            obj["#text"] = text;
        return obj;
    }

    private static string ToXml(JsonNode? node)
    {
        // An object with one key names the root element (what FromXml produces); anything else goes under <root>.
        var root = node is JsonObject { Count: 1 } single && single.First().Value is not JsonArray
            ? XmlElement(single.First().Key, single.First().Value)
            : XmlElement("root", node);
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineChars = "\n" };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
            new XDocument(root).Save(writer);
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static XElement XmlElement(string name, JsonNode? node)
    {
        var element = new XElement(XmlConvert.EncodeLocalName(name.Length == 0 ? "item" : name));
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key.StartsWith('@') && value is JsonValue attr)
                        element.SetAttributeValue(XmlConvert.EncodeLocalName(key[1..]), attr.ToString());
                    else if (key == "#text")
                        element.Add(value?.ToString());
                    else if (value is JsonArray items)
                        element.Add(items.Select(i => XmlElement(key, i)));
                    else
                        element.Add(XmlElement(key, value));
                }
                break;
            case JsonArray list:
                element.Add(list.Select(i => XmlElement("item", i)));
                break;
            case JsonValue value:
                element.Value = value.ToString();
                break;
        }
        return element;
    }

    // ---------------------------------------------------------------- tables

    /// <summary>
    /// The records in a document: the list itself, else the shallowest list of objects inside it (an API's
    /// "data" or "items"), else the whole thing as one row. Nested objects become dotted columns ("address.city").
    /// </summary>
    public static List<string[]> ToRows(JsonNode? node)
    {
        List<JsonNode?> records = node is JsonArray top ? [.. top] : [node];
        var level = new Queue<JsonNode?>([node]);
        while (node is not JsonArray && level.TryDequeue(out var next))
        {
            if (next is JsonArray { Count: > 0 } list && list.All(n => n is JsonObject))
            {
                records = [.. list];
                break;
            }
            foreach (var child in next switch { JsonObject o => o.Select(kv => kv.Value), JsonArray a => a, _ => [] })
                level.Enqueue(child);
        }
        var columns = new List<string>();
        var rows = new List<Dictionary<string, string>>();
        foreach (var record in records)
        {
            var row = new Dictionary<string, string>();
            Flatten(record, "", row);
            foreach (var key in row.Keys.Where(k => !columns.Contains(k)))
                columns.Add(key);
            rows.Add(row);
        }
        return [[.. columns], .. rows.Select(r => columns.Select(c => r.GetValueOrDefault(c, "")).ToArray())];
    }

    private static void Flatten(JsonNode? node, string prefix, Dictionary<string, string> row)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj)
                Flatten(value, prefix.Length == 0 ? key : prefix + "." + key, row);
        }
        else
        {
            var name = prefix.Length == 0 ? "value" : prefix;
            row[name] = node switch
            {
                null => "",
                JsonArray list when list.All(n => n is JsonValue) => string.Join("; ", list.Select(n => n?.ToString())),
                JsonArray list => Json(list, indented: false),
                _ => node.ToString(),
            };
        }
    }

    /// <summary>Rows with a header line → list of objects; "a.b" headers nest again, numbers stay numbers.</summary>
    public static JsonArray FromRows(IReadOnlyList<string[]> rows)
    {
        var result = new JsonArray();
        if (rows.Count == 0)
            return result;
        var header = rows[0];
        foreach (var row in rows.Skip(1))
        {
            var obj = new JsonObject();
            for (var c = 0; c < header.Length && c < row.Length; c++)
            {
                if (string.IsNullOrEmpty(row[c]) || string.IsNullOrWhiteSpace(header[c]))
                    continue;
                var path = header[c].Split('.');
                var target = obj;
                foreach (var part in path[..^1])
                    target = target[part] as JsonObject ?? (JsonObject)(target[part] = new JsonObject());
                target[path[^1]] = Cell(row[c]);
            }
            result.Add(obj);
        }
        return result;
    }

    private static JsonNode Cell(string value)
    {
        var keepAsText = value.Length > 1 && value[0] == '0' && char.IsDigit(value[1]); // phone numbers, ZIP codes
        if (!keepAsText && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
            return JsonValue.Create(l);
        if (!keepAsText && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && value.Length < 16)
            return JsonValue.Create(d);
        return JsonValue.Create(value);
    }

    public static async Task<string> ToTableAsync(FileItem item, string format, OperationContext ctx)
    {
        var rows = await Task.Run(() => item.Format == "vcf" ? VCard.ToRows(SubtitleBackend.ReadText(item.Path)) : ToRows(Read(item.Path, item.Format)), ctx.CancellationToken).ConfigureAwait(false);
        return await WriteTableAsync(item, rows, format, ctx).ConfigureAwait(false);
    }

    public static Task<string> WriteTableAsync(FileItem item, List<string[]> rows, string format, OperationContext ctx) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp => Task.Run(() =>
        {
            if (format == "xlsx")
                SpreadsheetBackend.WriteXlsx([new SpreadsheetBackend.Sheet(item.BaseName, rows)], tmp);
            else // BOM so Excel opens UTF-8 CSV with the right letters
                File.WriteAllText(tmp, SpreadsheetBackend.ToDelimited(rows, ','), new UTF8Encoding(true));
        }, ctx.CancellationToken));

    /// <summary>A spreadsheet as JSON: a list of rows, or {sheet: rows} when several sheets have data.</summary>
    public static async Task<string> FromTableAsync(FileItem item, string format, OperationContext ctx)
    {
        var sheets = (await Task.Run(() => SpreadsheetBackend.Read(item.Path), ctx.CancellationToken).ConfigureAwait(false)).Where(s => s.Rows.Count > 0).ToList();
        if (format == "vcf")
        {
            var cards = VCard.FromRows(sheets.FirstOrDefault()?.Rows ?? []);
            return await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, "vcf", tmp =>
                File.WriteAllTextAsync(tmp, cards, new UTF8Encoding(false), ctx.CancellationToken)).ConfigureAwait(false);
        }
        JsonNode node = sheets.Count == 1 ? FromRows(sheets[0].Rows) : new JsonObject(sheets.Select(s => KeyValuePair.Create(s.Name, (JsonNode?)FromRows(s.Rows))));
        return await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp =>
            File.WriteAllTextAsync(tmp, Write(node, format), new UTF8Encoding(false), ctx.CancellationToken)).ConfigureAwait(false);
    }
}
