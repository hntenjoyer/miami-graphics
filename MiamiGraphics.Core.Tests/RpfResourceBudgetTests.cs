using System;
using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class RpfResourceBudgetTests
{
    private static byte[] Rsc7(uint sysFlags, uint gfxFlags)
    {
        var d = new byte[16];
        d[0] = 0x52; d[1] = 0x53; d[2] = 0x43; d[3] = 0x37;
        BitConverter.GetBytes(165u).CopyTo(d, 4);
        BitConverter.GetBytes(sysFlags).CopyTo(d, 8);
        BitConverter.GetBytes(gfxFlags).CopyTo(d, 12);
        return d;
    }

    private const uint FatGraphics = 0x50FE0007;
    private const uint SmallSystem = 0xA0000012;

    [Fact]
    public void Размер_считается_из_флагов_страниц()
    {
        var size = RpfResourceBudget.MemorySize(Rsc7(SmallSystem, FatGraphics));
        Assert.True(size > 100L * 1024 * 1024, $"насчитали {size} байт");
    }

    [Fact]
    public void Жирный_ресурс_отклоняется()
    {
        var reason = RpfEntrySanity.RejectReason("w_ar_specialcarbinemk2.ydr",
            Rsc7(SmallSystem, FatGraphics));

        Assert.NotNull(reason);
        Assert.Contains("Oversized", reason);
    }

    [Fact]
    public void Нормальный_ресурс_проходит()
    {
        var ok = Rsc7(0xA0000012, 0x50040004);
        Assert.Null(RpfEntrySanity.RejectReason("w_pi_pistol.ydr", ok));
    }

    [Fact]
    public void Короткий_буфер_не_повод_для_отказа()
    {
        var head = new byte[] { 0x52, 0x53, 0x43, 0x37 };
        Assert.Null(RpfEntrySanity.RejectReason("w_pi_pistol.ydr", head));
        Assert.Equal(0, RpfResourceBudget.MemorySize(head));
    }

    [Fact]
    public void Двоичная_запись_размера_не_имеет()
    {
        Assert.Equal(0, RpfResourceBudget.MemorySize(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8,
                                                                  9, 10, 11, 12, 13, 14, 15, 16 }));
    }
}
