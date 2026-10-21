using static Sweetch.Core.Loc;

namespace Sweetch.Core;

public static class OutputPaths
{
    /// <summary>"dir/name.ext", or "dir/name (2).ext" … if that already exists.</summary>
    public static string Unique(string dir, string baseName, string ext)
    {
        ext = ext.TrimStart('.');
        var suffix = ext.Length == 0 ? "" : "." + ext;
        var candidate = Path.Combine(dir, baseName + suffix);
        for (var i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{baseName} ({i}){suffix}");
        return candidate;
    }

    public static string UniqueDirectory(string dir, string name) => Unique(dir, name, "");

    /// <summary>
    /// Runs <paramref name="produce"/> against a hidden temporary path in <paramref name="dir"/> and,
    /// when it succeeds, moves the file to a unique final name. Partial output is removed on failure.
    /// </summary>
    public static async Task<string> ProduceAsync(string dir, string baseName, string ext, Func<string, Task> produce)
    {
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{baseName}.{Guid.NewGuid().ToString("N")[..8]}.part.{ext}");
        try
        {
            await produce(temp).ConfigureAwait(false);
            if (!File.Exists(temp))
                throw new ConversionException(T("The conversion produced no output.", "Конвертация не создала выходных файлов."));
            var final = Unique(dir, baseName, ext);
            File.Move(temp, final);
            return final;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public static string TempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Sweetch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// "моё видео (обрезано)" → "moe_video_obrezano": ASCII letters, digits, '.', '-' and '_' only, so nothing
    /// needs quoting in a shell. Cyrillic is transliterated, other accents dropped (Žluťoučký → Zlutoucky).
    /// </summary>
    public static string PlainName(string name)
    {
        var latin = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (Cyrillic.TryGetValue(char.ToLowerInvariant(c), out var t))
                latin.Append(char.IsUpper(c) && t.Length > 0 ? char.ToUpperInvariant(t[0]) + t[1..] : t);
            else
                latin.Append(c);
        }
        var chars = latin.ToString().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        var plain = System.Text.RegularExpressions.Regex.Replace(new string(chars), "_{2,}", "_").Trim('_');
        return plain.Length == 0 ? "file" : plain;
    }

    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z",
        ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch",
        ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ґ'] = "g", ['ў'] = "u",
    };

    /// <summary>
    /// Renames a produced file or folder to a plain name (and, with <paramref name="recurse"/>, what's inside);
    /// returns the new path. Taken names get "_2", "_3"….
    /// </summary>
    public static string MakePlain(string path, bool recurse)
    {
        path = path.TrimEnd('\\', '/');
        var isDir = Directory.Exists(path);
        if (isDir && recurse)
        {
            foreach (var child in Directory.GetFileSystemEntries(path))
                MakePlain(child, recurse: true);
        }
        var name = Path.GetFileName(path);
        var ext = isDir ? "" : Path.GetExtension(name);
        var stem = PlainName(name[..^ext.Length]);
        if (stem + ext == name)
            return path;
        var dir = Path.GetDirectoryName(path)!;
        var target = Path.Combine(dir, stem + ext);
        for (var i = 2; File.Exists(target) || Directory.Exists(target); i++)
            target = Path.Combine(dir, $"{stem}_{i}{ext}");
        if (isDir)
            Directory.Move(path, target);
        else
            File.Move(path, target);
        return target;
    }
}

public sealed class ConversionException(string message, Exception? inner = null) : Exception(message, inner);
