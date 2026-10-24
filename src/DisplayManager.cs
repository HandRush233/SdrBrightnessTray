using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SdrBrightnessTray
{
    internal sealed class DisplaySnapshot
    {
        public bool HasHdrDisplay;
        public bool HdrEnabled;
        public bool HasReading;
        public int WhiteLevel;
        public string Error;
    }

    internal sealed class ApplyResult
    {
        public bool Success;
        public bool UsedFallback;
        public string Error;
    }

    /// <summary>
    /// Talks to the Windows display configuration API. The SDR white level setter is
    /// the same private packet the Settings app uses; the DWM boost is only a fallback
    /// for environments that deny the display configuration write.
    /// </summary>
    internal sealed class DisplayController
    {
        private const uint QdcOnlyActivePaths = 2;

        private const uint GetAdvancedColorInfo = 9;
        private const uint GetSdrWhiteLevel = 11;
        private const uint GetAdvancedColorInfo2 = 15;
        private const uint SetSdrWhiteLevel = 0xFFFFFFEE;

        private const int ErrorSuccess = 0;
        private const int ErrorAccessDenied = 5;
        private const int ErrorNotSupported = 50;

        private const int MinWhiteLevel = 1000;   // 80 nits
        private const int MaxWhiteLevel = 6000;   // 480 nits
        private const int WhiteLevelStep = 50;    // one percent

        private readonly List<DisplayTarget> _targets = new List<DisplayTarget>();

        public DisplaySnapshot Refresh()
        {
            DisplaySnapshot snapshot = new DisplaySnapshot();
            try
            {
                _targets.Clear();
                int queryError;
                List<DisplayTarget> targets = EnumerateTargets(out queryError);
                if (targets == null)
                {
                    snapshot.Error = DescribeError(queryError);
                    return snapshot;
                }

                _targets.AddRange(targets);
                DisplayTarget chosen = null;
                for (int i = 0; i < _targets.Count; i++)
                {
                    DisplayTarget target = _targets[i];
                    if (target.HdrEnabled)
                    {
                        snapshot.HasHdrDisplay = true;
                        snapshot.HdrEnabled = true;
                        if (chosen == null) chosen = target;
                    }
                    else if (target.HdrSupported)
                    {
                        snapshot.HasHdrDisplay = true;
                    }
                }

                if (chosen == null)
                {
                    for (int i = 0; i < _targets.Count; i++)
                    {
                        if (_targets[i].HdrSupported) { chosen = _targets[i]; break; }
                    }
                }
                if (chosen == null && _targets.Count > 0) chosen = _targets[0];

                if (chosen != null)
                {
                    snapshot.HasReading = chosen.HasWhiteLevel;
                    snapshot.WhiteLevel = chosen.WhiteLevel;
                }

                if (!snapshot.HasHdrDisplay && !snapshot.HdrEnabled)
                {
                    snapshot.Error = "未检测到 HDR 显示器";
                }
            }
            catch (Exception ex)
            {
                snapshot.Error = "读取显示器信息失败：" + ex.Message;
            }
            return snapshot;
        }

        public ApplyResult Apply(int percent)
        {
            ApplyResult result = new ApplyResult();
            percent = Math.Max(0, Math.Min(100, percent));
            int whiteLevel = MinWhiteLevel + percent * WhiteLevelStep;
            int lastError = ErrorSuccess;
            int attempted = 0;
            int succeeded = 0;

            for (int i = 0; i < _targets.Count; i++)
            {
                DisplayTarget target = _targets[i];
                if (!target.HdrEnabled) continue;
                attempted++;
                int error = SetTargetWhiteLevel(target, whiteLevel);
                if (error == ErrorSuccess) succeeded++;
                else lastError = error;
            }

            if (succeeded > 0)
            {
                result.Success = true;
                return result;
            }

            // The display config write can be denied for callers outside the shell's
            // console session. DwmpSDRToHDRBoost still changes the composited result,
            // so keep the slider useful instead of failing silently.
            if (attempted > 0 || _targets.Count > 0)
            {
                int boostError = SetDwmBoost(whiteLevel / 1000.0);
                if (boostError == ErrorSuccess)
                {
                    result.Success = true;
                    result.UsedFallback = true;
                    return result;
                }
                if (lastError == ErrorSuccess) lastError = boostError;
            }

            result.Error = lastError == ErrorAccessDenied
                ? "调整被系统拒绝，请确认 HDR 已开启"
                : DescribeError(lastError);
            return result;
        }

        public static int PercentFromWhiteLevel(int whiteLevel)
        {
            int percent = (int)Math.Round((whiteLevel - MinWhiteLevel) / (double)WhiteLevelStep);
            return Math.Max(0, Math.Min(100, percent));
        }

        public static int NitsFromPercent(int percent)
        {
            return (MinWhiteLevel + percent * WhiteLevelStep) * 80 / 1000;
        }

        private static List<DisplayTarget> EnumerateTargets(out int queryError)
        {
            queryError = ErrorSuccess;
            uint pathCount;
            uint modeCount;
            int error = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out pathCount, out modeCount);
            if (error != ErrorSuccess)
            {
                queryError = error;
                return null;
            }
            if (pathCount == 0)
            {
                queryError = 1168; // ERROR_NOT_FOUND
                return null;
            }

            PathInfo[] paths = new PathInfo[pathCount];
            ModeInfo[] modes = new ModeInfo[modeCount];
            for (int i = 0; i < modes.Length; i++) modes[i].Data = new byte[48];

            error = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (error != ErrorSuccess)
            {
                queryError = error;
                return null;
            }

            List<DisplayTarget> targets = new List<DisplayTarget>();
            int build = Environment.OSVersion.Version.Build;
            for (int i = 0; i < pathCount; i++)
            {
                PathInfo path = paths[i];
                Luid adapterId = path.TargetInfo.AdapterId;
                uint targetId = path.TargetInfo.Id;
                uint modeIndex = path.TargetInfo.ModeInfoIdx;
                if (modeIndex < modeCount)
                {
                    adapterId = modes[modeIndex].AdapterId;
                    targetId = modes[modeIndex].Id;
                }

                DisplayTarget target = new DisplayTarget();
                target.AdapterId = adapterId;
                target.TargetId = targetId;

                bool haveHdrInfo = false;
                if (build >= 26100)
                {
                    AdvancedColorInfo2 info2 = new AdvancedColorInfo2();
                    info2.Header.Type = GetAdvancedColorInfo2;
                    info2.Header.Size = (uint)Marshal.SizeOf(typeof(AdvancedColorInfo2));
                    info2.Header.AdapterId = adapterId;
                    info2.Header.Id = targetId;
                    error = DisplayConfigGetDeviceInfo(ref info2.Header);
                    if (error == ErrorSuccess)
                    {
                        target.HdrSupported = (info2.Value & (1u << 4)) != 0;
                        target.HdrEnabled = info2.ActiveColorMode == 2;
                        haveHdrInfo = true;
                    }
                }

                if (!haveHdrInfo)
                {
                    AdvancedColorInfo info = new AdvancedColorInfo();
                    info.Header.Type = GetAdvancedColorInfo;
                    info.Header.Size = (uint)Marshal.SizeOf(typeof(AdvancedColorInfo));
                    info.Header.AdapterId = adapterId;
                    info.Header.Id = targetId;
                    error = DisplayConfigGetDeviceInfo(ref info.Header);
                    if (error == ErrorSuccess)
                    {
                        target.HdrSupported = (info.Value & 1u) != 0;
                        target.HdrEnabled = (info.Value & 2u) != 0;
                    }
                }

                SdrWhiteInfo white = new SdrWhiteInfo();
                white.Header.Type = GetSdrWhiteLevel;
                white.Header.Size = (uint)Marshal.SizeOf(typeof(SdrWhiteInfo));
                white.Header.AdapterId = adapterId;
                white.Header.Id = targetId;
                error = DisplayConfigGetDeviceInfo(ref white.Header);
                if (error == ErrorSuccess && white.WhiteLevel > 0)
                {
                    target.HasWhiteLevel = true;
                    target.WhiteLevel = (int)white.WhiteLevel;
                }

                if (target.HdrSupported || target.HdrEnabled || target.HasWhiteLevel)
                {
                    bool duplicate = false;
                    for (int j = 0; j < targets.Count; j++)
                    {
                        if (targets[j].TargetId == target.TargetId &&
                            targets[j].AdapterId.LowPart == target.AdapterId.LowPart &&
                            targets[j].AdapterId.HighPart == target.AdapterId.HighPart)
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate) targets.Add(target);
                }
            }

            return targets;
        }

        private static int SetTargetWhiteLevel(DisplayTarget target, int whiteLevel)
        {
            SetSdrWhiteInfo packet = new SetSdrWhiteInfo();
            packet.Header.Type = SetSdrWhiteLevel;
            packet.Header.Size = (uint)Marshal.SizeOf(typeof(SetSdrWhiteInfo));
            packet.Header.AdapterId = target.AdapterId;
            packet.Header.Id = target.TargetId;
            packet.WhiteLevel = (uint)whiteLevel;
            packet.FinalValue = 1;
            return DisplayConfigSetDeviceInfo(ref packet.Header);
        }

        private static string DescribeError(int code)
        {
            if (code == ErrorAccessDenied) return "系统拒绝了显示设置（错误 5）";
            if (code == ErrorNotSupported) return "当前显示器或驱动不支持此功能";
            if (code == 1168) return "未检测到活动显示器";
            return code == ErrorSuccess ? "无法调整亮度" : "调整失败（错误 " + code + "）";
        }

        private static int SetDwmBoost(double value)
        {
            if (value < 1.0) value = 1.0;
            if (value > 6.0) value = 6.0;
            try
            {
                if (DwmBoost.Function == null) return 127;
                IntPtr monitor = MonitorFromWindow(IntPtr.Zero, 1);
                if (monitor == IntPtr.Zero) return 1168;
                return DwmBoost.Function(monitor, value);
            }
            catch
            {
                return 127;
            }
        }

        private sealed class DisplayTarget
        {
            public Luid AdapterId;
            public uint TargetId;
            public bool HdrSupported;
            public bool HdrEnabled;
            public bool HasWhiteLevel;
            public int WhiteLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rational
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PathSourceInfo
        {
            public Luid AdapterId;
            public uint Id;
            public uint ModeInfoIdx;
            public uint StatusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PathTargetInfo
        {
            public Luid AdapterId;
            public uint Id;
            public uint ModeInfoIdx;
            public uint OutputTechnology;
            public uint Rotation;
            public uint Scaling;
            public Rational RefreshRate;
            public uint ScanLineOrdering;
            public int TargetAvailable;
            public uint StatusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PathInfo
        {
            public PathSourceInfo SourceInfo;
            public PathTargetInfo TargetInfo;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ModeInfo
        {
            public uint InfoType;
            public uint Id;
            public Luid AdapterId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
            public byte[] Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInfoHeader
        {
            public uint Type;
            public uint Size;
            public Luid AdapterId;
            public uint Id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdvancedColorInfo
        {
            public DeviceInfoHeader Header;
            public uint Value;
            public uint ColorEncoding;
            public uint BitsPerColorChannel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdvancedColorInfo2
        {
            public DeviceInfoHeader Header;
            public uint Value;
            public uint ColorEncoding;
            public uint BitsPerColorChannel;
            public uint ActiveColorMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SdrWhiteInfo
        {
            public DeviceInfoHeader Header;
            public uint WhiteLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SetSdrWhiteInfo
        {
            public DeviceInfoHeader Header;
            public uint WhiteLevel;
            public byte FinalValue;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PathInfo[] paths,
            ref uint numMode, [Out] ModeInfo[] modes, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DeviceInfoHeader header);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DeviceInfoHeader header);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DwmBoostDelegate(IntPtr monitor, double brightness);

        private static class DwmBoost
        {
            public static readonly DwmBoostDelegate Function = Load();

            private static DwmBoostDelegate Load()
            {
                try
                {
                    IntPtr module = LoadLibrary("dwmapi.dll");
                    if (module == IntPtr.Zero) return null;
                    IntPtr address = GetProcAddress(module, new IntPtr(171));
                    if (address == IntPtr.Zero) return null;
                    return (DwmBoostDelegate)Marshal.GetDelegateForFunctionPointer(address, typeof(DwmBoostDelegate));
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
