using System.Text.Json;

namespace Sweetch.Core;

/// <summary>The latest published release, as reported by the GitHub releases API.</summary>
/// <param name="Installer">The setup .exe, for copies installed with it.</param>
/// <param name="Zip">The portable archive, for copies unpacked from it.</param>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, UpdateInfo.Asset? Installer, UpdateInfo.Asset? Zip)
{
    /// <param name="Sha256">From GitHub's own "digest" of the upload, lower case; null on older releases.</param>
    public sealed record Asset(string Url, string? Sha256);

    public string? InstallerUrl => Installer?.Url;
    public string? InstallerSha256 => Installer?.Sha256;

    /// <summary>Parses a "releases/latest" response; null when it isn't a usable release.</summary>
    public static UpdateInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
            return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var version))
            return null;
        var page = root.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "";

        Asset? installer = null, zip = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                // "Sweetch-2.1-Setup-win-x64.exe" and "Sweetch-2.1-win-x64.zip"; the .sha256 files beside them are skipped.
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("Sweetch", StringComparison.OrdinalIgnoreCase) || asset.GetProperty("browser_download_url").GetString() is not { } link)
                    continue;
                var sha = asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? d["sha256:".Length..].ToLowerInvariant() : null;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    installer ??= new Asset(link, sha);
                else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    zip ??= new Asset(link, sha);
            }
        }
        return new UpdateInfo(version, tag, page, installer, zip);
    }

    public bool IsNewerThan(Version current) => Version > Normalize(current);

    public static bool TryParseVersion(string text, out Version version)
    {
        var ok = Version.TryParse(text.Trim().TrimStart('v', 'V'), out var parsed);
        version = ok ? Normalize(parsed!) : new Version(0, 0, 0);
        return ok;
    }

    /// <summary>1.2 → 1.2.0 and 1.2.3.0 → 1.2.3, so versions compare the way people read them.</summary>
    public static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
