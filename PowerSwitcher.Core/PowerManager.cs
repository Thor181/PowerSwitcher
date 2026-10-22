using System.Runtime.InteropServices;

namespace PowerSwitcher.Core
{
    public enum CpuBoostMode
    {
        Disabled = 0,
        Enabled = 1,
        Aggressive = 2,
        EfficientEnabled = 3,
        EfficientAggressive = 4,
        AggressiveAtGuaranteed = 5,
        EfficientAggressiveAtGuaranteed = 6,
    }

    public sealed class PowerSchemeInfo
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public bool IsActive { get; init; }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class PowerManager
    {
        public static readonly Guid BalancedScheme = new("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid HighPerformanceScheme = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid PowerSaverScheme = new("a1841308-3541-4fab-bc81-f71556f20b4a");

        private const uint ACCESS_SCHEME = 16;
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_NO_MORE_ITEMS = 259;

        private static readonly Guid SubGroupProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Guid ProcessorBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");

        // Недокументированное WNF-состояние режима экономии заряда Windows 11
        // (его же переключает плитка «Энергосбережение» в быстрых настройках): 1 — вкл, 2 — выкл.
        private const ulong WnfEnergySaverState = 0x41C6013DA3BC3075;
        private const int EnergySaverOn = 1;
        private const int EnergySaverOff = 2;

        [DllImport("ntdll.dll")]
        private static extern int RtlPublishWnfStateData(
            ulong stateName,
            IntPtr typeId,
            byte[] buffer,
            int length,
            IntPtr explicitScope);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryWnfStateData(
            ref ulong stateName,
            IntPtr typeId,
            IntPtr explicitScope,
            out uint changeStamp,
            byte[] buffer,
            ref int bufferSize);

        [DllImport("powrprof.dll")]
        private static extern uint PowerEnumerate(
            IntPtr rootPowerKey,
            IntPtr schemeGuid,
            IntPtr subGroupOfPowerSettingsGuid,
            uint accessFlags,
            uint index,
            ref Guid buffer,
            ref uint bufferSize);

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(
            IntPtr userRootPowerKey,
            out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(
            IntPtr userRootPowerKey,
            ref Guid schemeGuid);

        [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadFriendlyName(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            IntPtr subGroupOfPowerSettingsGuid,
            IntPtr powerSettingGuid,
            IntPtr buffer,
            ref uint bufferSize);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            uint acValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            uint dcValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            out uint acValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid,
            out uint dcValueIndex);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public IReadOnlyList<PowerSchemeInfo> GetPowerSchemes()
        {
            var result = new List<PowerSchemeInfo>();
            var activeId = GetActiveSchemeId();

            for (uint i = 0; ; i++)
            {
                var guid = Guid.Empty;
                uint size = (uint)Marshal.SizeOf<Guid>();

                uint status = PowerEnumerate(
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    ACCESS_SCHEME, i, ref guid, ref size);

                if (status == ERROR_NO_MORE_ITEMS)
                    break;

                if (status != ERROR_SUCCESS)
                    break;

                result.Add(new PowerSchemeInfo
                {
                    Id = guid,
                    Name = ReadFriendlyName(guid),
                    IsActive = activeId == guid,
                });
            }

            return result;
        }

        public PowerSchemeInfo? GetActiveScheme()
        {
            var id = GetActiveSchemeId();
            if (id is null)
                return null;

            return new PowerSchemeInfo
            {
                Id = id.Value,
                Name = ReadFriendlyName(id.Value),
                IsActive = true,
            };
        }

        public bool SetActiveScheme(Guid schemeGuid)
        {
            return PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid) == ERROR_SUCCESS;
        }

        public CpuBoostMode? GetCpuBoostMode()
        {
            var schemeId = GetActiveSchemeId();
            if (schemeId is null)
                return null;

            var scheme = schemeId.Value;
            var subGroupProcessor = SubGroupProcessor;
            var processorBoostMode = ProcessorBoostMode;

            uint status = PowerReadACValueIndex(
                IntPtr.Zero, ref scheme, ref subGroupProcessor, ref processorBoostMode, out uint value);

            if (status != ERROR_SUCCESS)
                return null;

            return (CpuBoostMode)value;
        }

        public bool SetCpuBoostMode(CpuBoostMode mode)
        {
            var schemeId = GetActiveSchemeId();
            if (schemeId is null)
                return false;

            var scheme = schemeId.Value;
            var subGroupProcessor = SubGroupProcessor;
            var processorBoostMode = ProcessorBoostMode;
            uint value = (uint)mode;

            uint acStatus = PowerWriteACValueIndex(
                IntPtr.Zero, ref scheme, ref subGroupProcessor, ref processorBoostMode, value);
            uint dcStatus = PowerWriteDCValueIndex(
                IntPtr.Zero, ref scheme, ref subGroupProcessor, ref processorBoostMode, value);

            if (acStatus != ERROR_SUCCESS || dcStatus != ERROR_SUCCESS)
                return false;

            // Re-applying the active scheme makes the new value take effect immediately.
            return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == ERROR_SUCCESS;
        }

        public bool SetTurboBoostEnabled(bool enabled)
        {
            return SetCpuBoostMode(enabled ? CpuBoostMode.Enabled : CpuBoostMode.Disabled);
        }

        /// <summary>
        /// Включён ли сейчас режим экономии заряда, либо <c>null</c>, если состояние недоступно
        /// (например, Windows 10 или сборка без такого WNF-состояния).
        /// </summary>
        public bool? GetEnergySaver()
        {
            var stateName = WnfEnergySaverState;
            var buffer = new byte[sizeof(int)];
            int size = buffer.Length;

            int status = NtQueryWnfStateData(
                ref stateName, IntPtr.Zero, IntPtr.Zero, out _, buffer, ref size);

            if (status != 0 || size < sizeof(int))
                return null;

            return BitConverter.ToInt32(buffer, 0) switch
            {
                EnergySaverOn => true,
                EnergySaverOff => false,
                _ => null,
            };
        }

        /// <summary>
        /// Включает или выключает режим экономии заряда сразу — от сети и от батареи,
        /// так же как плитка в быстрых настройках. Схема питания при этом не переактивируется.
        /// </summary>
        public bool SetEnergySaver(bool enabled)
        {
            var data = BitConverter.GetBytes(enabled ? EnergySaverOn : EnergySaverOff);

            return RtlPublishWnfStateData(
                WnfEnergySaverState, IntPtr.Zero, data, data.Length, IntPtr.Zero) == 0;
        }

        private Guid? GetActiveSchemeId()
        {
            uint status = PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr);
            if (status != ERROR_SUCCESS || ptr == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStructure<Guid>(ptr);
            }
            finally
            {
                LocalFree(ptr);
            }
        }

        private static string ReadFriendlyName(Guid scheme)
        {
            uint size = 0;
            PowerReadFriendlyName(
                IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);

            if (size == 0)
                return scheme.ToString("B");

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint status = PowerReadFriendlyName(
                    IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size);

                if (status == ERROR_SUCCESS)
                    return Marshal.PtrToStringUni(buffer) ?? scheme.ToString("B");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return scheme.ToString("B");
        }
    }
}
