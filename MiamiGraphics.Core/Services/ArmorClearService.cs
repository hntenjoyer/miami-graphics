using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Parser;
using RageLib.GTA5.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{
    public sealed class ArmorClearRequest
    {
        public string RecipientPatchRoot { get; init; } = "";
        public DiffManifest RecipientManifest { get; init; } = default!;
    }

    public sealed class ArmorClearResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public IReadOnlyList<string> ClearedFiles { get; init; } = Array.Empty<string>();
    }

    public sealed class ArmorClearService
    {
        private static readonly JsonSerializerOptions JsonOpts = CreateJsonOptions();

        public ArmorClearResult Clear(ArmorClearRequest request)
        {
            var componentMap = LoadComponentMap(request.RecipientPatchRoot);
            if (componentMap == null ||
                !componentMap.Components.TryGetValue("armor", out ComponentInfo? armor) ||
                armor == null || !armor.IsFound || armor.InternalPaths.Count == 0)
            {
                return new ArmorClearResult
                {
                    Success = true,
                    Message = Loc.T("misc.reduxNoArmorComponent")
                };
            }

            string aPatchFiles = Path.Combine(request.RecipientPatchRoot, "patch_files");
            var manifest = request.RecipientManifest;
            var cleared = new List<string>();

            foreach (string p in armor.InternalPaths)
            {
                string abs = Path.Combine(aPatchFiles, p.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(abs))
                {
                    Console.WriteLine($"[ArmorClear] Пропуск {p}: файл отсутствует.");
                    continue;
                }

                try
                {
                    OverwriteWithEmptyRpf(abs);
                    Console.WriteLine($"[ArmorClear] {p} → перезаписан пустым RPF7 ({new FileInfo(abs).Length} байт)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ArmorClear] Не удалось обнулить {abs}: {ex.Message}");
                    continue;
                }

                byte[] bytes = File.ReadAllBytes(abs);
                string sha = ComputeSha256(bytes);
                var existing = manifest.Actions.FirstOrDefault(a =>
                    NormalizePath(a.TargetPath) == NormalizePath(p));
                if (existing != null)
                {
                    existing.Size = bytes.LongLength;
                    existing.Sha256 = sha;
                }
                else
                {

                    manifest.Actions.Add(new PatchAction
                    {
                        Type = ActionType.Replace,
                        TargetPath = NormalizePath(p),
                        SourcePath = $"patch_files/{NormalizePath(p)}",
                        Size = bytes.LongLength,
                        Sha256 = sha,
                        IsWholeReplaceNestedRpf = true
                    });
                }

                cleared.Add(p);
            }

            PatchCustomizationSupport.RecalculateTotalPatchSize(manifest);

            return new ArmorClearResult
            {
                Success = true,
                Message = Loc.T("misc.armorRpfCleared", ("count", cleared.Count)),
                ClearedFiles = cleared
            };
        }

        private static void OverwriteWithEmptyRpf(string targetPath)
        {

            RageArchiveEncryption7 originalEncryption = RageArchiveEncryption7.None;
            try
            {
                using var src = RageArchiveWrapper7.Open(targetPath);
                originalEncryption = src.archive_.Encryption;
            }
            catch
            {

            }

            string tempPath = targetPath + ".clear.tmp";
            if (File.Exists(tempPath)) File.Delete(tempPath);

            try
            {
                using (var emptyArc = RageArchiveWrapper7.Create(tempPath))
                {
                    emptyArc.FileName = Path.GetFileName(targetPath);
                    emptyArc.archive_.Encryption = originalEncryption;
                    emptyArc.Flush();
                }

                File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }
            catch
            {

                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }

        private static string NormalizePath(string p) =>
            (p ?? "").Replace('\\', '/').TrimStart('/').ToLowerInvariant();

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        private static ResolvedComponentMap? LoadComponentMap(string reduxRoot)
        {
            string path = Path.Combine(reduxRoot, "component_map.json");
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<ResolvedComponentMap>(File.ReadAllText(path), JsonOpts);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArmorClear] Ошибка чтения component_map.json ({reduxRoot}): {ex.Message}");
                return null;
            }
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var o = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }
    }
}
