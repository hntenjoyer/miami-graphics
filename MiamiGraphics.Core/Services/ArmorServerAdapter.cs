using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{
    public static class ArmorServerAdapter
    {
        private static readonly string[] LettersAtoJ =
            { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j" };

        private sealed record TargetSlot(string Folder, int Drawable);

        private static readonly Regex SourceMaleRx =
            new(@"^mp_m_freemode_01_.*(gta5rp_skins2|clothes_pack0)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SourceFemaleRx =
            new(@"^mp_f_freemode_01_.*(gta5rp_skins2|freemode_mplts|clothes_pack0)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly TargetSlot[] MaleTargets =
        {
            new("mp_m_freemode_01_mp_m_january2016", 11),
            new("mp_m_freemode_01_mp_m_january2016", 0),
            new("mp_m_freemode_01_male_heist",       0),
        };

        private static readonly TargetSlot[] FemaleTargets =
        {
            new("mp_f_freemode_01_mp_f_january2016", 8),
            new("mp_f_freemode_01_mp_f_january2016", 0),
            new("mp_f_freemode_01_female_heist",     0),
            new("mp_f_freemode_01_female_heist",     1),
        };

        private static readonly Regex SourceMaleMajRx =
            new(@"^mp_m_freemode_01_.*(january2016|_heist)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SourceFemaleMajRx =
            new(@"^mp_f_freemode_01_.*(january2016|_heist)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly TargetSlot[] Male5RpTargets =
        {
            new("mp_m_freemode_01_mp_m_gta5rp_skins2", 0),
        };
        private static readonly TargetSlot[] Female5RpTargets =
        {
            new("mp_f_freemode_01_female_freemode_mplts", 0),
            new("mp_f_freemode_01_mp_f_gta5rp_skins2",    0),
        };

        public sealed record AdaptResult(
            bool Success, string Message,
            int MeshesWritten, int TexturesWritten, IReadOnlyList<string> Notes);

        public static AdaptResult AdaptGta5RpToMajesticInPlace(string rpfPath) =>
            Adapt(rpfPath, SourceMaleRx, MaleTargets, SourceFemaleRx, FemaleTargets,
                  "Majestic", "5RP-папок (gta5rp_skins2 / female_freemode_mplts)");

        public static AdaptResult AdaptMajesticToGta5RpInPlace(string rpfPath) =>
            Adapt(rpfPath, SourceMaleMajRx, Male5RpTargets, SourceFemaleMajRx, Female5RpTargets,
                  "5RP", "Majestic-папок (january2016 / _heist)");

        private static AdaptResult Adapt(
            string rpfPath,
            Regex maleSrcRx, TargetSlot[] maleTargets,
            Regex femaleSrcRx, TargetSlot[] femaleTargets,
            string dirLabel, string missingLabel)
        {
            if (!File.Exists(rpfPath))
                return new AdaptResult(false, $"rpf не найден: {rpfPath}", 0, 0, Array.Empty<string>());

            var notes = new List<string>();
            int meshes = 0, textures = 0;

            using (IArchive archive = RageArchiveWrapper7.Open(rpfPath))
            {
                var root = archive.Root;

                var maleSrc = ExtractSource(root, maleSrcRx);
                var femaleSrc = ExtractSource(root, femaleSrcRx);

                if (maleSrc is null && femaleSrc is null)
                    return new AdaptResult(true, $"Нет {missingLabel} - адаптировать нечего.", 0, 0, notes);

                if (femaleSrc is not null && femaleSrc.Textures.Count == 0 && maleSrc is not null)
                {
                    femaleSrc = femaleSrc with { Textures = maleSrc.Textures };
                    notes.Add("у женского источника нет текстур - использованы мужские");
                }

                if (maleSrc is not null)
                    FillTargets(root, maleSrc, maleTargets, ref meshes, ref textures, notes);
                if (femaleSrc is not null)
                    FillTargets(root, femaleSrc, femaleTargets, ref meshes, ref textures, notes);

                if (meshes > 0 || textures > 0)
                    archive.Flush();
            }

            var msg = meshes + textures > 0
                ? $"Адаптировано: {meshes} мешей, {textures} текстур в канонические {dirLabel}-слоты."
                : $"Все целевые {dirLabel}-слоты уже заполнены - изменений нет.";
            return new AdaptResult(true, msg, meshes, textures, notes);
        }

        private sealed record SourceComponent(byte[] Mesh, IReadOnlyList<byte[]> Textures);

        private static SourceComponent? ExtractSource(IArchiveDirectory root, Regex folderRx)
        {
            var dir = root.GetDirectories()
                .Where(d => folderRx.IsMatch(d.Name))
                .OrderByDescending(d => d.GetFiles().Count(f => f.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(d => d.GetFiles().Count())
                .FirstOrDefault();
            if (dir is null) return null;

            var files = dir.GetFiles().ToList();

            var meshMatch = files
                .Select(f => (file: f, m: Regex.Match(f.Name, @"^task_(\d+)_u\.ydd$", RegexOptions.IgnoreCase)))
                .Where(x => x.m.Success)
                .OrderBy(x => x.file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (meshMatch.file is null) return null;
            string meshDrawable = meshMatch.m.Groups[1].Value;

            var byDrawable = files
                .Select(f => (file: f, m: Regex.Match(f.Name, @"^task_diff_(\d+)_([a-z])_uni\.ytd$", RegexOptions.IgnoreCase)))
                .Where(x => x.m.Success)
                .GroupBy(x => x.m.Groups[1].Value)
                .ToList();

            var texGroup = byDrawable.FirstOrDefault(g => g.Key == meshDrawable)
                         ?? byDrawable.OrderByDescending(g => g.Count()).FirstOrDefault();

            var texBytes = texGroup?
                .OrderBy(x => x.m.Groups[2].Value, StringComparer.OrdinalIgnoreCase)
                .Select(x => Export(x.file))
                .ToList() ?? new List<byte[]>();

            return new SourceComponent(Export(meshMatch.file), texBytes);
        }

        private static void FillTargets(
            IArchiveDirectory root, SourceComponent src, TargetSlot[] targets,
            ref int meshes, ref int textures, List<string> notes)
        {
            foreach (var slot in targets)
            {
                var dir = root.GetDirectories()
                    .FirstOrDefault(d => d.Name.Equals(slot.Folder, StringComparison.OrdinalIgnoreCase));
                if (dir is null)
                {
                    var created = root.CreateDirectory();
                    created.Name = slot.Folder;
                    dir = created;
                }

                var existing = dir.GetFiles().Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                string nnn = slot.Drawable.ToString("D3");

                string meshName = $"task_{nnn}_u.ydd";
                if (!existing.Contains(meshName))
                {
                    WriteResource(dir, meshName, src.Mesh);
                    meshes++;
                }

                if (src.Textures.Count == 0)
                {
                    notes.Add($"{slot.Folder}/task_{nnn}: без текстур (источник пуст)");
                    continue;
                }

                for (int i = 0; i < LettersAtoJ.Length; i++)
                {
                    string letter = LettersAtoJ[i];
                    string texName = $"task_diff_{nnn}_{letter}_uni.ytd";
                    if (existing.Contains(texName)) continue;

                    var srcTex = src.Textures[i % src.Textures.Count];
                    var retargeted = RetargetYtd(srcTex, $"task_diff_{nnn}_{letter}_uni", notes);
                    WriteResource(dir, texName, retargeted);
                    textures++;
                }
            }
        }

        private static byte[] RetargetYtd(byte[] ytdBytes, string newBaseName, List<string> notes)
        {
            var ytd = new YtdFile();
            ytd.Load(ytdBytes);

            var items = ytd.TextureDict?.Textures?.data_items;
            if (items is null || items.Length == 0)
            {
                notes.Add($"{newBaseName}: ytd без текстур - скопирован как есть");
                return ytdBytes;
            }

            var list = new List<Texture>(items.Length);
            foreach (var tex in items)
            {
                if (tex is null) continue;
                bool rename = items.Length == 1
                              || (tex.Name?.StartsWith("task_diff_", StringComparison.OrdinalIgnoreCase) ?? false);
                if (rename)
                {
                    tex.Name = newBaseName;
                    tex.NameHash = JenkHash.GenHash(newBaseName.ToLowerInvariant());
                }
                list.Add(tex);
            }
            ytd.TextureDict!.BuildFromTextureList(list);
            return ytd.Save();
        }

        private static byte[] Export(IArchiveFile f)
        {
            using var ms = new MemoryStream();
            f.Export(ms);
            return ms.ToArray();
        }

        private static void WriteResource(IArchiveDirectory dir, string name, byte[] bytes)
        {
            if (IsRsc7(bytes))
            {
                var rf = dir.CreateResourceFile();
                rf.Name = name;
                using var ims = new MemoryStream(bytes);
                rf.Import(ims);
            }
            else
            {
                var bf = dir.CreateBinaryFile();
                bf.Name = name;
                bf.IsEncrypted = false;
                bf.IsCompressed = false;
                bf.UncompressedSize = bytes.LongLength;
                using var ims = new MemoryStream(bytes);
                bf.Import(ims);
            }
        }

        private static bool IsRsc7(byte[] d) =>
            d.Length >= 4 && d[0] == 0x52 && d[1] == 0x53 && d[2] == 0x43 && d[3] == 0x37;
    }
}
