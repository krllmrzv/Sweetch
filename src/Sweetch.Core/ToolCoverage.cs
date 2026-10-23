namespace Sweetch.Core;

/// <summary>
/// What each helper program adds, worked out from the catalog instead of written by hand: an operation
/// belongs to a program when it works with every program present and stops working without that one.
/// New formats and operations show up here by themselves.
/// </summary>
public static class ToolCoverage
{
    /// <summary>Formats that get the same conversions and tools from a program, listed together.</summary>
    public sealed record Group(IReadOnlyList<string> Formats, IReadOnlyList<string> Converts, IReadOnlyList<string> Tools);

    public static IReadOnlyDictionary<ExternalTool, IReadOnlyList<Group>> Build(IReadOnlyList<Operation> ops)
    {
        var tools = Enum.GetValues<ExternalTool>();
        var everything = ToolLocator.Pretending(tools);
        var result = new Dictionary<ExternalTool, IReadOnlyList<Group>>();
        foreach (var tool in tools)
        {
            var without = ToolLocator.Pretending(tools.Where(t => t != tool));
            var perFormat = new List<(string Format, string[] Converts, string[] Tools)>();
            foreach (var format in Formats.All)
            {
                // Two files as well, for joins and merges that need a pair.
                List<FileItem> one = [new("a." + format)], two = [new("a." + format), new("b." + format)];
                var added = ops.Where(o => new[] { one, two }.Any(items => o.AppliesTo(items) && o.IsAvailable(everything, items) && !o.IsAvailable(without, items)))
                    .Where(o => o.Wheel != WheelKind.Actions).DistinctBy(o => o.Label).ToList();
                if (added.Count > 0)
                    perFormat.Add((format, [.. added.Where(o => o.Wheel == WheelKind.Convert).Select(o => o.Label)], [.. added.Where(o => o.Wheel == WheelKind.Tools).Select(o => o.Label)]));
            }
            // MP3 can't be converted to MP3, so its list lacks itself; counting the format in keeps all audio together.
            var outputs = perFormat.SelectMany(p => p.Converts).ToHashSet();
            string Key(string format, string[] converts) =>
                string.Join("|", converts.Append(Formats.Display(format)).Where(outputs.Contains).Order()) + "/" + string.Join("|", perFormat.First(p => p.Format == format).Tools);
            result[tool] = perFormat
                .GroupBy(p => Key(p.Format, p.Converts))
                .Select(g => new Group([.. g.Select(p => p.Format)], [.. g.SelectMany(p => p.Converts).Distinct()], g.First().Tools))
                .ToList();
        }
        return result;
    }

    /// <summary>The programs that add something for <paramref name="format"/> ("png", ".PNG" or "photo.png").</summary>
    public static IReadOnlyList<ExternalTool> For(IReadOnlyDictionary<ExternalTool, IReadOnlyList<Group>> coverage, string format)
    {
        var key = Normalize(format);
        return [.. coverage.Where(kv => kv.Value.Any(g => g.Formats.Contains(key))).Select(kv => kv.Key)];
    }

    public static string Normalize(string format) => Formats.KeyOf("x." + format.Trim().TrimStart('*').TrimStart('.').Split('.').Last().Trim());
}
