using System.Text;
using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class RpfEntrySanityTests
{
    private static byte[] Rsc7(int extra = 16)
    {
        var b = new byte[4 + extra];
        b[0] = 0x52; b[1] = 0x53; b[2] = 0x43; b[3] = 0x37;
        return b;
    }

    private static byte[] Rpf7(int extra = 16)
    {
        var b = new byte[4 + extra];
        b[0] = 0x37; b[1] = 0x46; b[2] = 0x50; b[3] = 0x52;
        return b;
    }

    private static byte[] Gfx() => Encoding.ASCII.GetBytes("GFX\x03" + new string('\0', 16));

    [Theory]
    [InlineData("weapon_carbine.ydr")]
    [InlineData("WEAPON_CARBINE.YDR")]
    [InlineData("prop_sign_road_03e.yft")]
    [InlineData("gun_diff.ytd")]
    [InlineData("core.ypt")]
    public void Ресурс_с_правильным_заголовком_проходит(string name)
        => Assert.Null(RpfEntrySanity.RejectReason(name, Rsc7()));

    [Theory]
    [InlineData("prop_sign_road_03e.yft")]
    [InlineData("weapon_carbine.ydr")]
    [InlineData("gun_diff.ytd")]
    public void Ресурсное_имя_с_чужим_содержимым_отклоняется(string name)
    {
        var reason = RpfEntrySanity.RejectReason(name, Gfx());
        Assert.NotNull(reason);
        Assert.Contains("не RSC7", reason);
    }

    [Fact]
    public void Rsc5_за_ресурс_не_сходит()
    {
        var rsc5 = new byte[] { 0x52, 0x53, 0x43, 0x35, 0, 0, 0, 0 };
        Assert.False(RpfEntrySanity.IsRsc7(rsc5));
        Assert.NotNull(RpfEntrySanity.RejectReason("weapon.ydr", rsc5));
    }

    [Fact]
    public void Настоящий_вложенный_архив_проходит()
        => Assert.Null(RpfEntrySanity.RejectReason("hunter_guns_selected.rpf", Rpf7()));

    [Fact]
    public void Магии_ресурса_и_архива_не_путаются()
    {
        Assert.True(RpfEntrySanity.IsRsc7(Rsc7()));
        Assert.False(RpfEntrySanity.IsRpf7(Rsc7()));
        Assert.True(RpfEntrySanity.IsRpf7(Rpf7()));
        Assert.False(RpfEntrySanity.IsRsc7(Rpf7()));
    }

    [Fact]
    public void Фейковый_вложенный_архив_отклоняется()
    {
        var reason = RpfEntrySanity.RejectReason("new.rpf", new byte[512]);
        Assert.NotNull(reason);
        Assert.Contains("RPF7", reason);
    }

    [Theory]
    [InlineData("weapons.meta")]
    [InlineData("vehicles.meta")]
    [InlineData("readme.txt")]
    [InlineData("audio.awc")]
    public void Не_ресурсные_имена_пропускаются_с_любым_содержимым(string name)
    {
        Assert.Null(RpfEntrySanity.RejectReason(name, Gfx()));
        Assert.Null(RpfEntrySanity.RejectReason(name, new byte[] { 1, 2, 3, 4 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("@825B:0:45;0")]
    [InlineData("weapon.ydr")]
    public void Имена_с_управляющими_символами_отклоняются(string name)
    {
        var reason = RpfEntrySanity.RejectReason(name, Rsc7());
        Assert.NotNull(reason);
        Assert.Contains("непечатное имя", reason);
    }

    [Fact]
    public void Не_ascii_имя_на_записи_пропускается_но_отличимо()
    {
        Assert.Null(RpfEntrySanity.RejectReason("текстура.ytd", Rsc7()));
        Assert.False(RpfEntrySanity.NameIsGarbage("текстура.ytd"));
        Assert.True(RpfEntrySanity.NameIsNonAscii("·@825B:0:45;0"));
    }

    [Fact]
    public void Пустое_и_короткое_содержимое_под_ресурсным_именем_отклоняется()
    {
        Assert.NotNull(RpfEntrySanity.RejectReason("weapon.ydr", new byte[0]));
        Assert.NotNull(RpfEntrySanity.RejectReason("weapon.ydr", new byte[] { 0x52, 0x53 }));
        Assert.NotNull(RpfEntrySanity.RejectReason("weapon.ydr", null));
    }
}
