using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiamiGraphics.Core.Services
{
    public class InstalledModsJournal
    {
        public List<InstalledModRecord> ActiveMods { get; set; } = new();
        public List<InstalledModRecord> InactiveHistory { get; set; } = new();

        public DateTime? LastRebuiltAt { get; set; }

        public long LastUpdateRpfSize { get; set; }

        public string? CleanUpdateRpfPath { get; set; }

        public GunpackInstallRecord? ActiveGunpack { get; set; }
        public List<GunpackInstallRecord> GunpackHistory { get; set; } = new();

        public HntgraphCustomization Hntgraph { get; set; } = new();
    }

    public class InstalledModRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string ReduxName { get; set; } = "";
        public DateTime InstalledAt { get; set; }

        public string BasePatchPath { get; set; } = "";

        public long TotalPatchSizeBytes { get; set; }
        public int ActionsCount { get; set; }

        public ModCustomizations Customizations { get; set; } = new();
    }

    public class ModCustomizations
    {
        public MinimapCustomization? Minimap { get; set; }
        public TracerCustomization? Tracers { get; set; }
        public ArenaCustomization? Arena { get; set; }
        public List<CrossInjectDonorRef> CrossInjectDonors { get; set; } = new();
    }

    public class MinimapCustomization
    {
        public byte HealthR { get; set; }
        public byte HealthG { get; set; }
        public byte HealthB { get; set; }

        public byte ArmourR { get; set; }
        public byte ArmourG { get; set; }
        public byte ArmourB { get; set; }

        public string? HitmarkerPath { get; set; }
    }

    public class TracerCustomization
    {
        public string Scenario { get; set; } = "MasterTexture";
        public string? ModelDirectory { get; set; }
        public byte Red { get; set; }
        public byte Green { get; set; }
        public byte Blue { get; set; }
    }

    public class ArenaCustomization
    {
        public string DonorBasePatchPath { get; set; } = "";
    }

    public class CrossInjectDonorRef
    {
        public string Component { get; set; } = "";
        public string DonorBasePatchPath { get; set; } = "";
    }
}
