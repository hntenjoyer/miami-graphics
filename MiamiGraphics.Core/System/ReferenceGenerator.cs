using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace MiamiGraphics.Core.System
{
    public class GtaFileReference
    {
        public long Size { get; set; }
        public string Sha256 { get; set; }
    }

    public class GtaVersionReference
    {
        public string ExeVersion { get; set; }
        public GtaFileReference UpdateRpf { get; set; }
        public GtaFileReference CommonRpf { get; set; }
    }

    public class ReferenceGenerator
    {
        public void GenerateServerJson(string gtaPath, string outputPath)
        {
            Console.WriteLine("\n[API Mock] Генерация эталонного JSON начата...");

            string exePath = Path.Combine(gtaPath, "GTA5.exe");
            string updatePath = Path.Combine(gtaPath, @"update\update.rpf");
            string commonPath = Path.Combine(gtaPath, "common.rpf");

            if (!File.Exists(exePath) || !File.Exists(updatePath))
            {
                Console.WriteLine("[ОШИБКА] Не найдены основные файлы GTA 5 для генерации эталона!");
                return;
            }

            var reference = new GtaVersionReference
            {
                ExeVersion = FileVersionInfo.GetVersionInfo(exePath).FileVersion,
                UpdateRpf = GetFileInfo(updatePath),
                CommonRpf = File.Exists(commonPath) ? GetFileInfo(commonPath) : null
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(reference, options);
            File.WriteAllText(outputPath, json);

            Console.WriteLine($"[API Mock] Успех! Файл сохранен: {outputPath}");
        }

        private GtaFileReference GetFileInfo(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                return new GtaFileReference
                {
                    Size = new FileInfo(filePath).Length,
                    Sha256 = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()
                };
            }
        }
    }
}