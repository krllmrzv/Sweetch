using System.Text;
using Sweetch.Core.Backends;
using Sweetch.Core.Operations;
using ImageMagick;

namespace Sweetch.Core;

/// <summary>
/// Runs a handful of built-in conversions (no external tools needed) inside a published app, so a trimmed
/// build that lost something it needs fails in CI instead of on someone's PC. Returns one line per check.
/// </summary>
public static class SmokeTest
{
    public static IReadOnlyList<string> Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new List<string>();
        var ops = Catalog.Build();
        var context = new OperationContext { Tools = new ToolLocator(dir), OutputDirectory = dir };

        string Run(string id, params string[] inputs)
        {
            var op = ops.Single(o => o.Id == id);
            var items = inputs.Select(i => new FileItem(i)).ToList();
            if (!op.AppliesTo(items))
                throw new InvalidOperationException($"{id} does not apply");
            // On a thread-pool thread: the caller may own a UI synchronization context that isn't pumping.
            var output = Task.Run(() => op.ExecuteAsync(items, context)).GetAwaiter().GetResult().Outputs.Single();
            if (!File.Exists(output) && !Directory.Exists(output))
                throw new InvalidOperationException($"{id} produced nothing");
            log.Add($"ok {id} -> {Path.GetFileName(output)}");
            return output;
        }

        var png = Path.Combine(dir, "smoke.png");
        using (var image = new MagickImage(MagickColors.Orange, 64, 48))
            image.Write(png);
        var jpg = Run("image.to.jpg", png);
        Run("image.to.webp", png);
        Run("image.compress", jpg);
        var pdf = Run("image.to.pdf", png);
        var pdf2 = Path.Combine(dir, "smoke2.pdf");
        File.Copy(pdf, pdf2);
        Run("pdf.merge", pdf, pdf2);
        Run("pdf.to.png", pdf);

        var csv = Path.Combine(dir, "smoke.csv");
        File.WriteAllText(csv, "name;count\nŽluťák;42\n", new UTF8Encoding(false));
        var xlsx = Run("sheet.to.xlsx", csv);
        Run("sheet.to.tsv", xlsx);

        var srt = Path.Combine(dir, "smoke.srt");
        File.WriteAllText(srt, "1\n00:00:01,000 --> 00:00:02,000\nAhoj\n");
        Run("subs.to.vtt", srt);
        Run("subs.to.timed", Run("subs.to.ass", srt));

        // YAML and TOML libraries in a trimmed build.
        var json = Path.Combine(dir, "smoke.json");
        File.WriteAllText(json, """{"name":"Žluťák","list":[{"id":1},{"id":2}]}""");
        Run("data.to.json", Run("data.to.toml", Run("data.to.yaml", json)));
        Run("data.to.xml", json);
        Run("data.to.xlsx", json);
        var vcf = Path.Combine(dir, "smoke.vcf");
        File.WriteAllText(vcf, "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Jan\r\nTEL:+420 1\r\nEND:VCARD\r\n");
        Run("sheet.to.vcf", Run("vcf.to.csv", vcf));

        var zip = Run("pack.zip", png, csv);
        Run("archive.extract", zip);
        return log;
    }
}
