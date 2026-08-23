using System;
using System.Collections.Generic;

namespace MiamiGraphics.Core.Services
{
    public class HntgraphCustomization
    {
        public List<SelectedGunRecord> SelectedGuns { get; set; } = new();

        public DateTime? LastBuiltAt { get; set; }

        public long LastBuiltSize { get; set; }

        public string? LastBuiltSha256 { get; set; }
    }

    public class SelectedGunRecord
    {
        public string GunpackDir { get; set; } = "";

        public string GunpackName { get; set; } = "";

        public string BaseName { get; set; } = "";

        public string WeaponPrefix { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public List<string> Files { get; set; } = new();

        public DateTime AddedAt { get; set; }
    }
}
