using System;
using System.IO;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace MiamiGraphics.Core.System
{
    public class HardwareLocator
    {
        public string FindGtaPath()
        {
            string path = GetPathFromRegistry(@"SOFTWARE\Majestic\LauncherX", "InstallPath");
            if (IsValidGtaPath(path)) return path;

            path = GetPathFromRegistry(@"SOFTWARE\RAGE-MP\Client", "gtapath");
            if (IsValidGtaPath(path)) return path;

            path = FindSteamGtaPath();
            if (path != null) return path;

            path = FindEpicGtaPath();
            if (path != null) return path;

            path = GetPathFromRegistry(@"SOFTWARE\WOW6432Node\Rockstar Games\GTAV", "InstallFolder");
            if (IsValidGtaPath(path)) return path;

            return FallbackDiskSearch();
        }

        public string FindGpuName()
        {
            try
            {
                var task = global::System.Threading.Tasks.Task.Run(() => FindGpuNameCore());
                return task.Wait(TimeSpan.FromSeconds(10)) ? task.Result : "Unknown GPU";
            }
            catch
            {
                return "Unknown GPU";
            }
        }

        private string FindGpuNameCore()
        {
            string settingsGpu = GetGpuFromSettingsXml();
            if (!string.IsNullOrEmpty(settingsGpu) && IsGpuPhysicallyPresent(settingsGpu))
            {
                return settingsGpu;
            }

            string discreteNvidia = null;
            string discreteAmd = null;
            string anyDiscrete = null;

            using (var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString();
                    if (string.IsNullOrEmpty(name)) continue;

                    bool isIntel = name.Contains("Intel") || name.Contains("HD Graphics") || name.Contains("UHD");
                    if (isIntel) continue;

                    if (name.Contains("NVIDIA")) discreteNvidia = name;
                    else if (name.Contains("AMD") || name.Contains("Radeon")) discreteAmd = name;
                    else anyDiscrete = name;
                }
            }

            return discreteNvidia ?? discreteAmd ?? anyDiscrete ?? "Unknown GPU";
        }

        private string FindSteamGtaPath()
        {
            string steamPath = GetPathFromRegistry(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                            ?? GetPathFromRegistry(@"SOFTWARE\Valve\Steam", "InstallPath");

            if (string.IsNullOrEmpty(steamPath)) return null;

            string vdfPath = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(vdfPath)) return null;

            string content = File.ReadAllText(vdfPath);
            var matches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"");

            foreach (Match match in matches)
            {
                string libPath = match.Groups[1].Value.Replace("\\\\", "\\");
                string gtaPath = Path.Combine(libPath, @"steamapps\common\Grand Theft Auto V");
                if (IsValidGtaPath(gtaPath)) return gtaPath;
            }

            return null;
        }

        private string FindEpicGtaPath()
        {
            string manifestsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (!Directory.Exists(manifestsPath)) return null;

            foreach (string file in Directory.GetFiles(manifestsPath, "*.item"))
            {
                try
                {
                    string content = File.ReadAllText(file);
                    using (JsonDocument doc = JsonDocument.Parse(content))
                    {
                        if (doc.RootElement.TryGetProperty("InstallLocation", out JsonElement locElement))
                        {
                            string path = locElement.GetString();
                            if (IsValidGtaPath(path)) return path;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private string GetPathFromRegistry(string keyPath, string valueName)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
            {
                return key?.GetValue(valueName)?.ToString();
            }
        }

        private bool IsValidGtaPath(string path)
        {
            return !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "GTA5.exe"));
        }

        private string FallbackDiskSearch()
        {
            string[] commonPaths = new[]
            {
                @"Program Files\Rockstar Games\Grand Theft Auto V",
                @"Program Files (x86)\Rockstar Games\Grand Theft Auto V",
                @"Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V",
                @"Steam\steamapps\common\Grand Theft Auto V",
                @"SteamLibrary\steamapps\common\Grand Theft Auto V",
                @"SteamLibrary\steamapps\common\GTAV",
                @"Program Files\Epic Games\GTAV",
                @"Program Files\Epic Games\Grand Theft Auto V",
                @"Epic Games\GTAV",
                @"Epic Games\Grand Theft Auto V",
                @"Games\GTA V",
                @"Games\Grand Theft Auto V",
                @"Games\GTAV",
                @"Games\GTA5",
                @"GTA V",
                @"Grand Theft Auto V",
                @"GTAV",
                @"GTA5",
                @"Rockstar\GTA V",
                @"Rockstar\Grand Theft Auto V",
                @"Rockstar Games\GTA V",
                @"Rockstar Games\GTAV",
                @"Repack\GTA V",
                @"Repack\Grand Theft Auto V",
                @"Games\Repack\GTA V",
                @"Games\Repack\Grand Theft Auto V",
                @"GTA5RP",
                @"GTA RP",
                @"GTARP",
                @"Games\GTA RP",
                @"Games\GTARP",
                @"Majestic\GTA V",
                @"RAGEMP\client_resources\game",
                @"RAGE Multiplayer\client_resources\game",
                @"SteamLibrary1\steamapps\common\Grand Theft Auto V",
                @"SteamLibrary2\steamapps\common\Grand Theft Auto V",
                @"SteamLibrary3\steamapps\common\Grand Theft Auto V"
            };

            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed) continue;
                    if (!drive.IsReady) continue;
                }
                catch
                {
                    continue;
                }

                foreach (var folder in commonPaths)
                {
                    try
                    {
                        string fullPath = Path.Combine(drive.RootDirectory.FullName, folder);
                        if (IsValidGtaPath(fullPath)) return fullPath;
                    }
                    catch
                    {

                    }
                }
            }
            return null;
        }

        private string GetGpuFromSettingsXml()
        {
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                               @"Rockstar Games\GTA V\settings.xml");
            if (!File.Exists(settingsPath)) return null;

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(settingsPath);
                XmlNode node = doc.SelectSingleNode("//GraphicsAdapter");
                return node?.InnerText?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private bool IsGpuPhysicallyPresent(string gpuName)
        {
            using (var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["Name"]?.ToString() == gpuName) return true;
                }
            }
            return false;
        }
    }
}
