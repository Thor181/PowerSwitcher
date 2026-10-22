using System.Management;
using System.Runtime.Versioning;

namespace PowerSwitcher.Core
{
    [SupportedOSPlatform("windows")]
    public sealed class BrightnessManager
    {
        private const string WmiNamespace = @"root\WMI";

        /// <summary>
        /// Текущая яркость встроенного экрана в процентах (0–100),
        /// либо <c>null</c>, если управление яркостью недоступно.
        /// </summary>
        public byte? GetBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    WmiNamespace,
                    "SELECT CurrentBrightness FROM WmiMonitorBrightness");

                foreach (ManagementObject item in searcher.Get())
                {
                    return Convert.ToByte(item["CurrentBrightness"]);
                }
            }
            catch
            {
                // Управление яркостью недоступно (например, внешний монитор без DDC/CI).
            }

            return null;
        }

        /// <summary>
        /// Устанавливает яркость встроенного экрана в процентах (0–100).
        /// </summary>
        public bool SetBrightness(byte brightness)
        {
            if (brightness > 100)
                brightness = 100;

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    WmiNamespace,
                    "SELECT * FROM WmiMonitorBrightnessMethods");

                foreach (ManagementObject item in searcher.Get())
                {
                        var result = item.InvokeMethod(
                        "WmiSetBrightness",
                        new object[] { (uint)1, brightness });

                    var mbo = result as ManagementBaseObject;
                    if (mbo != null)
                    {
                        var returnValue = mbo["ReturnValue"];
                        return returnValue == null || Convert.ToUInt32(returnValue) == 0;
                    }

                    return true;
                }
            }
            catch
            {
                // Управление яркостью недоступно.
            }

            return false;
        }
    }
}
