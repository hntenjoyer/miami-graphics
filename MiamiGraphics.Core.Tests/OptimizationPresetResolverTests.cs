using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class OptimizationPresetResolverTests
{
    private static OptimizationOption Option(int idx, params (string Key, string Value)[] settings)
        => new(idx, new Dictionary<string, string> { ["ru"] = "вариант " + idx },
               new Dictionary<string, string>(), "", "",
               settings.ToDictionary(x => x.Key, x => x.Value),
               Array.Empty<OptimizationFileEdit>());

    private static OptimizationGroup Group(string key, params OptimizationOption[] options)
        => new(key, "toggle", new[] { "setting" }, 0,
               Array.Empty<string>(), Array.Empty<string>(), 0, false, true, "",
               new Dictionary<string, string> { ["ru"] = key },
               new Dictionary<string, string>(), options);

    private static OptimizationCatalog Catalog(params OptimizationGroup[] groups)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
            foreach (var o in g.Options)
                foreach (var k in o.Settings.Keys)
                    owners[k] = g.Key;
        return new OptimizationCatalog(groups, owners);
    }

    private static string Xml(params (string Key, string Value)[] values)
        => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Settings><version value=\"27\" /><graphics>"
         + string.Concat(values.Select(v => $"<{v.Key} value=\"{v.Value}\" />"))
         + "</graphics></Settings>";

    [Fact]
    public void Значение_ниже_самого_низкого_варианта_это_он_и_есть()
    {
        var catalog = Catalog(Group("particles",
            Option(0, ("ParticleQuality", "2")),
            Option(1, ("ParticleQuality", "0"))));

        var r = OptimizationPresetResolver.Resolve(Xml(("ParticleQuality", "-1")), catalog);

        Assert.Equal(1, r.Selections["particles"]);
        Assert.DoesNotContain("particles", r.CustomGroups);
    }

    [Fact]
    public void Значение_выше_самого_высокого_варианта_это_верхний()
    {
        var catalog = Catalog(Group("particles",
            Option(0, ("ParticleQuality", "2")),
            Option(1, ("ParticleQuality", "0"))));

        var r = OptimizationPresetResolver.Resolve(Xml(("ParticleQuality", "5")), catalog);

        Assert.Equal(0, r.Selections["particles"]);
    }

    [Fact]
    public void Часть_ключей_за_краем_часть_совпала_точно_вариант_засчитан()
    {
        var catalog = Catalog(Group("graphics",
            Option(1, ("TextureQuality", "0"), ("WaterQuality", "0"), ("LodScale", "0.200000")),
            Option(2, ("TextureQuality", "1"), ("WaterQuality", "0"), ("LodScale", "0.300000")),
            Option(3, ("TextureQuality", "2"), ("WaterQuality", "2"), ("LodScale", "0.500000"))));

        var r = OptimizationPresetResolver.Resolve(
            Xml(("TextureQuality", "0"), ("WaterQuality", "-1"), ("LodScale", "0.000000")), catalog);

        Assert.Equal(1, r.Selections["graphics"]);
    }

    [Fact]
    public void Ключи_тянущие_в_разные_варианты_остаются_своим_значением()
    {
        var catalog = Catalog(Group("lodbias",
            Option(0, ("PedLodBias", "1.000000"), ("VehicleLodBias", "1.000000")),
            Option(1, ("PedLodBias", "0.000000"), ("VehicleLodBias", "0.000000")),
            Option(2, ("PedLodBias", "-0.500000"), ("VehicleLodBias", "-0.500000"))));

        var r = OptimizationPresetResolver.Resolve(
            Xml(("PedLodBias", "0.000000"), ("VehicleLodBias", "-0.500000")), catalog);

        Assert.Null(r.Selections["lodbias"]);
        Assert.Contains("lodbias", r.CustomGroups);
    }

    [Fact]
    public void Значение_между_вариантами_не_притягивается_к_соседнему()
    {
        var catalog = Catalog(Group("particles",
            Option(0, ("ParticleQuality", "2")),
            Option(1, ("ParticleQuality", "0"))));

        var r = OptimizationPresetResolver.Resolve(Xml(("ParticleQuality", "1")), catalog);

        Assert.Null(r.Selections["particles"]);
    }

    [Fact]
    public void Нечисловой_ключ_краёв_не_имеет_и_обязан_совпасть_точно()
    {
        var catalog = Catalog(Group("aa",
            Option(0, ("FXAA_Enabled", "true")),
            Option(1, ("FXAA_Enabled", "false"))));

        var r = OptimizationPresetResolver.Resolve(Xml(("FXAA_Enabled", "false")), catalog);
        Assert.Equal(1, r.Selections["aa"]);
    }

    [Fact]
    public void Точное_совпадение_по_прежнему_главнее()
    {
        var catalog = Catalog(Group("particles",
            Option(0, ("ParticleQuality", "2")),
            Option(1, ("ParticleQuality", "0"))));

        var r = OptimizationPresetResolver.Resolve(Xml(("ParticleQuality", "0")), catalog);
        Assert.Equal(1, r.Selections["particles"]);
    }

    [Fact]
    public void Разный_формат_одного_числа_это_совпадение()
    {
        var catalog = Catalog(Group("lodbias", Option(0, ("LodScale", "0.200000"))));

        var r = OptimizationPresetResolver.Resolve(Xml(("LodScale", "0.2")), catalog);
        Assert.Equal(0, r.Selections["lodbias"]);
    }
}
