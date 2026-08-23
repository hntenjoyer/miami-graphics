using RageLib.GTA5.ResourceWrappers.PC.Particles;
using RageLib.Helpers;
using System;
using System.IO;
using System.Linq;

namespace MiamiGraphics.Core.Parser
{
    public interface IPatchMutator
    {
        string Name { get; }
        void Apply(string patchRootDirectory, DiffManifest manifest, string gtaRootPath);
    }

    public sealed class SmokeFixMutator : IPatchMutator
    {
        public string Name => "Smoke Fix Mutator";

        private static readonly string DefaultSmokeAssetsDirectory = ResolveBundledSmokeDir();

        private readonly string _smokeAssetsDirectory;

        public SmokeFixMutator(string smokeAssetsDirectory = null)
        {
            _smokeAssetsDirectory = string.IsNullOrWhiteSpace(smokeAssetsDirectory)
                ? DefaultSmokeAssetsDirectory
                : smokeAssetsDirectory;
        }

        private static string ResolveBundledSmokeDir()
        {
            var direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "additionals", "smoke");
            if (Directory.Exists(direct)) return direct;

            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var p = Path.Combine(dir, "additionals", "smoke");
                if (Directory.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }

            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiamiGraphics", "additionals", "smoke");
            if (Directory.Exists(local)) return local;

            return string.Empty;
        }

        public void Apply(string patchRootDirectory, DiffManifest manifest, string gtaRootPath)
        {
            if (string.IsNullOrWhiteSpace(patchRootDirectory))
                throw new ArgumentNullException(nameof(patchRootDirectory));

            if (manifest?.Actions == null || manifest.Actions.Count == 0)
            {
                Console.WriteLine("[SmokeFix] Манифест пуст. Пропуск.");
                return;
            }

            string plumesDds = Path.Combine(_smokeAssetsDirectory, "ptfx_smoke_new_plumes.dds");
            string wispyDds = Path.Combine(_smokeAssetsDirectory, "ptfx_smoke_wispy_anim.dds");

            if (!File.Exists(plumesDds) || !File.Exists(wispyDds))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[SmokeFix] DDS для фикса дыма не найдены.");
                Console.WriteLine($"           {plumesDds}");
                Console.WriteLine($"           {wispyDds}");
                Console.ResetColor();
                return;
            }

            var coreActions = manifest.Actions
                .Where(a =>
                    !string.IsNullOrWhiteSpace(a.TargetPath) &&
                    a.TargetPath.EndsWith("core.ypt", StringComparison.OrdinalIgnoreCase) &&
                    (a.Type == ActionType.Replace || a.Type == ActionType.Import) &&
                    !a.IsWholeReplaceNestedRpf)
                .ToList();

            if (coreActions.Count == 0)
            {
                Console.WriteLine("[SmokeFix] В manifest нет core.ypt для патчинга.");
                return;
            }

            Console.WriteLine($"[SmokeFix] Найдено core.ypt для патчинга: {coreActions.Count}");

            foreach (var action in coreActions)
            {
                string physicalPath = Path.IsPathRooted(action.SourcePath)
                    ? action.SourcePath
                    : Path.Combine(patchRootDirectory, action.SourcePath);

                physicalPath = physicalPath.Replace('/', '\\');

                if (!File.Exists(physicalPath))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  -> [SmokeFix] core.ypt не найден: {physicalPath}");
                    Console.ResetColor();
                    continue;
                }

                Console.WriteLine($"  -> [SmokeFix] Патчим: {action.TargetPath}");

                try
                {
                    var result = PatchSingleYpt(physicalPath, plumesDds, wispyDds);

                    Console.ForegroundColor = result.Modified
                        ? ConsoleColor.Green
                        : ConsoleColor.Yellow;

                    if (result.Modified)
                    {
                        Console.WriteLine("     [OK] Дым успешно обновлён.");
                    }
                    else
                    {
                        Console.WriteLine("     [SKIP] В этом core.ypt smoke texture entry не найдены.");
                    }

                    if (result.MissingTextureNames.Any())
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine("     [WARN] Отсутствуют entry: " +
                            string.Join(", ", result.MissingTextureNames));
                    }

                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"     [ERROR] {action.TargetPath}: {ex.Message}");
                    Console.ResetColor();
                    throw;
                }
            }
        }

        private static PatchResult PatchSingleYpt(string yptPath, string plumesDdsPath, string wispyDdsPath)
        {
            var result = new PatchResult();

            var ypt = new ParticlesFileWrapper_GTA5_pc();
            ypt.Load(yptPath);

            var texDict = ypt.Particles?.TextureDictionary?.Textures;
            if (texDict == null)
                return result;

            var texPlumes = texDict.FirstOrDefault(t =>
                t.Name.Equals("ptfx_smoke_new_plumes", StringComparison.OrdinalIgnoreCase));

            if (texPlumes != null)
            {
                DDSIO.LoadTextureData(texPlumes, plumesDdsPath);
                Console.WriteLine("     [SmokeFix] Обновлена: ptfx_smoke_new_plumes");
                result.Modified = true;
            }
            else
            {
                result.MissingTextureNames.Add("ptfx_smoke_new_plumes");
            }

            var texWispy = texDict.FirstOrDefault(t =>
                t.Name.Equals("ptfx_smoke_wispy_anim", StringComparison.OrdinalIgnoreCase));

            if (texWispy != null)
            {
                DDSIO.LoadTextureData(texWispy, wispyDdsPath);
                Console.WriteLine("     [SmokeFix] Обновлена: ptfx_smoke_wispy_anim");
                result.Modified = true;
            }
            else
            {
                result.MissingTextureNames.Add("ptfx_smoke_wispy_anim");
            }

            if (result.Modified)
            {
                ypt.Save(yptPath);
            }

            return result;
        }

        private sealed class PatchResult
        {
            public bool Modified { get; set; }
            public global::System.Collections.Generic.List<string> MissingTextureNames { get; }
                = new global::System.Collections.Generic.List<string>();

        }
    }
}
