using System.Linq;
using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class MajesticCrashLogTests
{
    private static string[] PedSpawnCrash() => new[]
    {
        "[23:40:25.057][Warning][sync] Failed to request model",
        "[23:40:25.058][Warning][sync] Failed to request model",
        "[23:40:25.076][Warning][sync] Failed to request model",
        "[23:40:24.451][Warning][hooks.pool] !!! CObjectSyncData pool (0xe4ccd07f) is full - limit is 80 elements !!!",
        "[23:40:25.586][hooks] MINIDUMP: Temporary dump saved",
        "[23:40:25.653][hooks] Last loaded asset: tat_rt_029_a_uni_hires.ytd tat_rt_028_a_uni_hires.ytd",
        "[23:40:25.653][hooks] Texture info: givemechecker lowr_diff_005_a_whi",
        "[23:40:25.653][hooks] Last pos Point{ x: -2471.99, y: 2945.79, z: 48.5267 }",
        "[23:40:25.653] Shutting down",
    };

    private static string[] OversizedCrash() => new[]
    {
        "[17:19:21][Error] Oversized file (>100MB) prop_sign_road_03e.yft",
        "[17:19:21][Error] ERR_STR_FAILURE_3 happened.",
        "[17:19:21][hooks] MINIDUMP: Temporary dump saved",
    };

    [Fact]
    public void Лавина_сбоев_синхронизации_читается_как_сборка_педов()
    {
        var c = MajesticCrashLog.Parse(PedSpawnCrash().Concat(
            Enumerable.Repeat("[23:40:25.100][Warning][sync] Failed to request model", 300)));

        Assert.NotNull(c);
        Assert.Equal("23:40:25.586", c!.Time);
        Assert.Equal(303, c.FailedModelRequests);
        Assert.False(c.Oversized);
        Assert.Contains("сборка педов", c.Signature);
        Assert.Contains("givemechecker", c.TextureInfo);
        Assert.Contains("tat_rt_029", c.LastAsset);
        Assert.Contains("-2471.99", c.Position);
    }

    [Fact]
    public void Oversized_читается_как_стриминг_и_называет_запись()
    {
        var c = MajesticCrashLog.Parse(OversizedCrash());

        Assert.NotNull(c);
        Assert.True(c!.Oversized);
        Assert.True(c.StreamingFailure);
        Assert.Equal("prop_sign_road_03e.yft", c.OversizedName);
        Assert.Contains("стриминг", c.Signature);
    }

    [Fact]
    public void Спокойная_сессия_не_считается_падением()
    {
        var lines = new[]
        {
            "[13:56:20.464] Logger initialized",
            "[13:57:35.746][Error][hooks] Duplicate weapon item: 1 984333226 dlcMPLTSCRC:/common/data/ai/weaponHeavyShotgun.meta",
            "[14:20:11.100][Warning][sync] Failed to request model",
            "[14:40:45.350] Shutting down",
        };

        Assert.Null(MajesticCrashLog.Parse(lines));
    }

    [Fact]
    public void Oversized_без_падения_тоже_находка()
    {
        var c = MajesticCrashLog.Parse(new[] { "[17:19:21][Error] Oversized file (>100MB) miami_weapon.rpf" });

        Assert.NotNull(c);
        Assert.Equal("miami_weapon.rpf", c!.OversizedName);
    }
}
