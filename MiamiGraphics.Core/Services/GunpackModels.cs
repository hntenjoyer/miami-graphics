using System;
using System.Collections.Generic;

namespace MiamiGraphics.Core.Services
{
    public class GunpackInfo
    {
        public string Name { get; set; } = "";

        public DateTime ParsedAt { get; set; }

        public string SourceDlcRpfPath { get; set; } = "";

        public string WeaponsRpfName { get; set; } = "weapons.rpf";

        public long WeaponsRpfSize { get; set; }

        public int TotalFiles { get; set; }

        public int AttachmentFilesSkipped { get; set; }

        public List<string> UnrecognizedFiles { get; set; } = new();

        public List<GunCategory> Categories { get; set; } = new();
    }

    public class GunCategory
    {
        public string BaseName { get; set; } = "";

        public string WeaponPrefix { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public List<string> Files { get; set; } = new();

        public string ZipFileName { get; set; } = "";

        public string? GltfZipFileName { get; set; }
    }

    public enum GunpackMode
    {
        FullPack,
        SelectedGuns
    }

    public class GunpackInstallRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "";
        public DateTime InstalledAt { get; set; }
        public GunpackMode Mode { get; set; } = GunpackMode.FullPack;

        public string GunpackDir { get; set; } = "";

        public List<string> SelectedBaseNames { get; set; } = new();
    }
}
