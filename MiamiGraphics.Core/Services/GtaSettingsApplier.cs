#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.System;

namespace MiamiGraphics.Core.Services;

public sealed class GtaSettingsApplier
{
    private static readonly string[] GameProcessNames = { "GTA5", "GTA5_Enhanced", "PlayGTAV" };

    public sealed record ApplyResult(
        bool Success,
        string? ErrorMessage,
        string TargetPath,
        string? BackupPath,
        bool GameWasRunning
    );

    public Task<ApplyResult> ApplyAsync(string presetXml, CancellationToken ct = default)
        => Task.Run(() => Apply(presetXml), ct);

    private ApplyResult Apply(string presetXml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(presetXml))
                return Fail(Loc.T("error.emptyXmlPreset"));

            XDocument doc;
            try
            {
                doc = XDocument.Parse(presetXml);
            }
            catch (Exception ex)
            {
                return Fail(Loc.T("error.xmlPresetParseFailed", ("reason", ex.Message)));
            }

            var root = doc.Root;
            if (root is null)
                return Fail(Loc.T("error.xmlPresetNoRoot"));

            var targetPath = GetSettingsPath();
            var existingDoc = LoadExistingSettings(targetPath);

            var detectedGpu = TryDetectGpu();
            var existingGpu = existingDoc?.Root?.Element("VideoCardDescription")?.Value;
            var gpuToWrite = !string.IsNullOrWhiteSpace(detectedGpu) && detectedGpu != "Unknown GPU"
                ? detectedGpu
                : existingGpu;
            if (!string.IsNullOrWhiteSpace(gpuToWrite))
            {
                foreach (var el in doc.Descendants("VideoCardDescription"))
                    el.Value = gpuToWrite!;
            }

            var keptFromPlayer = KeepPlayerLocals(root, existingDoc?.Root);
            FeatureLog.Write("настройки", keptFromPlayer.Count > 0
                ? "из файла игрока сохранено: " + string.Join(", ", keptFromPlayer)
                : "своего settings.xml нет - пресет применяется целиком, включая экран автора");

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            string? backupPath = null;
            if (File.Exists(targetPath))
            {
                var attrs = File.GetAttributes(targetPath);
                if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    File.SetAttributes(targetPath, attrs & ~FileAttributes.ReadOnly);
                backupPath = MakeBackup(targetPath);
            }

            var gameRunning = IsGameRunning();

            var tempPath = targetPath + ".hnt-tmp";
            try
            {
                doc.Save(tempPath);
                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch {  }
                }
                return new ApplyResult(false, Loc.T("error.settingsXmlWriteFailed", ("reason", ex.Message)), targetPath, backupPath, gameRunning);
            }

            return new ApplyResult(true, null, targetPath, backupPath, gameRunning);
        }
        catch (Exception ex)
        {
            return Fail(Loc.T("error.unexpected", ("reason", ex.Message)));
        }
    }

    public static string GetSettingsPath()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "Rockstar Games", "GTA V", "settings.xml");
    }

    public static bool IsGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0) return true;
            }
            catch {  }
        }
        return false;
    }

    private static string? TryDetectGpu()
    {
        try
        {
            return new HardwareLocator().FindGpuName();
        }
        catch
        {
            return null;
        }
    }

    private static XDocument? LoadExistingSettings(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return XDocument.Load(path);
        }
        catch
        {

            return null;
        }
    }

    internal static List<string> KeepPlayerLocals(XElement presetRoot, XElement? playerRoot)
    {
        var kept = new List<string>();
        if (playerRoot is null) return kept;

        if (playerRoot.Element("version") is { } playerVersion &&
            presetRoot.Element("version") is { } presetVersion)
        {
            var was = presetVersion.Attribute("value")?.Value;
            var now = playerVersion.Attribute("value")?.Value;
            if (!string.IsNullOrEmpty(now) && !string.Equals(was, now, StringComparison.Ordinal))
            {
                presetVersion.SetAttributeValue("value", now);
                kept.Add($"version {was} → {now}");
            }
        }

        if (playerRoot.Element("video") is { } playerVideo)
        {
            if (presetRoot.Element("video") is { } presetVideo)
                presetVideo.ReplaceWith(new XElement(playerVideo));
            else
                presetRoot.Add(new XElement(playerVideo));

            kept.Add($"video {Val(playerVideo, "ScreenWidth")}x{Val(playerVideo, "ScreenHeight")}" +
                     $"@{Val(playerVideo, "RefreshRate")}");
        }

        return kept;

        static string Val(XElement parent, string name)
            => parent.Element(name)?.Attribute("value")?.Value ?? "?";
    }

    private static string MakeBackup(string filePath)
    {
        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var name = Path.GetFileName(filePath);
        var backup = Path.Combine(dir, $"{name}.backup-{ts}");
        File.Copy(filePath, backup, overwrite: false);
        return backup;
    }

    private static ApplyResult Fail(string message)
        => new(false, message, GetSettingsPath(), null, false);
}
