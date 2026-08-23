using System.Reflection;
using System.Text;
using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class OptimizationWholeFileTests
{
    private const string Archive = @"update\update.rpf";
    private const string Target  = "common/data/materials/procedural.meta";

    private const string Stub =
        "﻿<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<CProceduralInfo>\r\n" +
        "  <procObjInfos>\r\n  </procObjInfos>\r\n" +
        "  <plantInfos>\r\n  </plantInfos>\r\n" +
        "  <procTagTable>\r\n  </procTagTable>\r\n" +
        "</CProceduralInfo>";

    private const string Vanilla =
        "﻿<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<CProceduralInfo>\r\n" +
        "  <procObjInfos>\r\n    <Item>\r\n      <ModelName>NG_Proc_Paper_02A</ModelName>\r\n" +
        "    </Item>\r\n  </procObjInfos>\r\n" +
        "  <plantInfos>\r\n  </plantInfos>\r\n" +
        "  <procTagTable>\r\n  </procTagTable>\r\n" +
        "</CProceduralInfo>";

    [Fact]
    public void Replace_и_restore_считаются_правками_файла_целиком()
    {
        Assert.True(new OptimizationFileEdit(Archive, Target, OptimizationFileEdit.ReplaceWholeFile, Stub).IsWholeFile);
        Assert.True(new OptimizationFileEdit(Archive, Target, OptimizationFileEdit.RestoreWholeFile, "").IsWholeFile);
        Assert.False(new OptimizationFileEdit(Archive, Target, "rain.NumberParticles", "0").IsWholeFile);
    }

    [Fact]
    public void Замена_целиком_пишет_ровно_переданный_текст()
    {
        var original = Encoding.Latin1.GetBytes(Vanilla);
        var plan = new ModTextPatchBuilder.FilePlan(
            Target, new[] { new ModTextPatchBuilder.Edit("такогоКлючаНет", "1") }, Stub);

        Assert.Equal(Stub, plan.ReplacementText);
        Assert.NotEqual(Stub, Encoding.Latin1.GetString(original));
    }

    private static OptimizationOption Option(int idx, params OptimizationFileEdit[] edits)
        => new(idx, new Dictionary<string, string> { ["ru"] = "вариант " + idx },
               new Dictionary<string, string>(), "", "",
               new Dictionary<string, string>(), edits);

    private static OptimizationCatalog Catalog()
    {
        var group = new OptimizationGroup(
            "garbage", "toggle", new[] { "mod" }, 0,
            Array.Empty<string>(), Array.Empty<string>(), 0, false, true, "",
            new Dictionary<string, string> { ["ru"] = "Объекты мусора" },
            new Dictionary<string, string>(),
            new[]
            {
                Option(0, new OptimizationFileEdit(Archive, Target, OptimizationFileEdit.RestoreWholeFile, "")),
                Option(1, new OptimizationFileEdit(Archive, Target, OptimizationFileEdit.ReplaceWholeFile, Stub)),
            });

        return new OptimizationCatalog(
            new[] { group }, new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                [OptimizationCatalog.FileKey(Target, OptimizationFileEdit.ReplaceWholeFile)] = "garbage",
                [OptimizationCatalog.FileKey(Target, OptimizationFileEdit.RestoreWholeFile)] = "garbage",
            });
    }

    [Fact]
    public void Каталог_с_правками_файла_целиком_проходит_проверку()
    {
        Assert.Empty(Catalog().Validate());
    }

    [Fact]
    public void Пустой_XML_узнаётся_несмотря_на_другие_переводы_строк()
    {
        var alien = Stub.Replace("\r\n", "\n").Replace("﻿", "");
        Assert.NotEqual(Stub, alien);
        Assert.True(SameContentViaResolver(alien, Stub));
    }

    [Fact]
    public void Ванильный_файл_за_пустой_не_принимается()
    {
        Assert.False(SameContentViaResolver(Vanilla, Stub));
    }

    private static bool SameContentViaResolver(string a, string b)
    {
        var m = typeof(OptimizationModStateResolver).GetMethod(
            "SameContent",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)m.Invoke(null, new object[] { a, b })!;
    }
}

public class InteriorEditTests
{
    private const string Archive = @"update\update.rpf";

    private static OptimizationFileEdit Interior(string ytyp)
        => new(Archive, ytyp, OptimizationFileEdit.BuildInterior, "");

    [Fact]
    public void Считается_правкой_файла_целиком_и_отдельно_интерьером()
    {
        var e = Interior("v_int_10.ytyp");
        Assert.True(e.IsWholeFile);
        Assert.True(e.IsInterior);
    }

    [Fact]
    public void Обычная_правка_интерьером_не_считается()
    {
        Assert.False(new OptimizationFileEdit(Archive, "a.dat", "rain.NumberParticles", "0").IsInterior);
        Assert.False(new OptimizationFileEdit(Archive, "a.dat", OptimizationFileEdit.ReplaceWholeFile, "x").IsInterior);
    }

    [Fact]
    public void Каталог_с_интерьерами_проходит_проверку()
    {
        var group = new OptimizationGroup(
            "interiors", "toggle", new[] { "mod", "interior" }, 0,
            Array.Empty<string>(), Array.Empty<string>(), 0, false, true, "",
            new Dictionary<string, string> { ["ru"] = "Объекты в магазинах" },
            new Dictionary<string, string>(),
            new[]
            {
                new OptimizationOption(0, new Dictionary<string, string> { ["ru"] = "Вкл" },
                    new Dictionary<string, string>(), "", "", new Dictionary<string, string>(),
                    Array.Empty<OptimizationFileEdit>()),
                new OptimizationOption(1, new Dictionary<string, string> { ["ru"] = "Выкл" },
                    new Dictionary<string, string>(), "", "", new Dictionary<string, string>(),
                    new[] { Interior("v_int_10.ytyp") }),
            });

        var catalog = new OptimizationCatalog(
            new[] { group }, new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                [OptimizationCatalog.FileKey("v_int_10.ytyp", OptimizationFileEdit.BuildInterior)] = "interiors",
            });

        Assert.Empty(catalog.Validate());
    }

    [Fact]
    public void Незарегистрированный_интерьер_ловится()
    {
        var group = new OptimizationGroup(
            "interiors", "toggle", new[] { "mod" }, 0,
            Array.Empty<string>(), Array.Empty<string>(), 0, false, true, "",
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            new[]
            {
                new OptimizationOption(0, new Dictionary<string, string> { ["ru"] = "Выкл" },
                    new Dictionary<string, string>(), "", "", new Dictionary<string, string>(),
                    new[] { Interior("v_int_10.ytyp") }),
            });

        var catalog = new OptimizationCatalog(new[] { group }, new Dictionary<string, string>());
        Assert.Contains(catalog.Validate(), p => p.Contains("v_int_10.ytyp"));
    }
}
