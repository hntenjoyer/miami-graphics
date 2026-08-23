using MiamiGraphics.Core.Services;
using Xunit;

namespace MiamiGraphics.Core.Tests;

public class InteriorClutterRulesTests
{
    [Theory]
    [InlineData("v_ret_ml_beeram")]
    [InlineData("v_ret_ml_cigs6")]
    [InlineData("v_ret_ml_chips3")]
    [InlineData("v_ret_ml_sweet7")]
    [InlineData("v_ret_247_popcan2")]
    [InlineData("beerrow_local")]
    [InlineData("spiritsrow")]
    [InlineData("vodkarow")]
    [InlineData("winerow")]
    [InlineData("prop_whiskey_bottle")]
    [InlineData("prop_champ_01a")]
    [InlineData("v_51_clothing04")]
    [InlineData("v_51_briefsbox")]
    [InlineData("prop_ftowel_07")]
    [InlineData("v_res_fa_shoebox2")]
    [InlineData("v_ret_gc_folder1")]
    [InlineData("v_res_fashmag1")]
    [InlineData("prop_folder_02")]
    public void Товар_с_полок_убирается(string name)
        => Assert.True(InteriorClutterRules.IsClutter(name));

    [Theory]
    [InlineData("v_ilev_ml_door1")]
    [InlineData("v_ilev_cs_door01")]
    [InlineData("v_10_liquorstore")]
    [InlineData("v_10_liquor_counter")]
    [InlineData("v_10_liquorfloorshelves")]
    [InlineData("v_51_counter")]
    [InlineData("v_51_benches")]
    [InlineData("v_66_shelves")]
    [InlineData("v_51_v_shadowmap")]
    [InlineData("v_51_reflectproxy")]
    [InlineData("v_10_dpwnlights")]
    [InlineData("prop_till_01")]
    [InlineData("v_ret_ml_fridge")]
    [InlineData("prop_food_bs_soda_01")]
    [InlineData("prop_fire_exting_1a")]
    [InlineData("prop_cctv_cam_06a")]
    public void Сам_зал_остаётся(string name)
        => Assert.False(InteriorClutterRules.IsClutter(name));

    [Fact]
    public void Неопознанное_имя_не_трогаем()
    {
        Assert.False(InteriorClutterRules.IsClutter(null));
        Assert.False(InteriorClutterRules.IsClutter(""));
        Assert.False(InteriorClutterRules.IsClutter("что_то_невиданное"));
    }

    [Fact]
    public void Стеллаж_не_попадает_под_товарный_префикс()
    {
        Assert.False(InteriorClutterRules.IsClutter("v_ret_ml_shelfrk"));
        Assert.True(InteriorClutterRules.IsClutter("v_ret_ml_beerbar"));
    }

    [Fact]
    public void Регистр_имени_не_важен()
    {
        Assert.True(InteriorClutterRules.IsClutter("V_Ret_ML_BeerAm"));
        Assert.True(InteriorClutterRules.IsClutter("SPIRITSROW"));
    }
}
