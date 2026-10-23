using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using ExcelDataReader;
using static Sweetch.Core.Loc;

namespace Sweetch.Core.Backends;

/// <summary>XLSX/XLS/CSV/TSV conversions without Office (ClosedXML + ExcelDataReader).</summary>
public static class SpreadsheetBackend
{
    static SpreadsheetBackend()
    {
        // ExcelDataReader needs legacy code pages for old XLS files.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        LoadOptions.DefaultGraphicEngine = new FixedFontMetrics();
    }

    public sealed record Sheet(string Name, List<string[]> Rows);

    public static IReadOnlyList<Sheet> Read(string path)
    {
        var format = Formats.KeyOf(path);
        return format switch
        {
            "csv" or "tsv" => [new Sheet(Formats.BaseNameOf(path), ReadDelimited(path, format == "tsv" ? '\t' : null))],
            "xlsx" or "xlsm" => ReadXlsx(path),
            "xls" => ReadXls(path),
            _ => throw new ConversionException(T($"{format} spreadsheets need LibreOffice.", $"Для таблиц {format} требуется LibreOffice.")),
        };
    }

    private static List<Sheet> ReadXlsx(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var sheets = new List<Sheet>();
        foreach (var ws in workbook.Worksheets)
        {
            var rows = new List<string[]>();
            var used = ws.RangeUsed();
            if (used is not null)
            {
                var firstCol = used.FirstColumn().ColumnNumber();
                var lastCol = used.LastColumn().ColumnNumber();
                for (var r = 1; r <= used.LastRow().RowNumber(); r++)
                {
                    var row = new string[lastCol - firstCol + 1];
                    for (var c = firstCol; c <= lastCol; c++)
                        row[c - firstCol] = ws.Cell(r, c).GetFormattedString(CultureInfo.InvariantCulture);
                    rows.Add(row);
                }
            }
            sheets.Add(new Sheet(ws.Name, rows));
        }
        return sheets;
    }

    private static List<Sheet> ReadXls(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var sheets = new List<Sheet>();
        do
        {
            var rows = new List<string[]>();
            while (reader.Read())
            {
                var row = new string[reader.FieldCount];
                for (var c = 0; c < row.Length; c++)
                    row[c] = Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture) ?? "";
                rows.Add(row);
            }
            sheets.Add(new Sheet(reader.Name, rows));
        }
        while (reader.NextResult());
        return sheets;
    }

    /// <summary>RFC 4180 reader; guesses ',', ';' or tab when <paramref name="delimiter"/> is null.</summary>
    public static List<string[]> ReadDelimited(string path, char? delimiter = null)
    {
        var text = File.ReadAllText(path, DetectEncoding(path));
        return ParseDelimited(text, delimiter ?? GuessDelimiter(text));
    }

    private static Encoding DetectEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            // Older Czech Excel exports are Windows-1250.
            return Encoding.GetEncoding(1250);
        }
    }

    public static char GuessDelimiter(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var candidates = new[] { ',', ';', '\t', '|' };
        return candidates.OrderByDescending(c => firstLine.Count(ch => ch == c)).First();
    }

    public static List<string[]> ParseDelimited(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿')
            i = 1;
        for (; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
            }
            else
            {
                field.Append(ch);
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }

    public static string ToDelimited(IEnumerable<string[]> rows, char delimiter)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                if (c > 0)
                    sb.Append(delimiter);
                var v = row[c] ?? "";
                if (delimiter == '\t')
                    v = v.Replace('\t', ' ');
                if (v.IndexOfAny([delimiter, '"', '\n', '\r']) >= 0)
                    sb.Append('"').Append(v.Replace("\"", "\"\"")).Append('"');
                else
                    sb.Append(v);
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public static void WriteXlsx(IReadOnlyList<Sheet> sheets, string output)
    {
        using var workbook = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            var name = SheetName(sheet.Name, used);
            var ws = workbook.Worksheets.Add(name);
            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r];
                for (var c = 0; c < row.Length; c++)
                    SetCell(ws.Cell(r + 1, c + 1), row[c]);
            }
            if (sheet.Rows.Count > 0)
            {
                ws.Row(1).Style.Font.Bold = true;
                // Width from the text length (Excel's unit is one character); see FixedFontMetrics.
                var sample = sheet.Rows.Take(200).ToList();
                var columns = sample.Max(r => r.Length);
                for (var c = 0; c < columns; c++)
                {
                    var longest = sample.Max(r => c < r.Length && r[c] is { } v ? v.Split('\n').Max(l => l.Length) : 0);
                    ws.Column(c + 1).Width = Math.Clamp(longest + 2, 8, 60);
                }
            }
        }
        if (sheets.Count == 0)
            workbook.Worksheets.Add(T("Sheet1", "Лист1"));
        workbook.SaveAs(output);
    }

    private static string SheetName(string raw, HashSet<string> used)
    {
        var clean = new string(raw.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
        if (clean.Length == 0)
            clean = T("Sheet", "Лист");
        if (clean.Length > 31)
            clean = clean[..31];
        var name = clean;
        for (var i = 2; !used.Add(name); i++)
            name = clean.Length > 27 ? clean[..27] + " " + i : clean + " " + i;
        return name;
    }

    private static void SetCell(IXLCell cell, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        var keepAsText = value.Length > 1 && value[0] == '0' && char.IsDigit(value[1]); // ZIP codes, IDs
        if (!keepAsText && value.Length < 16 &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            cell.Value = number;
        }
        else
        {
            cell.Value = value;
        }
    }

    public static async Task<IEnumerable<string>> ConvertAsync(FileItem item, string format, OperationContext ctx)
    {
        var sheets = await Task.Run(() => Read(item.Path), ctx.CancellationToken).ConfigureAwait(false);
        var dir = ctx.OutputDirFor(item.Path);
        if (format == "xlsx")
        {
            return [await OutputPaths.ProduceAsync(dir, item.BaseName, "xlsx", tmp => Task.Run(() => WriteXlsx(sheets, tmp))).ConfigureAwait(false)];
        }

        var delimiter = format == "tsv" ? '\t' : ',';
        var nonEmpty = sheets.Where(s => s.Rows.Count > 0).ToList();
        if (nonEmpty.Count == 0)
            nonEmpty = sheets.Take(1).ToList();
        var outputs = new List<string>();
        foreach (var sheet in nonEmpty)
        {
            var name = nonEmpty.Count == 1 ? item.BaseName : $"{item.BaseName} - {sheet.Name}";
            // BOM so Excel opens UTF-8 CSV with the right diacritics.
            outputs.Add(await OutputPaths.ProduceAsync(dir, name, format, tmp =>
                File.WriteAllTextAsync(tmp, ToDelimited(sheet.Rows, delimiter), new UTF8Encoding(true), ctx.CancellationToken)).ConfigureAwait(false));
        }
        return outputs;
    }
}
