using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class OptimizationModResetTests
{
    private const string Archive = @"update\update.rpf";
    private const string DatPath = "common/data/visualsettings.dat";
    private const string Clean   = "clean.rpf";

    private static readonly Dictionary<string, string> Vanilla = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rain.NumberParticles"] = "16384",
        ["rain.UseLitShader"]    = "1.00",
        ["puddles.scale"]        = "0.015",
    };

    private static OptimizationFileEdit Edit(string key, string value)
        => new(Archive, DatPath, key, value);

    private static OptimizationOption Option(int idx, params OptimizationFileEdit[] edits)
        => new(idx, new Dictionary<string, string> { ["ru"] = "вариант " + idx },
               new Dictionary<string, string>(), "", "",
               new Dictionary<string, string>(), edits);

    private static OptimizationGroup ModGroup(string key, params OptimizationOption[] options)
        => new(key, "toggle", new[] { "mod" }, 0,
               Array.Empty<string>(), Array.Empty<string>(), 0, false, true, "",
               new Dictionary<string, string> { ["ru"] = key },
               new Dictionary<string, string>(), options);

    private static OptimizationGroup Rain() => ModGroup(
        "rain",
        Option(0, Edit("rain.NumberParticles", "16384"), Edit("rain.UseLitShader", "1.00")),
        Option(1, Edit("rain.NumberParticles", "0"),     Edit("rain.UseLitShader", "0")));

    private static string AsFile(IReadOnlyDictionary<string, string> values)
        => string.Concat(values.Select(kv => kv.Key + "\t" + kv.Value + "\r\n"));

    private static OptimizationModApplyService Service(
        IReadOnlyDictionary<string, string>? values = null, bool fileMissing = false)
        => new("C:\\gta", null,
               (_, target) => fileMissing || !string.Equals(target, DatPath, StringComparison.OrdinalIgnoreCase)
                   ? null
                   : AsFile(values ?? Vanilla));

    private static IReadOnlyDictionary<string, string> CleanMap(string path)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [Archive] = path };

    private static string MakeCleanFile()
    {
        var p = Path.Combine(Path.GetTempPath(), "mg-test-clean-" + Guid.NewGuid().ToString("N") + ".rpf");
        File.WriteAllText(p, "не настоящий архив, читает его подменённый ридер");
        return p;
    }

    [Fact]
    public void Сброс_возвращает_ванильные_значения_всех_ключей_группы()
    {
        var clean = MakeCleanFile();
        try
        {
            var edits = Service().ResolveResets(new[] { Rain() }, CleanMap(clean), out var error);

            Assert.Null(error);
            Assert.NotNull(edits);
            Assert.Equal(
                new[] { ("rain.NumberParticles", "16384"), ("rain.UseLitShader", "1.00") },
                edits!.Select(x => (x.Edit.Key, x.Edit.Value)).OrderBy(x => x.Key).ToArray());
            Assert.All(edits!, x => Assert.Equal("rain", x.GroupKey));
        }
        finally { File.Delete(clean); }
    }

    [Fact]
    public void Сбрасываются_ключи_из_всех_вариантов_а_не_только_из_текущего()
    {
        var group = ModGroup(
            "rain",
            Option(0, Edit("rain.NumberParticles", "16384")),
            Option(1, Edit("rain.NumberParticles", "0"), Edit("puddles.scale", "0")));

        var clean = MakeCleanFile();
        try
        {
            var edits = Service().ResolveResets(new[] { group }, CleanMap(clean), out var error);

            Assert.Null(error);
            Assert.Contains(edits!, x => x.Edit.Key == "puddles.scale" && x.Edit.Value == "0.015");
        }
        finally { File.Delete(clean); }
    }

    [Fact]
    public void Файл_читается_один_раз_на_все_группы()
    {
        var reads = 0;
        var svc = new OptimizationModApplyService("C:\\gta", null, (_, _) => { reads++; return AsFile(Vanilla); });

        var clean = MakeCleanFile();
        try
        {
            svc.ResolveResets(new[] { Rain(), ModGroup("other", Option(0, Edit("puddles.scale", "0"))) },
                              CleanMap(clean), out var error);
            Assert.Null(error);
            Assert.Equal(1, reads);
        }
        finally { File.Delete(clean); }
    }

    [Fact]
    public void Без_чистой_копии_сброс_отменяется_а_не_делает_вид_что_прошёл()
    {
        var edits = Service().ResolveResets(new[] { Rain() }, null, out var error);

        Assert.Null(edits);
        Assert.Contains("чистой копии", error);
    }

    [Fact]
    public void Несуществующий_файл_чистой_копии_это_тоже_отказ()
    {
        var edits = Service().ResolveResets(
            new[] { Rain() }, CleanMap("C:\\нет\\такого\\файла.rpf"), out var error);

        Assert.Null(edits);
        Assert.Contains("чистой копии", error);
    }

    [Fact]
    public void Отсутствие_целевого_файла_в_копии_это_отказ()
    {
        var clean = MakeCleanFile();
        try
        {
            var edits = Service(fileMissing: true).ResolveResets(new[] { Rain() }, CleanMap(clean), out var error);

            Assert.Null(edits);
            Assert.Contains(DatPath, error);
        }
        finally { File.Delete(clean); }
    }

    [Fact]
    public void Ключа_нет_в_чистой_копии_сброс_отменяется_целиком()
    {
        var clean = MakeCleanFile();
        try
        {
            var partial = new Dictionary<string, string> { ["rain.NumberParticles"] = "16384" };
            var edits = Service(partial).ResolveResets(new[] { Rain() }, CleanMap(clean), out var error);

            Assert.Null(edits);
            Assert.Contains("rain.UseLitShader", error);
        }
        finally { File.Delete(clean); }
    }

    [Fact]
    public void Ошибка_чтения_архива_не_вылетает_наружу_а_становится_сообщением()
    {
        var clean = MakeCleanFile();
        try
        {
            var svc = new OptimizationModApplyService(
                "C:\\gta", null, (_, _) => throw new IOException("архив занят"));

            var edits = svc.ResolveResets(new[] { Rain() }, CleanMap(clean), out var error);

            Assert.Null(edits);
            Assert.Contains("архив занят", error);
        }
        finally { File.Delete(clean); }
    }

    [Fact]
    public async Task Сброс_проходит_через_ApplyAsync_и_не_считается_неподдержанным()
    {
        var clean = MakeCleanFile();
        try
        {
            var catalog = new OptimizationCatalog(
                new[] { Rain() }, new Dictionary<string, string>(),
                new Dictionary<string, string>
                {
                    [OptimizationCatalog.FileKey(DatPath, "rain.NumberParticles")] = "rain",
                    [OptimizationCatalog.FileKey(DatPath, "rain.UseLitShader")]    = "rain",
                });

            var svc = Service();
            var outcome = await svc.ApplyAsync(
                new[] { new OptimizationApplyService.Selection("rain", null) },
                catalog, CleanMap(clean));

            Assert.DoesNotContain(outcome.Skipped, s => s.Contains("не поддержан"));
            Assert.DoesNotContain(outcome.ErrorMessage ?? "", "не поддержан");
        }
        finally { File.Delete(clean); }
    }

    [Fact]
    public async Task Сброс_и_чужая_правка_того_же_ключа_это_конфликт()
    {
        var clean = MakeCleanFile();
        try
        {
            var other = ModGroup("other", Option(0, Edit("rain.NumberParticles", "0")));
            var catalog = new OptimizationCatalog(
                new[] { Rain(), other }, new Dictionary<string, string>(),
                new Dictionary<string, string>());

            var outcome = await Service().ApplyAsync(
                new[]
                {
                    new OptimizationApplyService.Selection("other", 0),
                    new OptimizationApplyService.Selection("rain", null),
                },
                catalog, CleanMap(clean));

            Assert.False(outcome.Success);
            Assert.Contains("обе правят", outcome.ErrorMessage);
        }
        finally { File.Delete(clean); }
    }
}
