using System.Globalization;
using static Sweetch.Core.Loc;

namespace Sweetch.Core.Backends;

/// <summary>
/// Light and colour correction as one ffmpeg filter chain, so photos, videos and the live preview
/// all go through the same code. Every knob runs from -100 to 100 with 0 meaning "unchanged".
/// </summary>
public static class Relight
{
    public sealed record Knob(string Key, string Label);

    public static IReadOnlyList<Knob> Knobs() =>
    [
        new("exposure", T("Exposure", "Экспозиция")),
        new("contrast", T("Contrast", "Контраст")),
        new("highlights", T("Highlights", "Света")),
        new("shadows", T("Shadows", "Тени")),
        new("brightness", T("Brightness", "Яркость")),
        new("gamma", T("Gamma", "Гамма")),
        new("warmth", T("Warmth", "Теплота")),
        new("tint", T("Tint", "Оттенок")),
        new("saturation", T("Saturation", "Насыщенность")),
        new("vibrance", T("Vibrance", "Сочность")),
    ];

    public static IReadOnlyList<ParameterSpec> Parameters() =>
        Knobs().Select(k => new ParameterSpec(k.Key, k.Label, "0", "-100 … 100")).ToList();

    /// <summary>The ffmpeg -vf chain for the knob values; "null" (pass-through) when nothing changes.</summary>
    public static string Filter(IReadOnlyDictionary<string, string> values)
    {
        double V(string key) =>
            values.TryGetValue(key, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? Math.Clamp(v, -100, 100) / 100
                : 0;
        static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        var chain = new List<string>();
        if (V("exposure") is var exposure and not 0)
        {
            // ±1.5 EV as the curve 1 − (1 − x)^k: multiplies shadows by k like real exposure, but rolls
            // highlights off instead of clipping them (ffmpeg's exposure filter blows skies out to white).
            var k = Math.Pow(2, exposure * 1.5);
            chain.Add("curves=all='" + string.Join(" ", Enumerable.Range(0, 11).Select(i => F($"{i / 10.0:0.#}/{1 - Math.Pow(1 - i / 10.0, k):0.####}"))) + "'");
        }
        if ((V("shadows"), V("highlights")) is var (shadows, highlights) && (shadows, highlights) != (0, 0))
            chain.Add(F($"curves=all='0/0 0.25/{0.25 + shadows * 0.2:0.###} 0.75/{0.75 + highlights * 0.2:0.###} 1/1'"));
        if (V("warmth") is var warmth and not 0)
            chain.Add(F($"colortemperature=temperature={6500 * Math.Pow(2, -warmth * 0.6):0}:pl=1")); // ≈9850 K (cool) … ≈4290 K (warm), same lightness
        if (V("tint") is var tint and not 0)
            chain.Add(F($"colorbalance=gm={-tint * 0.3:0.###}")); // + is magenta, like photo editors
        if (V("vibrance") is var vibrance and not 0)
            chain.Add(F($"vibrance=intensity={vibrance:0.###}"));
        var (contrast, brightness, gamma, saturation) = (V("contrast"), V("brightness"), V("gamma"), V("saturation"));
        if ((contrast, brightness, gamma, saturation) != (0, 0, 0, 0))
        {
            // eq works in YUV; 4:4:4 keeps photo colour edges sharp.
            chain.Add("format=yuv444p");
            chain.Add(F($"eq=contrast={1 + contrast * 0.5:0.###}:brightness={brightness * 0.2:0.###}:gamma={Math.Pow(2, gamma):0.###}:saturation={1 + saturation:0.###}"));
        }
        return chain.Count == 0 ? "null" : string.Join(",", chain);
    }
}
