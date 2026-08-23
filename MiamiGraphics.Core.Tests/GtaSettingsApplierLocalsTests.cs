using System.Xml.Linq;
using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class GtaSettingsApplierLocalsTests
{
    private static XElement Preset(string version = "27",
                                   string w = "1440", string h = "1080", string hz = "164",
                                   string windowed = "1")
        => XElement.Parse($@"<Settings>
              <version value=""{version}"" />
              <graphics><TextureQuality value=""0"" /></graphics>
              <video>
                <ScreenWidth value=""{w}"" /><ScreenHeight value=""{h}"" />
                <RefreshRate value=""{hz}"" /><Windowed value=""{windowed}"" />
                <AdapterIndex value=""1"" />
              </video>
            </Settings>");

    private static XElement Player(string version = "27",
                                   string w = "1920", string h = "1080", string hz = "165")
        => XElement.Parse($@"<Settings>
              <version value=""{version}"" />
              <graphics><TextureQuality value=""2"" /></graphics>
              <video>
                <ScreenWidth value=""{w}"" /><ScreenHeight value=""{h}"" />
                <RefreshRate value=""{hz}"" /><Windowed value=""0"" />
                <AdapterIndex value=""0"" />
              </video>
            </Settings>");

    private static string Video(XElement root, string name)
        => root.Element("video")!.Element(name)!.Attribute("value")!.Value;

    [Fact]
    public void Экран_остаётся_игрока_а_не_автора_пресета()
    {
        var preset = Preset();

        GtaSettingsApplier.KeepPlayerLocals(preset, Player());

        Assert.Equal("1920", Video(preset, "ScreenWidth"));
        Assert.Equal("1080", Video(preset, "ScreenHeight"));
        Assert.Equal("165",  Video(preset, "RefreshRate"));
        Assert.Equal("0",    Video(preset, "Windowed"));
        Assert.Equal("0",    Video(preset, "AdapterIndex"));
    }

    [Fact]
    public void Графика_из_пресета_не_трогается()
    {
        var preset = Preset();

        GtaSettingsApplier.KeepPlayerLocals(preset, Player());

        Assert.Equal("0", preset.Element("graphics")!.Element("TextureQuality")!.Attribute("value")!.Value);
    }

    [Fact]
    public void Версия_берётся_из_файла_игрока()
    {
        var preset = Preset(version: "27");

        var kept = GtaSettingsApplier.KeepPlayerLocals(preset, Player(version: "28"));

        Assert.Equal("28", preset.Element("version")!.Attribute("value")!.Value);
        Assert.Contains(kept, x => x.StartsWith("version"));
    }

    [Fact]
    public void Одинаковая_версия_в_отчёт_не_попадает()
    {
        var kept = GtaSettingsApplier.KeepPlayerLocals(Preset(version: "27"), Player(version: "27"));

        Assert.DoesNotContain(kept, x => x.StartsWith("version"));
        Assert.Contains(kept, x => x.StartsWith("video"));
    }

    [Fact]
    public void Без_своего_файла_переносить_нечего()
    {
        var preset = Preset();

        var kept = GtaSettingsApplier.KeepPlayerLocals(preset, null);

        Assert.Empty(kept);
        Assert.Equal("1440", Video(preset, "ScreenWidth"));
    }

    [Fact]
    public void Блок_video_добавляется_если_в_пресете_его_нет()
    {
        var preset = XElement.Parse(@"<Settings><version value=""27"" /><graphics /></Settings>");

        GtaSettingsApplier.KeepPlayerLocals(preset, Player());

        Assert.Equal("1920", Video(preset, "ScreenWidth"));
    }
}
