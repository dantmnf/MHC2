using System.Runtime.InteropServices;
using static Vanara.PInvoke.Gdi32;
using static Vanara.PInvoke.User32;
using static Vanara.PInvoke.SetupAPI;

namespace CapsCheck
{

    record class DisplaySourceMode(int X, int Y, int Width, int Height, DISPLAYCONFIG_PIXELFORMAT PixelFormat);

    record class DisplayPath(ulong SourceAdapterLuid, uint VidPnSourceId, DisplaySourceMode SourceMode, string SourceGdiName, IntPtr GdiMonitorHandle, ulong TargetAdapterLuid, uint VidPnTargetId, bool IsCloned)
    {
        public string AdapterName { get; init; } = FindFriendlyName(DisplayConfigGetDeviceInfo<DISPLAYCONFIG_ADAPTER_NAME>(TargetAdapterLuid, VidPnTargetId).adapterDevicePath) ?? string.Empty;
        public string MonitorName { get; init; } = GetMonitorName(TargetAdapterLuid, VidPnTargetId);

        private static string GetMonitorName(ulong TargetAdapterLuid, uint VidPnOutputId)
        {
            var dev = DisplayConfigGetDeviceInfo<DISPLAYCONFIG_TARGET_DEVICE_NAME>(TargetAdapterLuid, VidPnOutputId);
            var edidname = dev.monitorFriendlyDeviceName;
            if (!string.IsNullOrEmpty(edidname)) return edidname;
            return FindFriendlyName(dev.monitorDevicePath) ?? string.Empty;
        }

        private static string? FindFriendlyName(string devicepath)
        {
            var instanceid = string.Join('\\', devicepath.Substring(4).Split('#').Take(3));
            using var infoSet = SetupDiGetClassDevs(IntPtr.Zero, instanceid, IntPtr.Zero, DIGCF.DIGCF_DEVICEINTERFACE | DIGCF.DIGCF_ALLCLASSES);
            var err = Marshal.GetLastWin32Error();
            if (infoSet.IsInvalid) return null;
            foreach (var devinfo in SetupDiEnumDeviceInfo(infoSet))
            {
                object nameobj;
                SetupDiGetDeviceProperty(infoSet, devinfo, DEVPKEY_Device_FriendlyName, out nameobj);
                err = Marshal.GetLastWin32Error();
                if (nameobj != null) return nameobj.ToString();
                SetupDiGetDeviceProperty(infoSet, devinfo, DEVPKEY_NAME, out nameobj);
                err = Marshal.GetLastWin32Error();
                return nameobj as string;
            }
            return null;
        }


        public static ICollection<DisplayPath> GetDisplayPaths()
        {
            const uint DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE = 0x00000008;
            QueryDisplayConfig(QDC.QDC_ONLY_ACTIVE_PATHS, out var paths, out var modes, out var topId).ThrowIfFailed();
            var result = new List<DisplayPath>();
            var sourceTargetCount = new Dictionary<(ulong, uint), int>();
            var gdiNameToHandle = new Dictionary<string, IntPtr>();
            EnumDisplayMonitors(IntPtr.Zero, null, (current_hmon, hdc, lprect, lparam) => {
                var moninfo = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(current_hmon, ref moninfo))
                {
                    gdiNameToHandle[moninfo.szDevice.ToUpperInvariant()] =  current_hmon;
                }
                return true;
            }, IntPtr.Zero);
            foreach (var path in paths)
            {
                var key = (path.sourceInfo.adapterId, path.sourceInfo.id);
                sourceTargetCount[key] = sourceTargetCount.GetValueOrDefault(key, 0) + 1;
            }
            foreach (var path in paths)
            {
                var key = (path.sourceInfo.adapterId, path.sourceInfo.id);
                var modeidx = (path.sourceInfo.statusFlags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) != 0 ? path.sourceInfo.union.sourceModeInfoIdx : path.sourceInfo.union.modeInfoIdx;
                ref var nativemode = ref modes[modeidx].sourceMode;
                var mode = new DisplaySourceMode(nativemode.position.X, nativemode.position.Y, (int)nativemode.width, (int)nativemode.height, nativemode.pixelFormat);
                var gdiname = DisplayConfigGetDeviceInfo<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(path.sourceInfo.adapterId, path.sourceInfo.id).viewGdiDeviceName;
                result.Add(new(path.sourceInfo.adapterId, path.sourceInfo.id, mode, gdiname, gdiNameToHandle.GetValueOrDefault(gdiname.ToUpperInvariant(), IntPtr.Zero), path.targetInfo.adapterId, path.targetInfo.id, sourceTargetCount.GetValueOrDefault(key, 0) > 1));
            }
            return result.AsReadOnly();
        }

        public unsafe DisplayColorManagementCapability GetColorManagementCapability()
        {
            if (IsCloned)
            {
                return new DisplayColorManagementCapability { MatrixDDI = false, SdrAcmCapability = SdrAcmCapability.Unsupported };
            }
            var mhc2support = false;
            var undocinfo = new DISPLAYCONFIG_UNDOC_INFO
            {
                header = new()
                {
                    type = unchecked((DISPLAYCONFIG_DEVICE_INFO_TYPE)(-12)),
                    size = 24,
                    adapterId = TargetAdapterLuid,
                    id = VidPnTargetId
                }
            };
            if (DisplayConfigGetDeviceInfo((IntPtr)(&undocinfo)).Succeeded)
            {
                mhc2support = (undocinfo.value & 1) != 0;
            }
            var acminfo = DisplayConfigGetDeviceInfo<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(TargetAdapterLuid, VidPnTargetId);
            var rtcaps = false;
            var sdracmsupport = SdrAcmCapability.Unsupported;
            var hdrsupport = false;
            var wcgsupport = false;
            try
            {
                if (GdiMonitorHandle != IntPtr.Zero)
                {
                    var rtinfo = Windows.Graphics.Display.DisplayInformationInterop.GetForMonitor(GdiMonitorHandle);
                    hdrsupport = rtinfo.GetAdvancedColorInfo().IsAdvancedColorKindAvailable(Windows.Graphics.Display.AdvancedColorKind.HighDynamicRange);
                    wcgsupport = rtinfo.GetAdvancedColorInfo().IsAdvancedColorKindAvailable(Windows.Graphics.Display.AdvancedColorKind.WideColorGamut);
                    rtcaps = true;
                }
            }
            catch { }

            if (wcgsupport)
            {
                sdracmsupport = SdrAcmCapability.Available;
            }
            else if (hdrsupport)
            {
                sdracmsupport = SdrAcmCapability.Unsupported;
            }
            else if (Environment.OSVersion.Version.Build >= 22621 && mhc2support)
            {
                sdracmsupport = SdrAcmCapability.Disabled;
            }

            var ccdextra = new DisplayColorManagementCapability.DisplayConfigExtraInfo(
                undocinfo.value,
                (uint)acminfo.value,
                acminfo.value.HasFlag(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_VALUE.advancedColorSupported),
                acminfo.value.HasFlag(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_VALUE.wideColorEnforced)
            );
            var uwpextra = rtcaps ? new DisplayColorManagementCapability.UwpExtraInfo(hdrsupport, wcgsupport) : null;
            return new DisplayColorManagementCapability {
                MatrixDDI = mhc2support,
                SdrAcmCapability = sdracmsupport,
                DisplayConfigExtra = ccdextra,
                UwpExtra = uwpextra
            };
        }
    }

    internal enum SdrAcmCapability
    {
        Unsupported,
        Disabled,
        Available
    }

    internal record class DisplayColorManagementCapability
    {
        public bool MatrixDDI { get; init; }
        public SdrAcmCapability SdrAcmCapability { get; init; }
        public DisplayConfigExtraInfo? DisplayConfigExtra { get; init; }
        public UwpExtraInfo? UwpExtra { get; init; }

        public record class DisplayConfigExtraInfo(
            int UndocumentedDisplayConfigValue,
            uint DisplayConfigAdvancedColorInfo,
            bool AdvancedColorSupported,
            bool WideColorEnforced
        );

        public record class UwpExtraInfo(
            bool HighDynamicRangeAvailable,
            bool WideColorGamutAvailable
        );
    }

    struct DISPLAYCONFIG_UNDOC_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public int value;
    }

    internal class Program
    {
        static unsafe void Main(string[] args)
        {
            var paths = DisplayPath.GetDisplayPaths();
            foreach (var path in paths)
            {
                Console.WriteLine(path);
                if (!path.IsCloned)
                {
                    Console.WriteLine(path.GetColorManagementCapability());
                }
                Console.WriteLine();
            }
        }
    }
}