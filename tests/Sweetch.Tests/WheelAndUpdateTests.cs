using Sweetch.Core;
using Sweetch.Core.Operations;

namespace Sweetch.Tests;

public class WheelLayoutTests
{
    private static IReadOnlyList<Operation> ImageConversions() =>
        Catalog.For(Catalog.Build(), WheelKind.Convert, [new FileItem("x.png")], new ToolLocator(Path.GetTempPath()));

    [Fact]
    public void Hidden_slices_are_removed()
    {
        var arranged = WheelLayout.Arrange(ImageConversions(), ["image.to.jpg"], []);
        Assert.DoesNotContain(arranged, o => o.Id == "image.to.jpg");
        Assert.Contains(arranged, o => o.Id == "image.to.webp");
    }

    [Fact]
    public void Favorites_come_first_in_their_order_and_the_rest_keeps_its_order()
    {
        var all = ImageConversions();
        var arranged = WheelLayout.Arrange(all, [], ["image.to.pdf", "image.to.webp"]);
        Assert.Equal("image.to.pdf", arranged[0].Id);
        Assert.Equal("image.to.webp", arranged[1].Id);
        var rest = all.Where(o => o.Id is not "image.to.pdf" and not "image.to.webp").Select(o => o.Id);
        Assert.Equal(rest, arranged.Skip(2).Select(o => o.Id));
    }
}

public class UpdateTests
{
    private const string Json = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://example.com/sweetch/releases/tag/v1.2.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "Sweetch-1.2-win-x64.zip.sha256", "browser_download_url": "https://example.com/zip.sha256" },
            { "name": "Sweetch-1.2-win-x64.zip", "browser_download_url": "https://example.com/zip", "digest": "sha256:0123" },
            { "name": "Sweetch-1.2-Setup-win-x64.exe", "browser_download_url": "https://example.com/setup.exe", "digest": "sha256:ABCDEF" }
          ]
        }
        """;

    [Fact]
    public void Parses_release()
    {
        var info = UpdateInfo.Parse(Json)!;
        Assert.Equal(new Version(1, 2, 0), info.Version);
        Assert.Equal("https://example.com/setup.exe", info.InstallerUrl);
        Assert.Equal("abcdef", info.InstallerSha256);
        Assert.Equal(new UpdateInfo.Asset("https://example.com/zip", "0123"), info.Zip);
    }

    [Theory]
    [InlineData("1.1.1.0", true)]
    [InlineData("1.2.0.0", false)]
    [InlineData("1.3.0", false)]
    [InlineData("1.0.45", true)]
    public void Compares_versions(string current, bool newer) =>
        Assert.Equal(newer, UpdateInfo.Parse(Json)!.IsNewerThan(Version.Parse(current)));

    [Fact]
    public void Ignores_prereleases() =>
        Assert.Null(UpdateInfo.Parse(Json.Replace("\"prerelease\": false", "\"prerelease\": true")));
}
