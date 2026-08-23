using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class OptimizationCatalogTests
{
    private const string SettingKey = "TextureQuality";
    private const string OtherKey   = "ShaderQuality";
    private const string DatPath    = "common/data/visualsettings.dat";
    private const string Archive    = @"update\update.rpf";

    private static OptimizationOption Option(
        int idx,
        IReadOnlyDictionary<string, string>? settings = null,
        IReadOnlyList<OptimizationFileEdit>? edits = null)
        => new(idx, new Dictionary<string, string> { ["ru"] = "вариант " + idx },
               new Dictionary<string, string>(), "", "",
               settings ?? new Dictionary<string, string>(),
               edits ?? Array.Empty<OptimizationFileEdit>());

    private static OptimizationGroup Group(
        string key, int resetIndex = 0, params OptimizationOption[] options)
        => new(key, "toggle", new[] { "setting" }, resetIndex,
               Array.Empty<string>(), Array.Empty<string>(), 0, false, true, "",
               new Dictionary<string, string> { ["ru"] = key },
               new Dictionary<string, string>(), options);

    private static OptimizationFileEdit Edit(string key, string value)
        => new(Archive, DatPath, key, value);

    [Fact]
    public void Согласованный_каталог_проблем_не_даёт()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("textures", 0, Option(0, new Dictionary<string, string> { [SettingKey] = "2" })) },
            new Dictionary<string, string> { [SettingKey] = "textures" });

        Assert.Empty(catalog.Validate());
    }

    [Fact]
    public void Чужой_ключ_у_группы_ловится()
    {
        var catalog = new OptimizationCatalog(
            new[]
            {
                Group("textures", 0, Option(0, new Dictionary<string, string> { [SettingKey] = "2" })),
                Group("shadows",  0, Option(0)),
            },
            new Dictionary<string, string> { [SettingKey] = "shadows" });

        var p = Assert.Single(catalog.Validate());
        Assert.Contains(SettingKey, p);
        Assert.Contains("shadows", p);
    }

    [Fact]
    public void Незарегистрированный_ключ_ловится()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("textures", 0, Option(0, new Dictionary<string, string> { [SettingKey] = "2" })) },
            new Dictionary<string, string>());

        Assert.Contains(catalog.Validate(), p => p.Contains(SettingKey));
    }

    [Fact]
    public void Ключ_которого_лаунчер_не_знает_ловится()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("textures", 0, Option(0)) },
            new Dictionary<string, string> { ["ВыдуманныйКлюч"] = "textures" });

        Assert.Contains(catalog.Validate(), p => p.Contains("ВыдуманныйКлюч"));
    }

    [Fact]
    public void Ключ_за_несуществующей_группой_ловится()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("textures", 0, Option(0)) },
            new Dictionary<string, string> { [SettingKey] = "такойГруппыНет" });

        Assert.Contains(catalog.Validate(), p => p.Contains("такойГруппыНет"));
    }

    [Fact]
    public void Две_группы_на_один_ключ_ловятся_хотя_бы_у_одной()
    {
        var catalog = new OptimizationCatalog(
            new[]
            {
                Group("textures", 0, Option(0, new Dictionary<string, string> { [SettingKey] = "2" })),
                Group("shadows",  0, Option(0, new Dictionary<string, string> { [SettingKey] = "0" })),
            },
            new Dictionary<string, string> { [SettingKey] = "textures" });

        var p = Assert.Single(catalog.Validate());
        Assert.Contains("shadows", p);
    }

    [Fact]
    public void Согласованные_правки_файла_проблем_не_дают()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("rain", 0, Option(0, edits: new[] { Edit("rain.NumberParticles", "0") })) },
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                [OptimizationCatalog.FileKey(DatPath, "rain.NumberParticles")] = "rain",
            });

        Assert.Empty(catalog.Validate());
    }

    [Fact]
    public void Чужая_правка_файла_ловится()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("rain", 0, Option(0, edits: new[] { Edit("puddles.scale", "0") })) },
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                [OptimizationCatalog.FileKey(DatPath, "puddles.scale")] = "shadows",
            });

        var p = Assert.Single(catalog.Validate());
        Assert.Contains("puddles.scale", p);
        Assert.Contains("shadows", p);
    }

    [Fact]
    public void Правка_без_реестра_владения_ловится()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("rain", 0, Option(0, edits: new[] { Edit("rain.NumberParticles", "0") })) },
            new Dictionary<string, string>());

        Assert.Contains(catalog.Validate(), p => p.Contains("rain.NumberParticles"));
    }

    [Fact]
    public void Один_ключ_в_разных_файлах_это_разные_адреса()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("rain", 0, Option(0, edits: new[]
            {
                new OptimizationFileEdit(Archive, DatPath, "scale", "0"),
                new OptimizationFileEdit(Archive, "common/data/other.dat", "scale", "0"),
            })) },
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                [OptimizationCatalog.FileKey(DatPath, "scale")] = "rain",
            });

        var p = Assert.Single(catalog.Validate());
        Assert.Contains("other.dat", p);
    }

    [Fact]
    public void Группа_без_вариантов_ловится()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("пустая") }, new Dictionary<string, string>());

        Assert.Contains(catalog.Validate(), p => p.Contains("пустая"));
    }

    [Fact]
    public void Reset_index_без_соответствующего_варианта_ловится()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("textures", resetIndex: 5, options: Option(0)) },
            new Dictionary<string, string>());

        Assert.Contains(catalog.Validate(), p => p.Contains("reset_index"));
    }

    [Fact]
    public void Все_проблемы_возвращаются_разом_а_не_первая()
    {
        var catalog = new OptimizationCatalog(
            new[]
            {
                Group("пустая"),
                Group("textures", resetIndex: 9,
                      options: Option(0, new Dictionary<string, string> { [OtherKey] = "1" })),
            },
            new Dictionary<string, string>());

        Assert.True(catalog.Validate().Count >= 3);
    }

    [Fact]
    public void Find_и_KeysOwnedBy_не_зависят_от_регистра()
    {
        var catalog = new OptimizationCatalog(
            new[] { Group("Textures", 0, Option(0)) },
            new Dictionary<string, string> { [SettingKey] = "TEXTURES" });

        Assert.NotNull(catalog.Find("textures"));
        Assert.Equal(new[] { SettingKey }, catalog.KeysOwnedBy("textures"));
    }

    [Fact]
    public void Группа_без_метода_setting_не_считается_настроечной()
    {
        var mod = new OptimizationGroup(
            "rain", "toggle", new[] { "mod" }, 0,
            Array.Empty<string>(), Array.Empty<string>(), 0, false, true, "",
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            new[] { Option(0) });

        Assert.False(mod.TouchesSettings);
    }
}
