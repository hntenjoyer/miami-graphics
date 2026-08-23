using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.System
{
    public sealed record AfDetectResult(string Vendor, bool Applied, bool Detectable, string? Detail);

    public sealed record AfApplyResult(bool Handled, bool Success, string Vendor, string? Error);

    public static class AnisotropicFilterDetector
    {
        public static AfDetectResult Detect()
        {
            try
            {
                var nv = DetectNvidia();
                if (nv.Detectable) return nv;
            }
            catch {}

            try
            {
                var amd = DetectAmd();
                if (amd.Detectable) return amd;
            }
            catch {}

            return new AfDetectResult("other", false, false, "no supported GPU driver detected");
        }

        public static AfApplyResult Apply()
        {
            try
            {
                var nv = ApplyNvidia();
                if (nv.Handled) return nv;
            }
            catch (Exception ex) { return new AfApplyResult(true, false, "nvidia", ex.Message); }

            try
            {
                var amd = ApplyAmd();
                if (amd.Handled) return amd;
            }
            catch (Exception ex) { return new AfApplyResult(true, false, "amd", ex.Message); }

            return new AfApplyResult(false, false, "other", Loc.T("error.gpuVendorUnknown"));
        }

        private const uint ANISO_MODE_SELECTOR_ID = 0x10D2BB16;
        private const uint ANISO_MODE_LEVEL_ID    = 0x101E61A9;

        private const int NVDRS_SETTING_SIZE  = 12320;
        private const int OFFSET_SETTING_ID    = 4100;
        private const int OFFSET_SETTING_TYPE  = 4104;
        private const int OFFSET_CURRENT_VALUE = 8220;
        private static readonly uint NVDRS_SETTING_VER = (uint)NVDRS_SETTING_SIZE | (1u << 16);

        private static AfDetectResult DetectNvidia()
        {
            IntPtr session = IntPtr.Zero;
            try
            {
                if (Nvapi.Initialize() != 0) return new("nvidia", false, false, "NvAPI_Initialize failed");
                if (Nvapi.CreateSession(out session) != 0 || session == IntPtr.Zero)
                    return new("nvidia", false, false, "NvAPI_DRS_CreateSession failed");
                if (Nvapi.LoadSettings(session) != 0)
                    return new("nvidia", false, false, "NvAPI_DRS_LoadSettings failed");

                IntPtr nameBuf = AllocUnicodeString("Grand Theft Auto V");
                try
                {
                    int fr = Nvapi.FindProfileByName(session, nameBuf, out IntPtr profile);
                    if (fr != 0 || profile == IntPtr.Zero)
                        return new("nvidia", false, true, $"GTA profile not found (nvapi {fr})");

                    int selector = GetSettingU32(session, profile, ANISO_MODE_SELECTOR_ID, out bool selFound);
                    int level    = GetSettingU32(session, profile, ANISO_MODE_LEVEL_ID,    out bool lvlFound);
                    bool applied = selFound && selector == 1 && lvlFound && level == 16;
                    var detail = $"selector={(selFound ? selector.ToString() : "-")} level={(lvlFound ? level.ToString() : "-")}";
                    return new("nvidia", applied, true, detail);
                }
                finally { Marshal.FreeHGlobal(nameBuf); }
            }
            catch (DllNotFoundException) { return new("nvidia", false, false, "nvapi64.dll not found"); }
            catch (EntryPointNotFoundException ex) { return new("nvidia", false, false, ex.Message); }
            catch (Exception ex) { return new("nvidia", false, false, ex.Message); }
            finally { if (session != IntPtr.Zero) { try { Nvapi.DestroySession(session); } catch { } } }
        }

        private static int GetSettingU32(IntPtr session, IntPtr profile, uint settingId, out bool found)
        {
            found = false;
            IntPtr buf = Marshal.AllocHGlobal(NVDRS_SETTING_SIZE);
            try
            {
                for (int o = 0; o < NVDRS_SETTING_SIZE; o += 4) Marshal.WriteInt32(buf, o, 0);
                Marshal.WriteInt32(buf, 0, unchecked((int)NVDRS_SETTING_VER));
                if (Nvapi.GetSetting(session, profile, settingId, buf) != 0) return 0;
                found = true;
                return Marshal.ReadInt32(buf, OFFSET_CURRENT_VALUE);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static AfApplyResult ApplyNvidia()
        {
            IntPtr session = IntPtr.Zero;
            try
            {
                if (Nvapi.Initialize() != 0) return new(true, false, "nvidia", "NvAPI_Initialize failed");
                if (Nvapi.CreateSession(out session) != 0 || session == IntPtr.Zero)
                    return new(true, false, "nvidia", "NvAPI_DRS_CreateSession failed");
                if (Nvapi.LoadSettings(session) != 0)
                    return new(true, false, "nvidia", "NvAPI_DRS_LoadSettings failed");

                IntPtr nameBuf = AllocUnicodeString("Grand Theft Auto V");
                try
                {
                    if (Nvapi.FindProfileByName(session, nameBuf, out IntPtr profile) != 0 || profile == IntPtr.Zero)
                        return new(true, false, "nvidia", Loc.T("error.nvidiaProfileMissing"));

                    int s1 = SetSettingU32(session, profile, ANISO_MODE_SELECTOR_ID, 1);
                    int s2 = SetSettingU32(session, profile, ANISO_MODE_LEVEL_ID, 16);
                    if (s1 != 0 || s2 != 0)
                        return new(true, false, "nvidia", $"NvAPI_DRS_SetSetting failed ({s1}/{s2})");

                    int save = Nvapi.SaveSettings(session);
                    if (save != 0) return new(true, false, "nvidia", $"NvAPI_DRS_SaveSettings failed ({save})");

                    return new(true, true, "nvidia", null);
                }
                finally { Marshal.FreeHGlobal(nameBuf); }
            }
            catch (DllNotFoundException) { return new(false, false, "nvidia", "nvapi64.dll not found"); }
            catch (EntryPointNotFoundException ex) { return new(true, false, "nvidia", ex.Message); }
            finally { if (session != IntPtr.Zero) { try { Nvapi.DestroySession(session); } catch { } } }
        }

        private static int SetSettingU32(IntPtr session, IntPtr profile, uint settingId, uint value)
        {
            IntPtr buf = Marshal.AllocHGlobal(NVDRS_SETTING_SIZE);
            try
            {
                for (int o = 0; o < NVDRS_SETTING_SIZE; o += 4) Marshal.WriteInt32(buf, o, 0);
                Marshal.WriteInt32(buf, 0, unchecked((int)NVDRS_SETTING_VER));
                Marshal.WriteInt32(buf, OFFSET_SETTING_ID, unchecked((int)settingId));
                Marshal.WriteInt32(buf, OFFSET_SETTING_TYPE, 0);
                Marshal.WriteInt32(buf, OFFSET_CURRENT_VALUE, unchecked((int)value));
                return Nvapi.SetSetting(session, profile, buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static IntPtr AllocUnicodeString(string s)
        {
            const int len = 2048;
            IntPtr buf = Marshal.AllocHGlobal(len * 2);
            for (int i = 0; i < len; i++) Marshal.WriteInt16(buf, i * 2, 0);
            for (int i = 0; i < s.Length && i < len - 1; i++) Marshal.WriteInt16(buf, i * 2, (short)s[i]);
            return buf;
        }

        private static class Nvapi
        {
            [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr NvAPI_QueryInterface(uint id);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitDele();
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateSessionDele(out IntPtr session);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LoadSettingsDele(IntPtr session);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindProfileDele(IntPtr session, IntPtr name, out IntPtr profile);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSettingDele(IntPtr session, IntPtr profile, uint settingId, IntPtr setting);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSettingDele(IntPtr session, IntPtr profile, IntPtr setting);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SaveSettingsDele(IntPtr session);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DestroySessionDele(IntPtr session);

            private static InitDele?           _init;
            private static CreateSessionDele?  _create;
            private static LoadSettingsDele?   _load;
            private static FindProfileDele?    _find;
            private static GetSettingDele?     _get;
            private static SetSettingDele?     _set;
            private static SaveSettingsDele?   _save;
            private static DestroySessionDele? _destroy;

            private static T Resolve<T>(uint id) where T : Delegate
            {
                IntPtr p = NvAPI_QueryInterface(id);
                if (p == IntPtr.Zero) throw new EntryPointNotFoundException($"NvAPI function 0x{id:X8} unavailable");
                return Marshal.GetDelegateForFunctionPointer<T>(p);
            }

            public static int Initialize()                                => (_init    ??= Resolve<InitDele>(0x0150E828))();
            public static int CreateSession(out IntPtr s)                 => (_create  ??= Resolve<CreateSessionDele>(0x0694D52E))(out s);
            public static int LoadSettings(IntPtr s)                      => (_load    ??= Resolve<LoadSettingsDele>(0x375DBD6B))(s);
            public static int FindProfileByName(IntPtr s, IntPtr n, out IntPtr p)
                                                                          => (_find    ??= Resolve<FindProfileDele>(0x7E4A9A0B))(s, n, out p);
            public static int GetSetting(IntPtr s, IntPtr pr, uint id, IntPtr set)
                                                                          => (_get     ??= Resolve<GetSettingDele>(0x73BF8338))(s, pr, id, set);
            public static int SetSetting(IntPtr s, IntPtr pr, IntPtr set) => (_set     ??= Resolve<SetSettingDele>(0x577DD202))(s, pr, set);
            public static int SaveSettings(IntPtr s)                      => (_save    ??= Resolve<SaveSettingsDele>(0xFCBC7E14))(s);
            public static int DestroySession(IntPtr s)                    => (_destroy ??= Resolve<DestroySessionDele>(0xDAD9CFF8))(s);
        }

        private static AfDetectResult DetectAmd()
        {
            const string classKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var cls = Registry.LocalMachine.OpenSubKey(classKey);
            if (cls == null) return new("amd", false, false, "display class key missing");

            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4) continue;
                using var k = cls.OpenSubKey(sub);
                if (k == null) continue;
                var desc = (k.GetValue("DriverDesc") as string) ?? string.Empty;
                if (desc.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) < 0 &&
                    desc.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var aniso = ReadRegString(k, "AnisoDegree");
                bool applied = aniso == "16";
                return new("amd", applied, true, $"AnisoDegree={aniso ?? "<none>"}");
            }
            return new("amd", false, false, "no AMD adapter in registry");
        }

        private static AfApplyResult ApplyAmd()
        {
            const string classKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var cls = Registry.LocalMachine.OpenSubKey(classKey);
            if (cls == null) return new(false, false, "amd", "display class key missing");

            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4) continue;
                using (var probe = cls.OpenSubKey(sub))
                {
                    var desc = (probe?.GetValue("DriverDesc") as string) ?? string.Empty;
                    if (desc.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) < 0 &&
                        desc.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                using var k = cls.OpenSubKey(sub, writable: true);
                if (k == null) return new(true, false, "amd", Loc.T("error.amdRegistryNoAccess"));
                k.SetValue("AnisoDegree", Encoding.Unicode.GetBytes("16\0"), RegistryValueKind.Binary);
                k.SetValue("AnisoDegree_NA", Encoding.Unicode.GetBytes("0\0"), RegistryValueKind.Binary);
                return new(true, true, "amd", null);
            }
            return new(false, false, "amd", "no AMD adapter in registry");
        }

        private static string? ReadRegString(RegistryKey key, string name)
        {
            var v = key.GetValue(name);
            if (v is byte[] b) return Encoding.Unicode.GetString(b).TrimEnd('\0');
            if (v is string s) return s;
            return null;
        }
    }
}
