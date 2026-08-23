#nullable enable
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MiamiGraphics.Core.PcDiag;

[SupportedOSPlatform("windows")]
public static class PcDiagNvidia
{
    public const string ProfileName = "Miami Graphics - GTA V";
    public const string AppName = "gta5.exe";

    private const uint PreferredPstateId = 0x1057EB71;
    private const uint PrerenderLimitId = 0x007BA09E;
    private const uint ShaderCacheMaxSizeId = 0x00AC8497;

    private const uint PstatePreferMax = 1;
    private const uint PrerenderMin = 1;
    private const uint ShaderCacheKb = 10 * 1024 * 1024;

    public static readonly (uint Id, uint Value)[] TargetSettings =
    {
        (PreferredPstateId, PstatePreferMax),
        (PrerenderLimitId, PrerenderMin),
        (ShaderCacheMaxSizeId, ShaderCacheKb),
    };

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    private delegate int InitializeDel();
    private delegate int UnloadDel();
    private delegate int CreateSessionDel(out IntPtr session);
    private delegate int DestroySessionDel(IntPtr session);
    private delegate int LoadSettingsDel(IntPtr session);
    private delegate int SaveSettingsDel(IntPtr session);
    private delegate int FindProfileByNameDel(IntPtr session, [MarshalAs(UnmanagedType.LPWStr)] string name, out IntPtr profile);
    private delegate int CreateProfileDel(IntPtr session, ref NvdrsProfile profile, out IntPtr handle);
    private delegate int DeleteProfileDel(IntPtr session, IntPtr profile);
    private delegate int CreateApplicationDel(IntPtr session, IntPtr profile, ref NvdrsApplication app);
    private delegate int FindApplicationByNameDel(IntPtr session, [MarshalAs(UnmanagedType.LPWStr)] string appName, out IntPtr profile, ref NvdrsApplication app);
    private delegate int SetSettingDel(IntPtr session, IntPtr profile, ref NvdrsSetting setting);
    private delegate int GetSettingDel(IntPtr session, IntPtr profile, uint settingId, ref NvdrsSetting setting);
    private delegate int DeleteProfileSettingDel(IntPtr session, IntPtr profile, uint settingId);

    private static T Fn<T>(uint id) where T : Delegate
    {
        var p = QueryInterface(id);
        if (p == IntPtr.Zero) throw new InvalidOperationException($"NVAPI: функция 0x{id:X8} недоступна");
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    [StructLayout(LayoutKind.Explicit, Size = 4100)]
    private struct SettingUnion
    {
        [FieldOffset(0)] public uint DwordValue;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct NvdrsSetting
    {
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)] public string SettingName;
        public uint SettingId;
        public uint SettingType;
        public uint SettingLocation;
        public uint IsCurrentPredefined;
        public uint IsPredefinedValid;
        public SettingUnion Predefined;
        public SettingUnion Current;

        public static NvdrsSetting Create() => new()
        {
            Version = (uint)Marshal.SizeOf<NvdrsSetting>() | (1u << 16),
            SettingName = "",
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct NvdrsProfile
    {
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)] public string ProfileName;
        public uint GpuSupport;
        public uint IsPredefined;
        public uint NumOfApps;
        public uint NumOfSettings;

        public static NvdrsProfile Create(string name) => new()
        {
            Version = (uint)Marshal.SizeOf<NvdrsProfile>() | (1u << 16),
            ProfileName = name,
            GpuSupport = 0x00000001,
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct NvdrsApplication
    {
        public uint Version;
        public uint IsPredefined;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)] public string UserFriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)] public string Launcher;

        public static NvdrsApplication Create(string appName) => new()
        {
            Version = (uint)Marshal.SizeOf<NvdrsApplication>() | (1u << 16),
            AppName = appName,
            UserFriendlyName = appName,
            Launcher = "",
        };
    }

    private sealed class DrsSession : IDisposable
    {
        public IntPtr Handle;
        private readonly DestroySessionDel _destroy;
        private readonly UnloadDel _unload;
        public DrsSession()
        {
            var init = Fn<InitializeDel>(0x0150E828);
            Check(init(), "Initialize");
            _unload = Fn<UnloadDel>(0xD22BDD7E);
            var create = Fn<CreateSessionDel>(0x0694D52E);
            Check(create(out Handle), "CreateSession");
            _destroy = Fn<DestroySessionDel>(0xDAD9CFF8);
            Check(Fn<LoadSettingsDel>(0x375DBD6B)(Handle), "LoadSettings");
        }
        public void Dispose()
        {
            try { _destroy(Handle); } catch { }
            try { _unload(); } catch { }
        }
    }

    private static void Check(int status, string what)
    {
        if (status != 0)
            throw new InvalidOperationException($"NVAPI {what}: код {status.ToString(CultureInfo.InvariantCulture)}");
    }

    public static bool IsAvailable()
    {
        try { return QueryInterface(0x0150E828) != IntPtr.Zero; }
        catch { return false; }
    }

    public sealed record ProfileState(bool AppBound, uint?[] CurrentValues);

    public static ProfileState ReadState()
    {
        using var s = new DrsSession();
        var app = NvdrsApplication.Create("");
        var find = Fn<FindApplicationByNameDel>(0xEEE566B2);
        var status = find(s.Handle, AppName, out var profile, ref app);
        if (status != 0)
            return new ProfileState(false, new uint?[TargetSettings.Length]);

        var get = Fn<GetSettingDel>(0x73BF8338);
        var values = new uint?[TargetSettings.Length];
        for (int i = 0; i < TargetSettings.Length; i++)
        {
            var setting = NvdrsSetting.Create();
            values[i] = get(s.Handle, profile, TargetSettings[i].Id, ref setting) == 0
                ? setting.Current.DwordValue
                : null;
        }
        return new ProfileState(true, values);
    }

    public static (string Message, global::System.Collections.Generic.Dictionary<string, string?> Previous) Apply()
    {
        using var s = new DrsSession();
        var prev = new global::System.Collections.Generic.Dictionary<string, string?>();

        var app = NvdrsApplication.Create("");
        var find = Fn<FindApplicationByNameDel>(0xEEE566B2);
        IntPtr profile;
        if (find(s.Handle, AppName, out profile, ref app) == 0)
        {
            prev["created"] = "0";
            var get = Fn<GetSettingDel>(0x73BF8338);
            for (int i = 0; i < TargetSettings.Length; i++)
            {
                var st = NvdrsSetting.Create();
                prev["v" + i] = get(s.Handle, profile, TargetSettings[i].Id, ref st) == 0
                    ? st.Current.DwordValue.ToString(CultureInfo.InvariantCulture)
                    : "";
            }
        }
        else
        {
            prev["created"] = "1";
            var prof = NvdrsProfile.Create(ProfileName);
            Check(Fn<CreateProfileDel>(0xCC176068)(s.Handle, ref prof, out profile), "CreateProfile");
            var newApp = NvdrsApplication.Create(AppName);
            Check(Fn<CreateApplicationDel>(0x4347A9DE)(s.Handle, profile, ref newApp), "CreateApplication");
        }

        var set = Fn<SetSettingDel>(0x577DD202);
        foreach (var (id, value) in TargetSettings)
        {
            var st = NvdrsSetting.Create();
            st.SettingId = id;
            st.SettingType = 0;
            st.Current.DwordValue = value;
            Check(set(s.Handle, profile, ref st), $"SetSetting 0x{id:X8}");
        }
        Check(Fn<SaveSettingsDel>(0xFCBC7E14)(s.Handle), "SaveSettings");

        return ("Профиль NVIDIA для gta5.exe настроен: максимальная производительность, короткая очередь кадров, кэш шейдеров 10 ГБ.", prev);
    }

    public static string Revert(global::System.Collections.Generic.IReadOnlyDictionary<string, string?> prev)
    {
        using var s = new DrsSession();
        if (prev.TryGetValue("created", out var created) && created == "1")
        {
            var findProf = Fn<FindProfileByNameDel>(0x7E4A9A0B);
            if (findProf(s.Handle, ProfileName, out var profile) == 0)
            {
                Check(Fn<DeleteProfileDel>(0x17093206)(s.Handle, profile), "DeleteProfile");
                Check(Fn<SaveSettingsDel>(0xFCBC7E14)(s.Handle), "SaveSettings");
            }
            return "Профиль NVIDIA, созданный нами, удалён.";
        }

        var app = NvdrsApplication.Create("");
        if (Fn<FindApplicationByNameDel>(0xEEE566B2)(s.Handle, AppName, out var prof2, ref app) != 0)
            return "Профиль игры в драйвере не найден: возвращать нечего.";

        var set = Fn<SetSettingDel>(0x577DD202);
        var del = Fn<DeleteProfileSettingDel>(0xE4A26362);
        for (int i = 0; i < TargetSettings.Length; i++)
        {
            var old = prev.GetValueOrDefault("v" + i);
            if (string.IsNullOrEmpty(old))
            {
                del(s.Handle, prof2, TargetSettings[i].Id);
            }
            else
            {
                var st = NvdrsSetting.Create();
                st.SettingId = TargetSettings[i].Id;
                st.SettingType = 0;
                st.Current.DwordValue = uint.Parse(old, CultureInfo.InvariantCulture);
                Check(set(s.Handle, prof2, ref st), "SetSetting");
            }
        }
        Check(Fn<SaveSettingsDel>(0xFCBC7E14)(s.Handle), "SaveSettings");
        return "Прежние настройки профиля NVIDIA возвращены.";
    }
}
