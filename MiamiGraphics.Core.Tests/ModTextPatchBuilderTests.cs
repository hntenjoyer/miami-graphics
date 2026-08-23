using System.Text;
using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class ModTextPatchBuilderTests
{
    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);
    private static string Text(byte[] b) => Encoding.Latin1.GetString(b);

    private static string Patch(string source, params (string Key, string Value)[] edits)
        => Text(ModTextPatchBuilder.ApplyEdits(
            Bytes(source),
            edits.Select(e => new ModTextPatchBuilder.Edit(e.Key, e.Value)).ToList(),
            out _, out _));

    [Fact]
    public void Запись_прежнего_значения_не_меняет_ни_байта()
    {
        const string src = "rain.NumberParticles\t\t16384\r\npuddles.scale\t0.015\r\n";
        Assert.Equal(src, Patch(src, ("rain.NumberParticles", "16384"), ("puddles.scale", "0.015")));
    }

    [Fact]
    public void Завершающий_таб_после_значения_сохраняется()
    {
        const string src = "puddles.ripples.minsize\t\t\t0.012\t\r\n";
        Assert.Equal("puddles.ripples.minsize\t\t\t0\t\r\n", Patch(src, ("puddles.ripples.minsize", "0")));
    }

    [Fact]
    public void Завершающие_пробелы_после_значения_сохраняются()
    {
        const string src = "rain.diffuse\t1.00   \n";
        Assert.Equal("rain.diffuse\t0   \n", Patch(src, ("rain.diffuse", "0")));
    }

    [Fact]
    public void Отступ_перед_ключом_сохраняется()
    {
        Assert.Equal("\t  rain.ambient\t0\n", Patch("\t  rain.ambient\t0.40\n", ("rain.ambient", "0")));
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\t\t\t")]
    [InlineData(" ")]
    [InlineData("    ")]
    [InlineData(" \t ")]
    public void Разделитель_берётся_из_строки_а_не_подставляется_свой(string sep)
    {
        var src = $"rain.wrapBias{sep}0.40\n";
        Assert.Equal($"rain.wrapBias{sep}0\n", Patch(src, ("rain.wrapBias", "0")));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void Перевод_строки_сохраняется(string eol)
    {
        var src = $"a\t1{eol}b\t2{eol}";
        Assert.Equal($"a\t9{eol}b\t2{eol}", Patch(src, ("a", "9")));
    }

    [Fact]
    public void Смешанные_переводы_строк_не_приводятся_к_одному()
    {
        const string src = "a\t1\r\nb\t2\nc\t3\r\n";
        Assert.Equal("a\t9\r\nb\t9\nc\t9\r\n", Patch(src, ("a", "9"), ("b", "9"), ("c", "9")));
    }

    [Fact]
    public void Файл_без_перевода_строки_в_конце_его_не_получает()
    {
        Assert.Equal("a\t9", Patch("a\t1", ("a", "9")));
    }

    [Fact]
    public void Комментарии_и_пустые_строки_остаются_как_были()
    {
        const string src = "# rain.diffuse 1.00\r\n\r\n   \r\nrain.diffuse\t1.00\r\n";
        Assert.Equal("# rain.diffuse 1.00\r\n\r\n   \r\nrain.diffuse\t0\r\n",
                     Patch(src, ("rain.diffuse", "0")));
    }

    [Fact]
    public void Закомментированный_ключ_не_считается_применённой_правкой()
    {
        ModTextPatchBuilder.ApplyEdits(
            Bytes("# rain.gravity\t-0.98\r\n"),
            new[] { new ModTextPatchBuilder.Edit("rain.gravity", "0") },
            out var applied, out var missing);

        Assert.Equal(0, applied);
        Assert.Equal(new[] { "rain.gravity" }, missing);
    }

    [Fact]
    public void Байты_вне_ASCII_переживают_проход()
    {
        var src = "note\t" + (char)0xE9 + (char)0xFF + "\r\na\t1\r\n";
        Assert.Equal("note\t" + (char)0xE9 + (char)0xFF + "\r\na\t9\r\n", Patch(src, ("a", "9")));
    }

    [Fact]
    public void Ключ_совпадает_целиком_а_не_по_префиксу()
    {
        const string src = "rain.diffuse\t1.00\nrain.diffuseExtra\t2.00\n";
        Assert.Equal("rain.diffuse\t0\nrain.diffuseExtra\t2.00\n", Patch(src, ("rain.diffuse", "0")));
    }

    [Fact]
    public void Отсутствующий_ключ_попадает_в_missing_а_не_теряется_молча()
    {
        ModTextPatchBuilder.ApplyEdits(
            Bytes("a\t1\n"),
            new[] { new ModTextPatchBuilder.Edit("a", "9"), new ModTextPatchBuilder.Edit("нетуТакого", "0") },
            out var applied, out var missing);

        Assert.Equal(1, applied);
        Assert.Equal(new[] { "нетуТакого" }, missing);
    }

    [Fact]
    public void Регистр_ключа_не_мешает_найти_строку()
    {
        Assert.Equal("Rain.Diffuse\t0\n", Patch("Rain.Diffuse\t1.00\n", ("rain.diffuse", "0")));
    }

    [Fact]
    public void ParseKeyValues_видит_ровно_те_ключи_которые_ApplyEdits_умеет_править()
    {
        const string src = "# comment\ta\r\n\r\nrain.diffuse\t1.00\r\npuddles.scale   0.015\r\nодинокий\r\n";
        var parsed = ModTextPatchBuilder.ParseKeyValues(src);

        Assert.Equal(new[] { "puddles.scale", "rain.diffuse" }, parsed.Keys.OrderBy(k => k).ToArray());

        foreach (var k in parsed.Keys)
        {
            ModTextPatchBuilder.ApplyEdits(Bytes(src), new[] { new ModTextPatchBuilder.Edit(k, "0") },
                                           out var applied, out _);
            Assert.Equal(1, applied);
        }
    }

    [Fact]
    public void Круг_ваниль_нули_ваниль_возвращает_исходные_байты()
    {
        const string src =
            "#Values for rain GPU particle effect\r\n" +
            "rain.NumberParticles\t16384\r\n" +
            "rain.UseLitShader\t1.00\r\n" +
            "puddles.ripples.minsize\t\t\t0.012\t\r\n" +
            "puddles.animspeed\t30.\r\n";

        var keys = ModTextPatchBuilder.ParseKeyValues(src);
        var off = keys.Keys.Select(k => new ModTextPatchBuilder.Edit(k, "0")).ToList();
        var on  = keys.Select(kv => new ModTextPatchBuilder.Edit(kv.Key, kv.Value)).ToList();

        var zeroed   = ModTextPatchBuilder.ApplyEdits(Bytes(src), off, out var n1, out _);
        var restored = ModTextPatchBuilder.ApplyEdits(zeroed, on, out var n2, out _);

        Assert.Equal(keys.Count, n1);
        Assert.Equal(keys.Count, n2);
        Assert.NotEqual(src, Text(zeroed));
        Assert.Equal(src, Text(restored));
    }
}
