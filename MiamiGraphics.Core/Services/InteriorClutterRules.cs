#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MiamiGraphics.Core.Services;

public static class InteriorClutterRules
{
    private static readonly string[] Prefixes =
    {
        "v_ret_ml_beer",
        "v_ret_ml_cigs",
        "v_ret_ml_chips",
        "v_ret_ml_sweet",
        "v_ret_247_popcan",
        "v_ret_247_cigs",
        "v_ret_247_sweet",
        "prop_beer_",
        "prop_whiskey_",
        "prop_champ_",
        "prop_bottle_",
        "prop_food_",

        "v_51_clothing",
        "v_51_clothes",
        "v_51_v_clothes",
        "v_51_briefsbox",
        "v_51_masks",
        "prop_ftowel_",
        "v_res_fa_shoebox",

        "v_res_investbook",
        "v_ret_gc_folder",
        "v_ret_gc_pen",
        "v_res_paperfolder",
        "v_res_fashmag",
        "v_ret_gc_mags",
        "prop_folder_",
    };

    private static readonly HashSet<string> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        "beerrow_local", "beerrow_world", "winerow", "spiritsrow", "vodkarow",
        "v_res_binder", "v_ret_ta_jelly", "v_corp_bank_pen",
        "v_ret_gc_scissors", "v_ret_gc_calc", "v_ret_gc_staple",
        "v_ret_ml_liqshelfc", "v_ind_ss_box04", "p_tennis_bag_01_s",
    };

    private static readonly HashSet<string> Keep = new(StringComparer.OrdinalIgnoreCase)
    {
        "v_ret_ml_shelfrk",
        "v_ret_ml_fridge",
        "prop_food_bs_soda_01",
        "prop_bottle_water",
    };

    public static bool IsClutter(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (Keep.Contains(name)) return false;
        if (Exact.Contains(name)) return true;
        return Prefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
