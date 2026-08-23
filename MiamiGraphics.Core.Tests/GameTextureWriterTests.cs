using CodeWalker.GameFiles;
using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class GameTextureWriterTests
{
    private static byte[] Bgra(int w, int h, byte alpha)
    {
        var d = new byte[w * h * 4];
        for (int i = 0; i < d.Length; i += 4)
        {
            d[i] = (byte)(i % 251); d[i + 1] = (byte)(i % 199); d[i + 2] = (byte)(i % 97);
            d[i + 3] = alpha;
        }
        return d;
    }

    [Fact]
    public void Непрозрачная_текстура_жмётся_в_DXT1_с_мипами()
    {
        var enc = GameTextureWriter.Encode(Bgra(256, 256, 255), 256, 256);

        Assert.Equal(TextureFormat.D3DFMT_DXT1, enc.Format);
        Assert.True(enc.Levels > 1, $"мипы не записаны (Levels={enc.Levels})");
        Assert.True(enc.Data.Length * 4 < 256 * 256 * 4,
            $"не сжалось: {enc.Data.Length} против {256 * 256 * 4} сырых");
    }

    [Fact]
    public void Текстура_с_альфой_жмётся_в_DXT5()
    {
        var enc = GameTextureWriter.Encode(Bgra(128, 128, 128), 128, 128);

        Assert.Equal(TextureFormat.D3DFMT_DXT5, enc.Format);
        Assert.True(enc.Data.Length * 2 < 128 * 128 * 4);
    }

    [Fact]
    public void Замена_не_имеет_права_быть_больше_оригинала()
    {
        var tex = new Texture { Width = 256, Height = 256, Name = "diff" };
        GameTextureWriter.Apply(tex, Bgra(2048, 2048, 255), 2048, 2048);

        Assert.Equal(256, tex.Width);
        Assert.Equal(256, tex.Height);
    }

    [Fact]
    public void Меньше_оригинала_остаётся_как_есть()
    {
        var tex = new Texture { Width = 1024, Height = 1024, Name = "diff" };
        GameTextureWriter.Apply(tex, Bgra(64, 64, 255), 64, 64);

        Assert.Equal(64, tex.Width);
        Assert.Equal(64, tex.Height);
    }

    [Fact]
    public void Сторона_не_кратная_четырём_остаётся_несжатой()
    {
        var enc = GameTextureWriter.Encode(Bgra(30, 30, 255), 30, 30);

        Assert.Equal(TextureFormat.D3DFMT_A8R8G8B8, enc.Format);
        Assert.Equal(30 * 30 * 4, enc.Data.Length);
    }

    [Fact]
    public void Потолок_2048_для_текстур_созданных_с_нуля()
    {
        var enc = GameTextureWriter.Encode(Bgra(4096, 64, 255), 4096, 64);

        Assert.Equal(GameTextureWriter.MaxSide, enc.Width);
        Assert.Equal(64, enc.Height);
    }

    [Fact]
    public void Формат_наследуется_от_заменяемой_текстуры()
    {
        var dxt5 = new Texture { Width = 512, Height = 512, Format = TextureFormat.D3DFMT_DXT5, Name = "mask" };
        Assert.Equal(GameTextureWriter.Policy.ForceBc3, GameTextureWriter.PolicyFor(dxt5));

        GameTextureWriter.Apply(dxt5, Bgra(512, 512, 255), 512, 512);
        Assert.Equal(TextureFormat.D3DFMT_DXT5, dxt5.Format);
    }

    [Fact]
    public void Мелкую_несжатую_не_трогаем()
    {
        var small = new Texture { Width = 128, Height = 128, Format = TextureFormat.D3DFMT_A8R8G8B8, Name = "tiny" };
        Assert.Equal(GameTextureWriter.Policy.KeepRaw, GameTextureWriter.PolicyFor(small));

        GameTextureWriter.Apply(small, Bgra(128, 128, 255), 128, 128);
        Assert.Equal(TextureFormat.D3DFMT_A8R8G8B8, small.Format);
    }

    [Fact]
    public void Крупную_несжатую_жмём_обязательно()
    {
        var big = new Texture { Width = 1024, Height = 1024, Format = TextureFormat.D3DFMT_A8R8G8B8, Name = "diff" };
        Assert.Equal(GameTextureWriter.Policy.Auto, GameTextureWriter.PolicyFor(big));

        GameTextureWriter.Apply(big, Bgra(1024, 1024, 255), 1024, 1024);
        Assert.NotEqual(TextureFormat.D3DFMT_A8R8G8B8, big.Format);
        Assert.True(big.Data.FullData.Length * 4 < 1024 * 1024 * 4);
    }

    [Fact]
    public void Мипы_считаются_до_единицы()
    {
        Assert.Equal(9, GameTextureWriter.MipCount(256, 256));
        Assert.Equal(1, GameTextureWriter.MipCount(1, 1));
        Assert.Equal(12, GameTextureWriter.MipCount(2048, 64));
    }
}
