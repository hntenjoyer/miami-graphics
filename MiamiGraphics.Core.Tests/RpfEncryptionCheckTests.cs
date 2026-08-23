using MiamiGraphics.Core.Injector;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class RpfEncryptionCheckTests
{
    private const uint Rpf7 = 0x52504637u;
    private const uint MarkerOpen = 0x4E45504Fu;
    private const uint MarkerNg = 0x0FEFFFFFu;

    private static string WriteRpf(uint marker, bool validToc, string name = "test.rpf")
    {
        var dir = Path.Combine(Path.GetTempPath(), "rpfcheck_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);

        var bytes = new byte[16 + 16];
        BitConverter.GetBytes(Rpf7).CopyTo(bytes, 0);
        BitConverter.GetBytes(1u).CopyTo(bytes, 4);
        BitConverter.GetBytes(0u).CopyTo(bytes, 8);
        BitConverter.GetBytes(marker).CopyTo(bytes, 12);
        if (validToc) BitConverter.GetBytes(0x7FFFFF00u).CopyTo(bytes, 16 + 4);

        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Открытый_архив_с_честным_маркером_читается()
    {
        var check = RpfEncryptionCheck.Inspect(WriteRpf(MarkerOpen, validToc: true));

        Assert.True(check.IsRpf7);
        Assert.Equal(RpfTocMode.Plain, check.Declared);
        Assert.Equal(RpfTocMode.Plain, check.Actual);
        Assert.True(check.GameCanRead);
    }

    [Fact]
    public void Маркер_врёт_про_шифрование_и_это_видно()
    {
        var check = RpfEncryptionCheck.Inspect(WriteRpf(MarkerNg, validToc: true));

        Assert.Equal(RpfTocMode.Ng, check.Declared);
        Assert.Equal(RpfTocMode.Plain, check.Actual);
        Assert.False(check.GameCanRead);
        Assert.Contains("Plain", check.Detail);
    }

    [Fact]
    public void Нечитаемое_оглавление_ловится()
    {
        var check = RpfEncryptionCheck.Inspect(WriteRpf(MarkerOpen, validToc: false));

        Assert.False(check.GameCanRead);
        Assert.Equal(RpfTocMode.Unknown, check.Actual);
    }

    [Fact]
    public void Открытый_архив_переживает_переименование()
    {
        var path = WriteRpf(MarkerOpen, validToc: true);

        Assert.True(RpfEncryptionCheck.WillSurviveRename(path, "miami_graphics_armor.rpf"));
    }

    [Fact]
    public void Не_RPF7_помечается_и_не_ломает_проверку()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rpfcheck_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "not_an_archive.rpf");
        File.WriteAllBytes(path, new byte[32]);

        var check = RpfEncryptionCheck.Inspect(path);

        Assert.False(check.IsRpf7);
        Assert.False(check.GameCanRead);
    }

    [Fact]
    public void Отсутствующий_файл_не_бросает()
    {
        var check = RpfEncryptionCheck.Inspect(Path.Combine(Path.GetTempPath(), "нет-такого-файла.rpf"));

        Assert.False(check.GameCanRead);
    }
}
