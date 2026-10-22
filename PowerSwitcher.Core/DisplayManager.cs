using System.Runtime.InteropServices;

namespace PowerSwitcher.Core
{
    public sealed class DisplayManager
    {
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int ENUM_REGISTRY_SETTINGS = -2;

        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DISP_CHANGE_RESTART = 1;
        private const int DISP_CHANGE_FAILED = -1;
        private const int DISP_CHANGE_BADMODE = -2;
        private const int DISP_CHANGE_NOTUPDATED = -3;
        private const int DISP_CHANGE_BADFLAGS = -4;
        private const int DISP_CHANGE_BADPARAM = -5;
        private const int DISP_CHANGE_BADDUALVIEW = -6;

        private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
        private const int DM_DISPLAYFREQUENCY = 0x00400000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;

            public int StateFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;

            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;

            public int dmFields;

            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;

            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;

            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;

            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;

            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(
            string? lpDevice,
            uint iDevNum,
            ref DISPLAY_DEVICE lpDisplayDevice,
            uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(
            string? lpszDeviceName,
            int iModeNum,
            ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(
            string? lpszDeviceName,
            ref DEVMODE lpDevMode,
            IntPtr hwnd,
            uint dwflags,
            IntPtr lParam);

        public IReadOnlyList<DisplayInfo> GetDisplays()
        {
            var result = new List<DisplayInfo>();

            for (uint i = 0; ; i++)
            {
                var device = new DISPLAY_DEVICE
                {
                    cb = Marshal.SizeOf<DISPLAY_DEVICE>()
                };

                if (!EnumDisplayDevices(null, i, ref device, 0))
                    break;

                if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                    continue;

                var mode = new DEVMODE
                {
                    dmSize = (short)Marshal.SizeOf<DEVMODE>()
                };

                if (!EnumDisplaySettings(
                        device.DeviceName,
                        ENUM_CURRENT_SETTINGS,
                        ref mode))
                {
                    continue;
                }

                result.Add(new DisplayInfo
                {
                    DeviceName = device.DeviceName,
                    Name = device.DeviceString,
                    DeviceId = device.DeviceID,
                    Width = mode.dmPelsWidth,
                    Height = mode.dmPelsHeight,
                    RefreshRate = mode.dmDisplayFrequency,
                    BitsPerPixel = mode.dmBitsPerPel,
                    PositionX = mode.dmPositionX,
                    PositionY = mode.dmPositionY
                });
            }

            return result;
        }

        public IReadOnlyList<int> GetSupportedRefreshRates(
            string deviceName)
        {
            var frequencies = new HashSet<int>();

            for (int modeIndex = 0; ; modeIndex++)
            {
                var mode = new DEVMODE
                {
                    dmSize = (short)Marshal.SizeOf<DEVMODE>()
                };

                if (!EnumDisplaySettings(
                        deviceName,
                        modeIndex,
                        ref mode))
                {
                    break;
                }

                if (mode.dmDisplayFrequency > 1)
                    frequencies.Add(mode.dmDisplayFrequency);
            }

            return frequencies
                .OrderBy(x => x)
                .ToArray();
        }

        public bool SetRefreshRate(
            string deviceName,
            int refreshRate)
        {
            var mode = new DEVMODE
            {
                dmSize = (short)Marshal.SizeOf<DEVMODE>()
            };

            if (!EnumDisplaySettings(
                    deviceName,
                    ENUM_CURRENT_SETTINGS,
                    ref mode))
            {
                return false;
            }

            mode.dmDisplayFrequency = refreshRate;
            mode.dmFields |= DM_DISPLAYFREQUENCY;

            var result = ChangeDisplaySettingsEx(
                deviceName,
                ref mode,
                IntPtr.Zero,
                0,
                IntPtr.Zero);

            return result == DISP_CHANGE_SUCCESSFUL;
        }
    }

    public sealed class DisplayInfo
    {
        public required string DeviceName { get; init; }

        public required string Name { get; init; }

        public required string DeviceId { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }

        public int RefreshRate { get; init; }

        public int BitsPerPixel { get; init; }

        public int PositionX { get; init; }

        public int PositionY { get; init; }

        public override string ToString()
        {
            return $"{Name} - {Width}x{Height} @ {RefreshRate} Hz";
        }
    }
}
