using System;
using System.IO;
using Microsoft.Win32;

namespace MiamiGraphics.Shell.Services;

public static class DeviceIdService
{
    private const string KeyPath   = @"Software\MiamiGraphics";
    private const string ValueName = "DeviceId";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics", "config", "device_id.txt");

    public static string GetOrCreate()
    {
        var fromReg  = ReadRegistry();
        var fromFile = ReadFile();

        var id = !string.IsNullOrWhiteSpace(fromReg) ? fromReg
               : !string.IsNullOrWhiteSpace(fromFile) ? fromFile
               : Guid.NewGuid().ToString("N");

        if (!string.Equals(fromReg,  id, StringComparison.Ordinal)) WriteRegistry(id);
        if (!string.Equals(fromFile, id, StringComparison.Ordinal)) WriteFile(id);
        return id;
    }

    private static string? ReadRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) as string;
        }
        catch { return null; }
    }

    private static void WriteRegistry(string id)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key?.SetValue(ValueName, id, RegistryValueKind.String);
        }
        catch {}
    }

    private static string? ReadFile()
    {
        try
        {
            var p = FilePath;
            if (!File.Exists(p)) return null;
            var v = File.ReadAllText(p).Trim();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { return null; }
    }

    private static void WriteFile(string id)
    {
        try
        {
            var p = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, id);
        }
        catch {}
    }
}
