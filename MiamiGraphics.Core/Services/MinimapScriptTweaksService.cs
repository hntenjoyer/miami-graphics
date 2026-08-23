using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MiamiGraphics.Core.Services
{
    public sealed record MinimapTweaks(
        bool Digits = false,
        string? DigitsHpColor = null,
        string? DigitsArmorColor = null,
        double DigitsX = 10,
        double DigitsY = 103,
        double DigitsScale = 100,
        double DigitsHpDx = 0,
        double DigitsHpDy = 0,
        double DigitsArmorDx = 22,
        double DigitsArmorDy = 0,
        double DigitsBigDx = 0,
        double DigitsBigDy = 0,
        bool DamagePopup = false,
        string DamageColor = "#FF4040",
        bool HealPopup = false,
        string HealColor = "#34D399",
        double PopupSize = 18,
        double PopupSeconds = 1.0,
        double PopupX = 46,
        double PopupY = 34,
        int? LowHpThreshold = null,
        string? LowHpColor = null,
        int? HitAlpha = null,
        double? HitFadeSeconds = null,
        double? HitScale = null,
        string BarPosition = "default",
        double BarOffsetX = 0,
        double BarOffsetY = 0,
        bool ArmorPopup = false,
        string ArmorPopupColor = "#60A5FA",
        double ArmorPopupX = 46,
        double ArmorPopupY = 54,
        string? CustomText = null,
        string CustomTextColor = "#FFFFFF",
        double CustomTextX = 4,
        double CustomTextY = 2,
        double CustomTextScale = 100,
        double BarScale = 100,
        string? BarHpColor = null,
        string? BarArmorColor = null,
        bool BarPulseLowHp = false,
        bool BarHpGradient = false,
        string? BarGradFullColor = null,
        string? BarGradMidColor = null,
        string? BarGradLowColor = null,
        double? BarScaleY = null,
        bool HideNorth = false,
        bool HitNoRedTint = false,
        string? DigitsFont = null,

        double? HitX = null,
        double? HitY = null,

        bool HitArtBaked = false,

        string? BarHpTroughColor = null,
        string? BarArmorTroughColor = null);

    public static class MinimapScriptTweaksService
    {
        public static bool AlreadyTweaked(string asText) =>
            !string.IsNullOrEmpty(asText) &&
            (asText.Contains("__MGTWEAK__", StringComparison.Ordinal) ||
             asText.Contains("\"mgPopMC\"", StringComparison.Ordinal) ||
             asText.Contains("__mgPop_beg", StringComparison.Ordinal) ||
             asText.Contains("__mgBar_beg", StringComparison.Ordinal));

        public static bool AnyRequested(MinimapTweaks? t) =>
            t is not null && (
                t.Digits || t.DamagePopup || t.HealPopup || t.ArmorPopup ||
                !string.IsNullOrWhiteSpace(t.CustomText) ||
                t.LowHpThreshold is not null || t.LowHpColor is not null ||
                t.HitAlpha is not null || t.HitFadeSeconds is not null || t.HitScale is not null ||
                t.HitX is not null || t.HitY is not null ||
                !string.Equals(t.BarPosition, "default", StringComparison.OrdinalIgnoreCase) ||
                Math.Abs(t.BarOffsetX) > 0.01 || Math.Abs(t.BarOffsetY) > 0.01 ||
                Math.Abs(t.BarScale - 100) > 0.01 || t.BarHpColor is not null ||
                t.BarArmorColor is not null || t.BarPulseLowHp || t.BarHpGradient ||
                t.BarScaleY is not null ||
                t.BarHpTroughColor is not null || t.BarArmorTroughColor is not null ||
                t.HideNorth);

        public static string Apply(string asText, MinimapTweaks t, List<string> notes, out bool changed)
        {
            string original = asText;
            asText = StripPrevious(asText);

            if (t.Digits) asText = ApplyDigits(asText, t, notes);
            if (t.DamagePopup || t.HealPopup || t.ArmorPopup) asText = ApplyPopups(asText, t, notes);
            if (!string.IsNullOrWhiteSpace(t.CustomText)) asText = ApplyCustomText(asText, t, notes);
            asText = ApplyLowHp(asText, t, notes);
            asText = ApplyHitTuning(asText, t, notes);
            asText = ApplyBarStyle(asText, t, notes);
            asText = ApplyBarFlatColor(asText, t, notes);
            asText = ApplyBarGradient(asText, t, notes);
            asText = ApplyBarPosition(asText, t, notes);
            asText = ApplyBarCosmetics(asText, t, notes);
            asText = ApplyAuthorHud(asText, notes);

            changed = !string.Equals(original, asText, StringComparison.Ordinal);
            return asText;
        }

        private static string Block(string name, string body) =>
            $"var __mg{name}_beg = 0;\n      var __mg{name}_tag = \"__MGTWEAK__\";\n{body}\n      var __mg{name}_end = 0;";

        private static string StripPrevious(string s)
        {
            s = Regex.Replace(s, @"[ \t]*var __mg\w+_beg = 0;[\s\S]*?var __mg\w+_end = 0;\r?\n?", "");
            s = Regex.Replace(s, @"[ \t]*var __mg\w+ = [^\n]*\n", "");
            return s;
        }

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static (int r, int g, int b) Hex(string? hex, (int, int, int) fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var h = hex.TrimStart('#');
            if (h.Length != 6) return fallback;
            try
            {
                return (Convert.ToInt32(h.Substring(0, 2), 16),
                        Convert.ToInt32(h.Substring(2, 2), 16),
                        Convert.ToInt32(h.Substring(4, 2), 16));
            }
            catch { return fallback; }
        }

        private static IEnumerable<string> FontLines(string field, string embedName, string varSuffix)
        {
            string v = "__mgF" + varSuffix;
            yield return $"var {v} = {field}.getTextFormat();";
            yield return $"{v}.font = \"{embedName}\";";
            yield return $"{field}.embedFonts = true;";
            yield return $"{field}.setNewTextFormat({v});";
            yield return $"{field}.setTextFormat({v});";
        }

        private static string HexNum(string? hex, string fallback = "0xFFFFFF")
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var h = hex.TrimStart('#');
            return h.Length == 6 ? "0x" + h.ToUpperInvariant() : fallback;
        }

        private static (int start, int end) FindFnBody(string s, string fnSignature)
        {
            int fnStart = s.IndexOf(fnSignature, StringComparison.Ordinal);
            if (fnStart < 0) return (-1, -1);
            int bodyStart = s.IndexOf('{', fnStart);
            if (bodyStart < 0) return (-1, -1);
            int depth = 0, i = bodyStart;
            for (; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}') { depth--; if (depth == 0) break; }
            }
            return depth == 0 ? (bodyStart, i) : (-1, -1);
        }

        private static List<(int start, int end)> FindAllFnBodies(string s, string fnSignature)
        {
            var res = new List<(int, int)>();
            int from = 0;
            while (from < s.Length)
            {
                int fnStart = s.IndexOf(fnSignature, from, StringComparison.Ordinal);
                if (fnStart < 0) break;
                int bodyStart = s.IndexOf('{', fnStart);
                if (bodyStart < 0) break;
                int depth = 0, i = bodyStart;
                for (; i < s.Length; i++)
                {
                    if (s[i] == '{') depth++;
                    else if (s[i] == '}') { depth--; if (depth == 0) break; }
                }
                if (depth != 0) break;
                res.Add((bodyStart, i));
                from = i + 1;
            }
            res.Reverse();
            return res;
        }

        private static List<int> AllIndexes(string s, string needle)
        {
            var res = new List<int>();
            int from = 0;
            while (from < s.Length)
            {
                int i = s.IndexOf(needle, from, StringComparison.Ordinal);
                if (i < 0) break;
                res.Add(i);
                from = i + needle.Length;
            }
            res.Reverse();
            return res;
        }

        private static string ReplaceFnBody(string s, string fnSignature, string newBody, List<string> notes)
        {
            var all = FindAllFnBodies(s, fnSignature);
            if (all.Count == 0) { notes.Add($"[Минимапа] Функция не найдена: {fnSignature}"); return s; }
            foreach (var (b, e) in all)
                s = s.Substring(0, b) + "{\n" + newBody + "\n   }" + s.Substring(e + 1);
            return s;
        }

        private static string InsertAtFnEnd(string s, string fnSignature, string code, List<string> notes)
        {
            var all = FindAllFnBodies(s, fnSignature);
            if (all.Count == 0) { notes.Add($"[Минимапа] Функция не найдена: {fnSignature}"); return s; }
            foreach (var (_, e) in all)
                s = s.Substring(0, e) + "      " + code + "\n   " + s.Substring(e);
            return s;
        }

        private static string InsertAtFnStart(string s, string fnSignature, string code, List<string> notes)
        {
            var all = FindAllFnBodies(s, fnSignature);
            if (all.Count == 0) { notes.Add($"[Минимапа] Функция не найдена: {fnSignature}"); return s; }
            foreach (var (b, _) in all)
                s = s.Substring(0, b + 1) + "\n      " + code + s.Substring(b + 1);
            return s;
        }

        private static string InsertAfterAnchor(string s, string anchor, string code, List<string> notes, string what)
        {
            var idx = AllIndexes(s, anchor);
            if (idx.Count == 0) { notes.Add($"[Минимапа] {what}: якорь не найден, пропущено."); return s; }
            foreach (int i in idx)
            {
                int end = i + anchor.Length;
                s = s.Substring(0, end) + "\n      " + code + s.Substring(end);
            }
            return s;
        }

        private static string ApplyDigits(string s, MinimapTweaks t, List<string> notes)
        {
            bool hackPresent = s.Contains("distanceToSurfaceTF.text = newHealthValue")
                            || s.Contains("distanceToSurfaceTF.text = this.currentHealthValue");

            if (!hackPresent)
            {
                s = InsertAtFnStart(s, "function SET_PLAYER_HEALTH(",
                    Block("DigH", "this.DEPTHGUAGE.distanceToSurfaceTF.text = newHealthValue;"),
                    notes);
                s = InsertAfterAnchor(s,
                    "this.currentHealthValue = newHealthValue;",
                    Block("DigH2", "this.DEPTHGUAGE.distanceToSurfaceTF.text = this.currentHealthValue;"),
                    notes, "Цифры HP (обновление)");
                s = InsertAtFnStart(s, "function SET_PLAYER_ARMOUR(",
                    Block("DigA", "this.DEPTHGUAGE.distanceToFloorTF.text = newArmourValue;"),
                    notes);
                s = InsertAfterAnchor(s,
                    "this.currentArmourValue = newArmourValue;",
                    Block("DigA2", "this.DEPTHGUAGE.distanceToFloorTF.text = this.currentArmourValue;"),
                    notes, "Цифры брони (обновление)");

                string show =
                    "      this.DEPTHGUAGE = this.healthContainer.attachMovie(\"depth_bar\",\"depth_bar\",3,{_x:10,_y:103});\n" +
                    "      this.DEPTHGUAGE.distanceToSurfaceTF.text = this.currentHealthValue;\n" +
                    "      this.DEPTHGUAGE.distanceToFloorTF.text = this.currentArmourValue;\n" +
                    "      this.DEPTHGUAGE.depgthRedBG._visible = false;";
                s = ReplaceFnBody(s, "function SHOW_DEPTH()", show, notes);
                s = ReplaceFnBody(s, "function SET_DEPTH(distanceToSurface, distanceToFloor, isMetric, warning)",
                    "      this.SHOW_DEPTH();", notes);
                s = ReplaceFnBody(s, "function HIDE_DEPTH()", "      this.SHOW_DEPTH();", notes);

                var (sb, _) = FindFnBody(s, "function SETUP_HEALTH_ARMOUR(");
                if (sb >= 0)
                {
                    var (b2, e2) = FindFnBody(s, "function SETUP_HEALTH_ARMOUR(");
                    string setupBody = s.Substring(b2, e2 - b2 + 1);
                    if (!setupBody.Contains("attachMovie(\"depth_bar\""))
                        s = InsertAtFnEnd(s, "function SETUP_HEALTH_ARMOUR(",
                            Block("DigS", "this.SHOW_DEPTH();"), notes);
                }
            }

            string init = $"{{_x:{F(t.DigitsX)},_y:{F(t.DigitsY)},_xscale:{F(t.DigitsScale)},_yscale:{F(t.DigitsScale)}}}";
            s = Regex.Replace(s,
                "attachMovie\\(\"depth_bar\",\"depth_bar\",3,\\{[^}]*\\}\\)",
                "attachMovie(\"depth_bar\",\"depth_bar\",3," + init + ")");

            var styleLines = new List<string>
            {
                "for(var __mgK in this.DEPTHGUAGE)",
                "{",
                "   if(typeof this.DEPTHGUAGE[__mgK] == \"movieclip\")",
                "   {",
                "      this.DEPTHGUAGE[__mgK]._visible = false;",
                "   }",
                "}",
                "this.DEPTHGUAGE.distanceToSurfaceTF.border = false;",
                "this.DEPTHGUAGE.distanceToSurfaceTF.background = false;",
                "this.DEPTHGUAGE.distanceToFloorTF.border = false;",
                "this.DEPTHGUAGE.distanceToFloorTF.background = false;",
                "this.DEPTHGUAGE.distanceToSurfaceTF.autoSize = \"left\";",
                "this.DEPTHGUAGE.distanceToFloorTF.autoSize = \"left\";",
                $"this.DEPTHGUAGE.distanceToSurfaceTF._x = {F(t.DigitsHpDx)};",
                $"this.DEPTHGUAGE.distanceToSurfaceTF._y = {F(t.DigitsHpDy)};",
                $"this.DEPTHGUAGE.distanceToFloorTF._x = {F(t.DigitsArmorDx)};",
                $"this.DEPTHGUAGE.distanceToFloorTF._y = {F(t.DigitsArmorDy)};",
            };
            styleLines.Add("if(this.mapType != 1)");
            styleLines.Add("{");
            styleLines.Add("   this.DEPTHGUAGE._visible = false;");
            styleLines.Add("   this.DEPTHGUAGE._x = 9999;");
            styleLines.Add("}");
            if (t.DigitsHpColor is not null)
                styleLines.Add($"this.DEPTHGUAGE.distanceToSurfaceTF.textColor = {HexNum(t.DigitsHpColor)};");
            if (t.DigitsArmorColor is not null)
                styleLines.Add($"this.DEPTHGUAGE.distanceToFloorTF.textColor = {HexNum(t.DigitsArmorColor)};");

            if (styleLines.Count > 0)
            {
                string style = Block("DigStyle", "      " + string.Join("\n      ", styleLines));
                s = Regex.Replace(s,
                    "(this\\.DEPTHGUAGE = this\\.healthContainer\\.attachMovie\\(\"depth_bar\"[^;]*;)",
                    "$1\n      " + style.Replace("$", "$$"));
            }

            s = HideAuthorDigits(s, notes);
            return s;
        }

        private static string HideAuthorDigits(string s, List<string> notes)
        {
            static string Kill(string name, string field) =>
                $"if(typeof this.HEALTH_ARMOUR_ABILITY.{name}.text == \"string\")\n" +
                "         {\n" +
                $"            this.HEALTH_ARMOUR_ABILITY.{name}.text = \"\";\n" +
                $"            this.HEALTH_ARMOUR_ABILITY.{name}._visible = false;\n" +
                $"            this.{field} = undefined;\n" +
                "         }";

            string body =
                "if(this.HEALTH_ARMOUR_ABILITY != undefined)\n" +
                "      {\n" +
                "         " + Kill("healthNum", "HEALTHNUM") + "\n" +
                "         " + Kill("armourNum", "ARMOURNUM") + "\n" +
                "      }";
            return InsertAtFnEnd(s, "function SETUP_HEALTH_ARMOUR(",
                Block("AuthNum", "      " + body), notes);
        }

        private static string PopupBlock(
            string name, int depth, string deltaExpr,
            bool dmgOn, string dmgColor, bool healOn, string healColor,
            double x, double y, double size, double secs)
        {
            string scale = F(size / 18.0 * 100);
            return
                "if(this.healthContainer != undefined)\n" +
                "      {\n" +
                $"         var __mgDelta = {deltaExpr};\n" +
                "         var __mgShow = 0;\n" +
                $"         if(__mgDelta > 0 && __mgDelta <= 100 && {(dmgOn ? "true" : "false")})\n" +
                "         {\n" +
                "            __mgShow = 1;\n" +
                "         }\n" +
                $"         if(__mgDelta < 0 && __mgDelta >= -100 && {(healOn ? "true" : "false")})\n" +
                "         {\n" +
                "            __mgShow = 2;\n" +
                "         }\n" +
                "         if(__mgShow > 0)\n" +
                "         {\n" +
                $"            com.rockstargames.ui.tweenStar.TweenStarLite.removeTweenOf(this.healthContainer.{name});\n" +
                $"            this.healthContainer.{name}.removeMovieClip();\n" +
                $"            var __mgPop = this.healthContainer.attachMovie(\"depth_bar\",\"{name}\",{depth},{{_x:{F(x)},_y:{F(y)},_xscale:{scale},_yscale:{scale}}});\n" +
                "            var __mgRise = 8;\n" +
                "            if(this.mapType != 1 && this.HEALTH_ARMOUR_BAR_MC != undefined)\n" +
                "            {\n" +
                "               var __mgBigB = this.HEALTH_ARMOUR_BAR_MC;\n" +
                $"               __mgPop._x = __mgBigB._x + ({F(x)} - 6) * __mgBigB._xscale / 100;\n" +
                $"               __mgPop._y = __mgBigB._y + ({F(y)} - 126) * __mgBigB._yscale / 100;\n" +
                $"               __mgPop._xscale = {scale} * __mgBigB._xscale / 100;\n" +
                $"               __mgPop._yscale = {scale} * __mgBigB._yscale / 100;\n" +
                "               __mgRise = 8 * __mgBigB._yscale / 100;\n" +
                "            }\n" +
                "            for(var __mgK in __mgPop)\n" +
                "            {\n" +
                "               if(typeof __mgPop[__mgK] == \"movieclip\")\n" +
                "               {\n" +
                "                  __mgPop[__mgK]._visible = false;\n" +
                "               }\n" +
                "            }\n" +
                "            __mgPop.distanceToFloorTF.text = \"\";\n" +
                "            __mgPop.distanceToFloorTF.border = false;\n" +
                "            __mgPop.distanceToFloorTF.background = false;\n" +
                "            __mgPop.distanceToSurfaceTF.border = false;\n" +
                "            __mgPop.distanceToSurfaceTF.background = false;\n" +
                "            __mgPop.distanceToSurfaceTF.autoSize = \"left\";\n" +
                "            __mgPop.distanceToSurfaceTF._x = 0;\n" +
                "            __mgPop.distanceToSurfaceTF._y = 0;\n" +
                "            __mgPop._alpha = 100;\n" +
                "            if(__mgShow == 1)\n" +
                "            {\n" +
                $"               __mgPop.distanceToSurfaceTF.textColor = {dmgColor};\n" +
                "               __mgPop.distanceToSurfaceTF.text = \"-\" + __mgDelta;\n" +
                "            }\n" +
                "            else\n" +
                "            {\n" +
                $"               __mgPop.distanceToSurfaceTF.textColor = {healColor};\n" +
                "               __mgPop.distanceToSurfaceTF.text = \"+\" + (- __mgDelta);\n" +
                "            }\n" +
                $"            com.rockstargames.ui.tweenStar.TweenStarLite.to(__mgPop,{F(secs)},{{_alpha:0,_y:__mgPop._y - __mgRise}});\n" +
                "         }\n" +
                "      }";
        }

        private static string ApplyPopups(string s, MinimapTweaks t, List<string> notes)
        {
            if (t.DamagePopup || t.HealPopup)
            {
                string body = PopupBlock(
                    "mgPopMC", 41,
                    "Math.round(this.currentHealthValue - newHealthValue)",
                    t.DamagePopup, HexNum(t.DamageColor, "0xFF4040"),
                    t.HealPopup, HexNum(t.HealColor, "0x34D399"),
                    t.PopupX, t.PopupY, t.PopupSize, t.PopupSeconds);
                s = InsertBeforeAnchor(s, "this.currentHealthValue = newHealthValue;",
                    Block("Pop", "      " + body), notes, "Всплывающий урон (HP)");
            }
            if (t.ArmorPopup)
            {
                string body = PopupBlock(
                    "mgPopArMC", 42,
                    "Math.round(this.currentArmourValue - newArmourValue)",
                    true, HexNum(t.ArmorPopupColor, "0x60A5FA"),
                    false, "0x34D399",
                    t.ArmorPopupX, t.ArmorPopupY, t.PopupSize, t.PopupSeconds);
                s = InsertBeforeAnchor(s, "this.currentArmourValue = newArmourValue;",
                    Block("PopAr", "      " + body), notes, "Всплывающий урон (броня)");
            }
            return s;
        }

        private static string InsertBeforeAnchor(string s, string anchor, string code, List<string> notes, string what)
        {
            var idx = AllIndexes(s, anchor);
            if (idx.Count == 0) { notes.Add($"[Минимапа] {what}: якорь не найден, пропущено."); return s; }
            foreach (int i in idx)
                s = s.Substring(0, i) + code + "\n      " + s.Substring(i);
            return s;
        }

        private static string ApplyAuthorHud(string s, List<string> notes)
        {
            string body =
                "if(this.HEALTH_ARMOUR_ABILITY != undefined)\n" +
                "      {\n" +
                "         if(typeof this.HEALTH_ARMOUR_ABILITY.healthHitMC.text == \"string\")\n" +
                "         {\n" +
                "            this.HEALTH_ARMOUR_ABILITY.healthHitMC.text = \"\";\n" +
                "            this.HEALTHHITMC = undefined;\n" +
                "         }\n" +
                "         if(typeof this.HEALTH_ARMOUR_ABILITY.armourHitMC.text == \"string\")\n" +
                "         {\n" +
                "            this.HEALTH_ARMOUR_ABILITY.armourHitMC.text = \"\";\n" +
                "            this.ARMOURHITMC = undefined;\n" +
                "         }\n" +
                "      }";
            s = InsertAtFnEnd(s, "function SETUP_HEALTH_ARMOUR(", Block("AuthHud", body), notes);

            static string Mute(string name) =>
                "if(this.HEALTH_ARMOUR_ABILITY != undefined)\n" +
                "      {\n" +
                $"         if(typeof this.HEALTH_ARMOUR_ABILITY.{name}.text == \"string\")\n" +
                "         {\n" +
                $"            this.HEALTH_ARMOUR_ABILITY.{name}.text = \"\";\n" +
                "         }\n" +
                "      }";
            s = InsertAtFnEnd(s, "function SET_HEALTH_DAMAGE_VISIBLE(",
                Block("AuthHudHitHp", Mute("healthHitMC")), notes);
            s = InsertAtFnEnd(s, "function SET_ARMOUR_DAMAGE_VISIBLE(",
                Block("AuthHudHitAr", Mute("armourHitMC")), notes);
            return s;
        }

        private static string ApplyCustomText(string s, MinimapTweaks t, List<string> notes)
        {
            string txt = new string((t.CustomText ?? "")
                .Replace("\\", "").Replace("\"", "").Replace("\r", " ").Replace("\n", " ")
                .Take(32).ToArray()).Trim();
            if (txt.Length == 0) return s;

            double sc = Math.Clamp(t.CustomTextScale, 20, 400);
            string body =
                "if(this.healthContainer != undefined)\n" +
                "      {\n" +
                "         this.healthContainer.mgTxtMC.removeMovieClip();\n" +
                $"         var __mgTxt = this.healthContainer.attachMovie(\"depth_bar\",\"mgTxtMC\",43,{{_x:{F(t.CustomTextX)},_y:{F(t.CustomTextY)},_xscale:{F(sc)},_yscale:{F(sc)}}});\n" +
                "         for(var __mgK in __mgTxt)\n" +
                "         {\n" +
                "            if(typeof __mgTxt[__mgK] == \"movieclip\")\n" +
                "            {\n" +
                "               __mgTxt[__mgK]._visible = false;\n" +
                "            }\n" +
                "         }\n" +
                "         __mgTxt.distanceToFloorTF.text = \"\";\n" +
                "         __mgTxt.distanceToFloorTF.border = false;\n" +
                "         __mgTxt.distanceToFloorTF.background = false;\n" +
                "         __mgTxt.distanceToSurfaceTF.border = false;\n" +
                "         __mgTxt.distanceToSurfaceTF.background = false;\n" +
                "         __mgTxt.distanceToSurfaceTF.autoSize = \"left\";\n" +
                "         __mgTxt.distanceToSurfaceTF._x = 0;\n" +
                "         __mgTxt.distanceToSurfaceTF._y = 0;\n" +
                $"         __mgTxt.distanceToSurfaceTF.textColor = {HexNum(t.CustomTextColor)};\n" +
                $"         __mgTxt.distanceToSurfaceTF.text = \"{txt}\";\n" +
                "      }";
            return InsertAtFnEnd(s, "function SETUP_HEALTH_ARMOUR(", Block("Txt", body), notes);
        }

        private static string ApplyLowHp(string s, MinimapTweaks t, List<string> notes)
        {
            if (t.LowHpThreshold is int th)
            {
                th = Math.Clamp(th, 1, 99);
                string before = s;
                s = s.Replace("newHealthValue <= 25", $"newHealthValue <= {th}");
                s = s.Replace("newHealthValue > 25", $"newHealthValue > {th}");
                if (ReferenceEquals(before, s) || before == s)
                    notes.Add("[Минимапа] Порог «мало HP»: стоковые сравнения не найдены (мод уже менял порог?).");
            }
            if (t.LowHpColor is not null)
            {
                var (r, g, b) = Hex(t.LowHpColor, (194, 80, 80));
                string assign = $"this.colourRed = {{r:{r},g:{g},b:{b}}};";

                var redCall = new Regex(
                    @"[^\r\n]*setHudColour\([^)]*HUD_COLOUR_RED\s*,\s*this\.colourRed\s*\)\s*;");
                var m = redCall.Match(s);
                if (m.Success)
                {
                    s = s.Substring(0, m.Index + m.Length)
                      + "\n            " + Block("LowHp", "            " + assign)
                      + s.Substring(m.Index + m.Length);
                }
                else
                {
                    s = InsertAtFnStart(s, "function SETUP_HEALTH_ARMOUR(",
                        Block("LowHp", assign), notes);
                }
            }
            return s;
        }

        private static string ApplyHitTuning(string s, MinimapTweaks t, List<string> notes)
        {
            if (t.HitAlpha is int a)
            {
                a = Math.Clamp(a, 0, 100);
                const string anchor = "healthHitMC._alpha = 75;";
                if (!s.Contains(anchor, StringComparison.Ordinal))
                    notes.Add("[Минимапа] Сила хит-вспышки: стоковая строка не найдена.");
                else
                    s = s.Replace(anchor, $"healthHitMC._alpha = {a};");
            }
            if (t.HitFadeSeconds is double f)
            {
                f = Math.Clamp(f, 0.1, 10);
                const string anchor = "fadeTime = 1.5;";
                if (!s.Contains(anchor, StringComparison.Ordinal))
                    notes.Add("[Минимапа] Затухание хит-вспышки: стоковая строка не найдена.");
                else
                    s = s.Replace(anchor, $"fadeTime = {F(f)};");
            }
            if (t.HitNoRedTint)
            {
                s = InsertAfterAnchor(s,
                    "this.HEALTH_ARMOUR_BAR_MC = this.HEALTH_ARMOUR_ABILITY.healthArmourMC;",
                    Block("HitTint",
                        "      if(this.HEALTH_ARMOUR_ABILITY.healthHitMC != undefined)\n" +
                        "      {\n" +
                        "         com.rockstargames.ui.utils.Colour.Colourise(this.HEALTH_ARMOUR_ABILITY.healthHitMC,255,255,255,100);\n" +
                        "      }"),
                    notes, "Цвета PNG вспышки");
            }
            if (t.HitScale is double sc && !t.HitArtBaked)
            {
                sc = Math.Clamp(sc, 10, 400);
                s = InsertAtFnEnd(s, "function SETUP_HEALTH_ARMOUR(",
                    Block("HitScale",
                        "if(this.HEALTH_ARMOUR_ABILITY.healthHitMC != undefined)\n" +
                        "      {\n" +
                        "         var __mgHit = this.HEALTH_ARMOUR_ABILITY.healthHitMC;\n" +
                        "         if(__mgHit.__mgBaseSx == undefined)\n" +
                        "         {\n" +
                        "            __mgHit.__mgBaseSx = __mgHit._xscale;\n" +
                        "            __mgHit.__mgBaseSy = __mgHit._yscale;\n" +
                        "         }\n" +
                        "         var __mgHb0 = __mgHit.getBounds(__mgHit._parent);\n" +
                        $"         __mgHit._xscale = __mgHit.__mgBaseSx * {F(sc)} / 100;\n" +
                        $"         __mgHit._yscale = __mgHit.__mgBaseSy * {F(sc)} / 100;\n" +
                        "         var __mgHb1 = __mgHit.getBounds(__mgHit._parent);\n" +
                        "         __mgHit._x += (__mgHb0.xMin + __mgHb0.xMax - __mgHb1.xMin - __mgHb1.xMax) / 2;\n" +
                        "         __mgHit._y += (__mgHb0.yMin + __mgHb0.yMax - __mgHb1.yMin - __mgHb1.yMax) / 2;\n" +
                        "      }"),
                    notes);
            }
            s = ApplyHitPlacement(s, t, notes);
            s = ApplyHitFadeGuard(s, t, notes);
            return s;
        }

        private static string ApplyHitPlacement(string s, MinimapTweaks t, List<string> notes)
        {
            if (t.HitArtBaked) return s;
            if (t.HitX is not double hx || t.HitY is not double hy) return s;
            string body =
                "if(this.HEALTH_ARMOUR_ABILITY.healthHitMC != undefined && this.healthContainer != undefined)\n" +
                "      {\n" +
                "         var __mgHp = this.HEALTH_ARMOUR_ABILITY.healthHitMC;\n" +
                "         var __mgHpB = __mgHp.getBounds(this.healthContainer);\n" +
                "         var __mgHpKx = __mgHp._parent._xscale;\n" +
                "         if(!(__mgHpKx > 0))\n" +
                "         {\n" +
                "            __mgHpKx = 100;\n" +
                "         }\n" +
                "         var __mgHpKy = __mgHp._parent._yscale;\n" +
                "         if(!(__mgHpKy > 0))\n" +
                "         {\n" +
                "            __mgHpKy = 100;\n" +
                "         }\n" +
                $"         __mgHp._x += ({F(hx)} - (__mgHpB.xMin + __mgHpB.xMax) / 2) * 100 / __mgHpKx;\n" +
                $"         __mgHp._y += ({F(hy)} - (__mgHpB.yMin + __mgHpB.yMax) / 2) * 100 / __mgHpKy;\n" +
                "      }";
            return InsertAtFnEnd(s, "function SETUP_HEALTH_ARMOUR(", Block("HitPos", "      " + body), notes);
        }

        private static string ApplyHitFadeGuard(string s, MinimapTweaks t, List<string> notes)
        {
            if (t.HitAlpha is null && t.HitFadeSeconds is null && t.HitScale is null) return s;
            if (s.Contains("__mgHitT", StringComparison.Ordinal)) return s;

            const string hit = "this.HEALTH_ARMOUR_ABILITY.healthHitMC";
            const string aRemove = "com.rockstargames.ui.tweenStar.TweenStarLite.removeTweenOf(" + hit + ");";
            const string aVisible = hit + "._visible = vis;";
            const string aTween = "com.rockstargames.ui.tweenStar.TweenStarLite.to(" + hit + ",fadeTime,";

            if (AllIndexes(s, aRemove).Count != 1 || AllIndexes(s, aVisible).Count != 1 ||
                AllIndexes(s, aTween).Count != 1)
            {
                notes.Add("[Минимапа] Затухание вспышки: разметка SET_HEALTH_DAMAGE_VISIBLE не стоковая, "
                        + "защита от обрыва пропущена.");
                return s;
            }

            s = s.Replace(aTween,
                hit + ".__mgHitT = getTimer() + (fadeTime + 0.25) * 1000;\n            " + aTween);

            string Guard(string line) =>
                $"if(vis == true || {hit}.__mgHitT == undefined || getTimer() >= {hit}.__mgHitT)\n" +
                "         {\n" +
                "            " + line + "\n" +
                "         }";

            s = s.Replace(aRemove, Guard(aRemove));
            s = s.Replace(aVisible, Guard(aVisible));
            return s;
        }

        private static string ApplyBarStyle(string s, MinimapTweaks t, List<string> notes)
        {
            bool wantColor = t.BarHpColor is not null || t.BarArmorColor is not null;
            double scX = Math.Clamp(t.BarScale, 30, 200);
            double scY = Math.Clamp(t.BarScaleY ?? t.BarScale, 30, 200);
            bool wantSize = Math.Abs(scX - 100) > 0.01 || Math.Abs(scY - 100) > 0.01
                            || t.BarScaleY is not null;
            bool wantPulse = t.BarPulseLowHp;
            if (!wantColor && !wantSize && !wantPulse) return s;

            var lines = new List<string>
            {
                "var __mgBarMC = this.HEALTH_ARMOUR_BAR_MC;",
                "if(__mgBarMC != undefined)",
                "{",
            };
            if (t.BarHpColor is not null)
            {
                var (r, g, b) = Hex(t.BarHpColor, (255, 255, 255));
                lines.Add($"   this.colourHealth = {{r:{r},g:{g},b:{b}}};");
            }
            if (t.BarArmorColor is not null)
            {
                var (r, g, b) = Hex(t.BarArmorColor, (96, 165, 250));
                lines.Add($"   this.colourArmour = {{r:{r},g:{g},b:{b}}};");
            }
            if (wantSize)
            {
                string kx = F(scX / 100), ky = F(scY / 100);
                lines.Add("   if(this.mapType == 1)");
                lines.Add("   {");
                lines.Add($"      __mgBarMC._xscale = {F(scX)};");
                lines.Add($"      __mgBarMC._yscale = {F(scY)};");
                foreach (var mc in new[] { "bar_bg", "abilityMC", "airMC" })
                {
                    lines.Add($"      if(this.HEALTH_ARMOUR_ABILITY.{mc} != undefined)");
                    lines.Add("      {");
                    lines.Add($"         var __mgSz_{mc} = this.HEALTH_ARMOUR_ABILITY.{mc};");
                    lines.Add($"         __mgSz_{mc}._xscale = {F(scX)};");
                    lines.Add($"         __mgSz_{mc}._yscale = {F(scY)};");
                    lines.Add($"         __mgSz_{mc}._x = __mgBarMC._x + (__mgSz_{mc}._x - __mgBarMC._x) * {kx};");
                    lines.Add($"         __mgSz_{mc}._y = __mgBarMC._y + (__mgSz_{mc}._y - __mgBarMC._y) * {ky};");
                    lines.Add("      }");
                }
                lines.Add("   }");
            }
            if (wantPulse)
            {
                int thr = t.LowHpThreshold is int th ? Math.Clamp(th, 1, 99) : 25;
                lines.Add("   var __mgFill = __mgBarMC.healthBar;");
                lines.Add("   if(__mgFill != undefined)");
                lines.Add("   {");
                lines.Add("      __mgFill.__mgOwner = this;");
                lines.Add($"      __mgFill.__mgThr = {thr};");
                lines.Add("      __mgFill.__mgPh = 0;");
                lines.Add("      __mgFill.onEnterFrame = function()");
                lines.Add("      {");
                lines.Add("         if(this.__mgOwner.currentHealthValue <= this.__mgThr && this.__mgOwner.currentHealthValue > 0)");
                lines.Add("         {");
                lines.Add("            this.__mgPh = this.__mgPh + 0.3;");
                lines.Add("            this._alpha = 55 + 45 * Math.sin(this.__mgPh);");
                lines.Add("         }");
                lines.Add("         else if(this._alpha != 100)");
                lines.Add("         {");
                lines.Add("            this._alpha = 100;");
                lines.Add("            this.__mgPh = 0;");
                lines.Add("         }");
                lines.Add("      };");
                lines.Add("   }");
            }
            lines.Add("}");

            return InsertAfterAnchor(s,
                "this.HEALTH_ARMOUR_BAR_MC = this.HEALTH_ARMOUR_ABILITY.healthArmourMC;",
                Block("BarStyle", "      " + string.Join("\n      ", lines)),
                notes, "Стиль полоски");
        }

        private static string ApplyBarCosmetics(string s, MinimapTweaks t, List<string> notes)
        {
            string pos = (t.BarPosition ?? "default").Trim().ToLowerInvariant();
            bool styled = BarMoveRequested(t) || t.BarHpColor is not null || t.BarArmorColor is not null ||
                          Math.Abs(t.BarScale - 100) > 0.01 || t.BarScaleY is not null;
            if (!styled || pos == "left" || pos == "right") return s;

            string body =
                "if(this.mapType == 1 && this.HEALTH_ARMOUR_ABILITY != undefined)\n" +
                "      {\n" +
                "         var __mgCosB = this.HEALTH_ARMOUR_ABILITY.healthArmourMC;\n" +
                "         var __mgCosBg = this.HEALTH_ARMOUR_ABILITY.bar_bg;\n" +
                "         var __mgCosAir = this.HEALTH_ARMOUR_ABILITY.airMC;\n" +
                "         if(__mgCosBg != undefined && __mgCosBg._totalframes >= 2 && (__mgCosAir == undefined || __mgCosAir._visible != true))\n" +
                "         {\n" +
                "            __mgCosBg.gotoAndStop(2);\n" +
                "            this.multiplayerScaleModifier = 2.049;\n" +
                "            if(this.HEALTH_ARMOUR_BAR_MC != undefined && this.HEALTH_ARMOUR_BAR_MC.armourTrough != undefined)\n" +
                "            {\n" +
                "               this.HEALTH_ARMOUR_BAR_MC.armourTrough._xscale = 204.9;\n" +
                "            }\n" +
                "            if(this.ARMOUR != undefined)\n" +
                "            {\n" +
                "               this.ARMOUR._xscale = this.currentArmourValue * 2.049;\n" +
                "            }\n" +
                "            if(this.ABILITY_BAR_MC != undefined)\n" +
                "            {\n" +
                "               this.ABILITY_BAR_MC._visible = false;\n" +
                "            }\n" +
                "         }\n" +
                "         else if(this.HEALTH_ARMOUR_BAR_MC != undefined)\n" +
                "         {\n" +
                "            if(this.HEALTH_ARMOUR_BAR_MC.armourTrough != undefined)\n" +
                "            {\n" +
                "               this.HEALTH_ARMOUR_BAR_MC.armourTrough._xscale = 100 * this.multiplayerScaleModifier;\n" +
                "            }\n" +
                "            if(this.ARMOUR != undefined)\n" +
                "            {\n" +
                "               this.ARMOUR._xscale = this.currentArmourValue * this.multiplayerScaleModifier;\n" +
                "            }\n" +
                "         }\n" +
                "         var __mgCosSh = this.HEALTH_ARMOUR_ABILITY.mgShadowMC;\n" +
                "         if(__mgCosSh != undefined && __mgCosB != undefined)\n" +
                "         {\n" +
                "            var __mgBB = __mgCosB.getBounds(this.HEALTH_ARMOUR_ABILITY);\n" +
                "            var __mgSB = __mgCosSh.getBounds(this.HEALTH_ARMOUR_ABILITY);\n" +
                "            if(__mgSB.yMax - __mgSB.yMin > 0.1 && __mgBB.yMax - __mgBB.yMin > 0.1)\n" +
                "            {\n" +
                "               __mgCosSh._yscale *= (__mgBB.yMax - __mgBB.yMin + 2) / (__mgSB.yMax - __mgSB.yMin);\n" +
                "               __mgSB = __mgCosSh.getBounds(this.HEALTH_ARMOUR_ABILITY);\n" +
                "               __mgCosSh._y += __mgBB.yMin - 1 - __mgSB.yMin;\n" +
                "            }\n" +
                "            if(__mgSB.xMax - __mgSB.xMin > 0.1 && __mgBB.xMax - __mgBB.xMin > 0.1)\n" +
                "            {\n" +
                "               __mgCosSh._xscale *= (__mgBB.xMax - __mgBB.xMin + 2) / (__mgSB.xMax - __mgSB.xMin);\n" +
                "               __mgSB = __mgCosSh.getBounds(this.HEALTH_ARMOUR_ABILITY);\n" +
                "               __mgCosSh._x += __mgBB.xMin - 1 - __mgSB.xMin;\n" +
                "            }\n" +
                "         }\n" +
                "      }";
            return InsertAtFnEnd(s, "function rescaleAllBars(", Block("BarCos", body), notes);
        }

        private static string ApplyBarFlatColor(string s, MinimapTweaks t, List<string> notes)
        {
            bool wantHpFill = t.BarHpColor is not null && !t.BarHpGradient;
            bool wantArFill = t.BarArmorColor is not null;
            bool wantHp = wantHpFill || t.BarHpTroughColor is not null;
            bool wantAr = wantArFill || t.BarArmorTroughColor is not null;
            if (!wantHp && !wantAr) return s;

            static (int r, int g, int b) Dim((int r, int g, int b) c) =>
                ((int)Math.Round(c.r * 0.22), (int)Math.Round(c.g * 0.22), (int)Math.Round(c.b * 0.22));

            static string Flat(string id, string mc, (int r, int g, int b) c) =>
                $"if({mc} != undefined)\n" +
                "      {\n" +
                $"         var __mgA{id} = {mc}._alpha;\n" +
                $"         var __mgC{id} = new Color({mc});\n" +
                $"         __mgC{id}.setTransform({{ra:0,rb:{c.r},ga:0,gb:{c.g},ba:0,bb:{c.b},aa:__mgA{id},ab:0}});\n" +
                "      }";

            const string hpTrough = "this.HEALTH_ARMOUR_BAR_MC.healthTrough";
            const string arTrough = "this.HEALTH_ARMOUR_BAR_MC.armourTrough";

            string hpBody = "";
            if (wantHp)
            {
                var hp = Hex(t.BarHpColor, (255, 255, 255));
                var hpTr = t.BarHpTroughColor is not null ? Hex(t.BarHpTroughColor, Dim(hp)) : Dim(hp);
                string fillNormal = wantHpFill ? Flat("Hf", "this.HEALTH", hp) + "\n      " : "";
                string normal = fillNormal + Flat("Ht", hpTrough, hpTr);
                if (t.LowHpColor is not null && wantHpFill)
                {
                    var lo = Hex(t.LowHpColor, (194, 80, 80));
                    var loTr = t.BarHpTroughColor is not null ? hpTr : Dim(lo);
                    string low = Flat("Lf", "this.HEALTH", lo) + "\n      " +
                                 Flat("Lt", hpTrough, loTr);
                    hpBody = "if(this.healthLow == true)\n      {\n      " + low +
                             "\n      }\n      else\n      {\n      " + normal + "\n      }";
                }
                else hpBody = normal;
            }

            string arBody = "";
            if (wantAr)
            {
                var ar = Hex(t.BarArmorColor, (96, 165, 250));
                var arTr = t.BarArmorTroughColor is not null ? Hex(t.BarArmorTroughColor, Dim(ar)) : Dim(ar);
                arBody = (wantArFill ? Flat("Af", "this.ARMOUR", ar) + "\n      " : "") +
                         Flat("At", arTrough, arTr);
            }

            if (wantHp)
            {
                s = InsertAtFnEnd(s, "function SETUP_HEALTH_ARMOUR(", Block("FlatH0", "      " + hpBody), notes);
                s = InsertAtFnEnd(s, "function SET_PLAYER_HEALTH(", Block("FlatH1", "      " + hpBody), notes);
                s = InsertAtFnEnd(s, "function flashHealthBarRed(", Block("FlatH2", "      " + hpBody), notes);
                s = InsertAtFnEnd(s, "function removeRedHealthBar(", Block("FlatH3", "      " + hpBody), notes);
                s = InsertAtFnEnd(s, "function rescaleAllBars(", Block("FlatH4", "      " + hpBody), notes);
            }
            if (wantAr)
            {
                s = InsertAtFnEnd(s, "function SETUP_HEALTH_ARMOUR(", Block("FlatA0", "      " + arBody), notes);
                s = InsertAtFnEnd(s, "function SET_PLAYER_ARMOUR(", Block("FlatA1", "      " + arBody), notes);
                s = InsertAtFnEnd(s, "function rescaleAllBars(", Block("FlatA2", "      " + arBody), notes);
            }
            return s;
        }

        private static string ApplyBarGradient(string s, MinimapTweaks t, List<string> notes)
        {
            if (!t.BarHpGradient) return s;
            var (fr, fg, fb) = Hex(t.BarGradFullColor ?? t.BarHpColor, (52, 211, 153));
            var (lr, lg, lb) = Hex(t.BarGradLowColor ?? t.LowHpColor, (255, 59, 59));
            var (mr, mg, mb) = Hex(t.BarGradMidColor, (255, 212, 0));
            string body =
                "if(this.HEALTH != undefined)\n" +
                "      {\n" +
                "         var __mgGp = newHealthValue;\n" +
                "         if(__mgGp < 0){ __mgGp = 0; }\n" +
                "         if(__mgGp > 100){ __mgGp = 100; }\n" +
                "         var __mgGr; var __mgGg; var __mgGb; var __mgGt;\n" +
                "         if(__mgGp >= 50)\n" +
                "         {\n" +
                "            __mgGt = (__mgGp - 50) / 50;\n" +
                $"            __mgGr = Math.round({mr} + ({fr} - {mr}) * __mgGt);\n" +
                $"            __mgGg = Math.round({mg} + ({fg} - {mg}) * __mgGt);\n" +
                $"            __mgGb = Math.round({mb} + ({fb} - {mb}) * __mgGt);\n" +
                "         }\n" +
                "         else\n" +
                "         {\n" +
                "            __mgGt = __mgGp / 50;\n" +
                $"            __mgGr = Math.round({lr} + ({mr} - {lr}) * __mgGt);\n" +
                $"            __mgGg = Math.round({lg} + ({mg} - {lg}) * __mgGt);\n" +
                $"            __mgGb = Math.round({lb} + ({mb} - {lb}) * __mgGt);\n" +
                "         }\n" +
                "         com.rockstargames.ui.utils.Colour.Colourise(this.HEALTH,__mgGr,__mgGg,__mgGb,100);\n" +
                "      }";
            return InsertAtFnEnd(s, "function SET_PLAYER_HEALTH(", Block("BarGrad", "      " + body), notes);
        }

        public static bool BarMoveRequested(MinimapTweaks t)
        {
            string pos = (t.BarPosition ?? "default").Trim().ToLowerInvariant();
            return pos != "default"
                || Math.Abs(Math.Clamp(t.BarOffsetX, -200, 200)) > 0.01
                || Math.Abs(Math.Clamp(t.BarOffsetY, -200, 200)) > 0.01;
        }

        private static string ApplyBarPosition(string s, MinimapTweaks t, List<string> notes)
        {
            string pos = (t.BarPosition ?? "default").Trim().ToLowerInvariant();
            double ox = Math.Clamp(t.BarOffsetX, -200, 200);
            double oy = Math.Clamp(t.BarOffsetY, -200, 200);
            bool hasOffset = Math.Abs(ox) > 0.01 || Math.Abs(oy) > 0.01;
            if (pos == "default" && !hasOffset) return s;

            var lines = new List<string>
            {
                "var __mgBar = this.HEALTH_ARMOUR_BAR_MC;",
                "var __mgBg = this.HEALTH_ARMOUR_ABILITY.bar_bg;",
                "var __mgAbil = this.HEALTH_ARMOUR_ABILITY.abilityMC;",
                "var __mgAir = this.HEALTH_ARMOUR_ABILITY.airMC;",
                "var __mgSh = this.HEALTH_ARMOUR_ABILITY.mgShadowMC;",
                "var __mgBar0y = __mgBar._y;",
                "if(this.mapType == 1)",
                "{",
            };
            switch (pos)
            {
                case "top":
                case "bottom":
                    lines.Add($"   var __mgDy = {F((pos == "top" ? 3.5 : 126) + oy)} - __mgBar0y;");
                    foreach (var mc in new[] { "__mgBar", "__mgBg", "__mgAbil", "__mgAir" })
                    {
                        lines.Add($"   {mc}._y += __mgDy;");
                        if (Math.Abs(ox) > 0.01) lines.Add($"   {mc}._x += {F(ox)};");
                    }
                    lines.Add("   if(__mgSh != undefined)");
                    lines.Add("   {");
                    lines.Add("      __mgSh._y += __mgDy;");
                    if (Math.Abs(ox) > 0.01) lines.Add($"      __mgSh._x += {F(ox)};");
                    lines.Add("   }");
                    break;
                case "left":
                case "right":
                    lines.Add("   __mgBar._rotation = 90;");
                    lines.Add($"   __mgBar._x = {F((pos == "left" ? 10 : 181) + ox)};");
                    lines.Add($"   __mgBar._y = {F(8 + oy)};");
                    lines.Add("   __mgBg._visible = false;");
                    lines.Add("   __mgAbil._visible = false;");
                    lines.Add("   __mgAir._visible = false;");
                    lines.Add("   if(__mgSh != undefined)");
                    lines.Add("   {");
                    lines.Add("      __mgSh._visible = false;");
                    lines.Add("   }");
                    break;
                default:
                    foreach (var mc in new[] { "__mgBar", "__mgBg", "__mgAbil", "__mgAir" })
                    {
                        lines.Add($"   {mc}._x += {F(ox)};");
                        lines.Add($"   {mc}._y += {F(oy)};");
                    }
                    lines.Add("   if(__mgSh != undefined)");
                    lines.Add("   {");
                    lines.Add($"      __mgSh._x += {F(ox)};");
                    lines.Add($"      __mgSh._y += {F(oy)};");
                    lines.Add("   }");
                    break;
            }
            lines.Add("}");

            return InsertAfterAnchor(s,
                "this.HEALTH_ARMOUR_BAR_MC = this.HEALTH_ARMOUR_ABILITY.healthArmourMC;",
                Block("Bar", "      " + string.Join("\n      ", lines)),
                notes, "Позиция хелсбара");
        }
    }
}
