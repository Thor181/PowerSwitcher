using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PowerSwitcher.Core
{
    public sealed class BatteryInfo
    {
        public bool IsOnAc { get; init; }

        public bool HasBattery { get; init; }

        public bool IsCharging { get; init; }

        public int? Percent { get; init; }

        /// <summary>Оставшееся время работы от батареи, если Windows его знает.</summary>
        public TimeSpan? TimeRemaining { get; init; }

        /// <summary>Мощность в ваттах: &gt; 0 — зарядка, &lt; 0 — разряд.</summary>
        public double? PowerWatts { get; init; }
    }

    [SupportedOSPlatform("windows")]
    public sealed class BatteryMonitor
    {
        private const string WmiNamespace = @"root\WMI";

        private const byte AcOnline = 1;
        private const byte BatteryFlagCharging = 8;
        private const byte BatteryFlagNoBattery = 128;
        private const byte BatteryFlagUnknown = 255;
        private const byte UnknownPercent = 255;
        private const uint UnknownTime = uint.MaxValue;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        public BatteryInfo GetStatus()
        {
            if (!GetSystemPowerStatus(out var status))
                return new BatteryInfo { IsOnAc = true };

            bool hasBattery = (status.BatteryFlag & BatteryFlagNoBattery) == 0
                && status.BatteryFlag != BatteryFlagUnknown;

            return new BatteryInfo
            {
                IsOnAc = status.ACLineStatus == AcOnline,
                HasBattery = hasBattery,
                IsCharging = hasBattery && (status.BatteryFlag & BatteryFlagCharging) != 0,
                Percent = hasBattery && status.BatteryLifePercent != UnknownPercent
                    ? status.BatteryLifePercent
                    : null,
                TimeRemaining = hasBattery && status.BatteryLifeTime != UnknownTime
                    ? TimeSpan.FromSeconds(status.BatteryLifeTime)
                    : null,
                PowerWatts = hasBattery ? ReadPowerWatts() : null,
            };
        }

        private static double? ReadPowerWatts()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    WmiNamespace,
                    "SELECT ChargeRate, DischargeRate, Charging, Discharging FROM BatteryStatus");

                foreach (ManagementObject item in searcher.Get())
                {
                    // Значения в милливаттах
                    if (item["Discharging"] is true)
                        return -Convert.ToDouble(item["DischargeRate"]) / 1000;
                    if (item["Charging"] is true)
                        return Convert.ToDouble(item["ChargeRate"]) / 1000;
                    return 0;
                }
            }
            catch
            {
                // Драйвер батареи не отдаёт мощность через WMI.
            }

            return null;
        }
    }
}
