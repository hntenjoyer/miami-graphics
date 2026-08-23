using System;
using System.Linq;
using CodeWalker.GameFiles;
using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class RpfEntryGateTests
{
    private static byte[] Noise(int w, int h)
    {
        var d = new byte[w * h * 4];
        var rnd = new Random(1234);
        rnd.NextBytes(d);
        for (int i = 3; i < d.Length; i += 4) d[i] = 255;
        return d;
    }

    private static byte[] FatYtd(int count, int side)
    {
        var ytd = new YtdFile { TextureDict = new TextureDictionary() };
        var list = Enumerable.Range(0, count).Select(i =>
        {
            var tex = new Texture
            {
                Name = "tex" + i,
                NameHash = JenkHash.GenHash("tex" + i),
                Width = (ushort)side, Height = (ushort)side, Depth = 1, Levels = 1,
                Format = TextureFormat.D3DFMT_A8R8G8B8,
                Stride = (ushort)(side * 4),
                Data = new TextureData { FullData = Noise(side, side) },
                VFT = 2483783232, Unknown_4h = 32760, Unknown_30h = 1, Unknown_32h = 128,
                UsageData = 538269056,
            };
            return tex;
        }).ToList();
        ytd.TextureDict.BuildFromTextureList(list);
        return ytd.Save();
    }

    [Fact]
    public void Переросток_ужимается_и_проходит()
    {
        var fat = FatYtd(count: 8, side: 2048);
        Assert.True(RpfResourceBudget.MemorySize(fat) > RpfResourceBudget.RefuseAt,
            $"заготовка не переросток: {RpfResourceBudget.MemorySize(fat)} байт");

        var ok = RpfEntryGate.TryPrepare("w_ar_test.ytd", fat, out var ready, out var reason, out var note);

        Assert.True(ok, "ворота отказали: " + reason);
        Assert.NotNull(note);
        Assert.True(RpfResourceBudget.MemorySize(ready) <= RpfResourceBudget.RefuseAt,
            $"после ворот всё ещё {RpfResourceBudget.MemorySize(ready)} байт");
    }

    [Fact]
    public void Здоровый_ресурс_проходит_как_есть()
    {
        var slim = FatYtd(count: 1, side: 256);
        var ok = RpfEntryGate.TryPrepare("w_pi_test.ytd", slim, out var ready, out var reason, out var note);

        Assert.True(ok, reason);
        Assert.Null(note);
        Assert.Same(slim, ready);
    }

    [Fact]
    public void Битое_содержимое_под_ресурсным_именем_не_чинится()
    {
        var gfx = new byte[] { (byte)'G', (byte)'F', (byte)'X', 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var ok = RpfEntryGate.TryPrepare("prop_sign_road_03e.yft", gfx, out _, out var reason, out _);

        Assert.False(ok);
        Assert.Contains("RSC7", reason);
    }
}
