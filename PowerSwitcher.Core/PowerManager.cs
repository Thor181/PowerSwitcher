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

        // У гибридных процессоров (P- и E-ядра) вторая настройка относится к энергоэффективным ядрам
        private static readonly Guid ProcessorMaxState = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
        private static readonly Guid ProcessorMaxState1 = new("bc5038f7-23e0-4960-96da-33abaf5935ed");
        private static readonly Guid ProcessorEpp = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
        private static readonly Guid ProcessorEpp1 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864");

        private static readonly Guid SubGroupVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
        private static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
        private static readonly Guid AdaptiveBrightnessSetting = new("fbd9aa66-9553-4097-ba44-ed6e9d65eab8");

        private static readonly Guid SubGroupSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

        // Режимы питания Windows 11 — «оверлеи» поверх сбалансированной схемы
        private static readonly Guid OverlayBestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
        private static readonly Guid OverlayBalanced = Guid.Empty;
        private static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

        // «Effective» на части систем всегда возвращает пустой GUID, «actual» — выбранный режим
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActualOverlayScheme(out Guid actualOverlayGuid);

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
            return ReadValue(SubGroupProcessor, ProcessorBoostMode) is uint value
                ? (CpuBoostMode)value
                : null;
        }

        public bool SetCpuBoostMode(CpuBoostMode mode)
        {
            return WriteValue(SubGroupProcessor, ProcessorBoostMode, (uint)mode);
        }

        public byte? GetMaxProcessorState()
        {
            return ReadValue(SubGroupProcessor, ProcessorMaxState) is uint value
                ? (byte)Math.Min(value, 100)
                : null;
        }

        public bool SetMaxProcessorState(byte percent)
        {
            uint value = Math.Clamp(percent, (byte)5, (byte)100);
            bool ok = WriteValue(SubGroupProcessor, ProcessorMaxState, value);
            // Настройки для E-ядер может не быть (не гибридный процессор) — это не ошибка
            WriteValue(SubGroupProcessor, ProcessorMaxState1, value);
            return ok;
        }

        public byte? GetEnergyPerformancePreference()
        {
            return ReadValue(SubGroupProcessor, ProcessorEpp) is uint value
                ? (byte)Math.Min(value, 100)
                : null;
        }

        public bool SetEnergyPerformancePreference(byte preference)
        {
            uint value = Math.Min(preference, (byte)100);
            bool ok = WriteValue(SubGroupProcessor, ProcessorEpp, value);
            WriteValue(SubGroupProcessor, ProcessorEpp1, value);
            return ok;
        }

        /// <summary>Таймаут отключения экрана в секундах (0 — никогда).</summary>
        public int? GetDisplayOffTimeout()
        {
            return ReadValue(SubGroupVideo, VideoIdle) is uint value ? (int)value : null;
        }

        public bool SetDisplayOffTimeout(int seconds)
        {
            return WriteValue(SubGroupVideo, VideoIdle, (uint)Math.Max(seconds, 0));
        }

        /// <summary>Таймаут перехода в сон в секундах (0 — никогда).</summary>
        public int? GetSleepTimeout()
        {
            return ReadValue(SubGroupSleep, StandbyIdle) is uint value ? (int)value : null;
        }

        public bool SetSleepTimeout(int seconds)
        {
            return WriteValue(SubGroupSleep, StandbyIdle, (uint)Math.Max(seconds, 0));
        }

        public bool? GetAdaptiveBrightness()
        {
            return ReadValue(SubGroupVideo, AdaptiveBrightnessSetting) is uint value ? value != 0 : null;
        }

        public bool SetAdaptiveBrightness(bool enabled)
        {
            return WriteValue(SubGroupVideo, AdaptiveBrightnessSetting, enabled ? 1u : 0u);
        }

        /// <summary>
        /// Режим питания Windows 11, либо <c>null</c>, если он неизвестен
        /// (например, активна не сбалансированная схема).
        /// </summary>
        public PowerMode? GetPowerMode()
        {
            if (PowerGetActualOverlayScheme(out var overlay) != ERROR_SUCCESS)
                return null;

            if (overlay == OverlayBestEfficiency)
                return PowerMode.BestEfficiency;
            if (overlay == OverlayBestPerformance)
                return PowerMode.BestPerformance;
            if (overlay == OverlayBalanced)
                return PowerMode.Balanced;

            return null;
        }

        /// <summary>
        /// Задаёт режим питания для текущего источника (сеть или батарея — у Windows они раздельные).
        /// Режим действует только при сбалансированной схеме.
        /// </summary>
        public bool SetPowerMode(PowerMode mode)
        {
            var overlay = mode switch
            {
                PowerMode.BestEfficiency => OverlayBestEfficiency,
                PowerMode.BestPerformance => OverlayBestPerformance,
                _ => OverlayBalanced,
            };

            return PowerSetActiveOverlayScheme(overlay) == ERROR_SUCCESS;
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

        /// <summary>Значение настройки активной схемы при питании от сети.</summary>
        private uint? ReadValue(Guid subGroup, Guid setting)
        {
            var schemeId = GetActiveSchemeId();
            if (schemeId is null)
                return null;

            var scheme = schemeId.Value;

            uint status = PowerReadACValueIndex(
                IntPtr.Zero, ref scheme, ref subGroup, ref setting, out uint value);

            return status == ERROR_SUCCESS ? value : null;
        }

        /// <summary>Записывает значение для сети и батареи и сразу применяет его.</summary>
        private bool WriteValue(Guid subGroup, Guid setting, uint value)
        {
            var schemeId = GetActiveSchemeId();
            if (schemeId is null)
                return false;

            var scheme = schemeId.Value;

            uint acStatus = PowerWriteACValueIndex(
                IntPtr.Zero, ref scheme, ref subGroup, ref setting, value);
            uint dcStatus = PowerWriteDCValueIndex(
                IntPtr.Zero, ref scheme, ref subGroup, ref setting, value);

            if (acStatus != ERROR_SUCCESS || dcStatus != ERROR_SUCCESS)
                return false;

            // Re-applying the active scheme makes the new value take effect immediately.
            return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == ERROR_SUCCESS;
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
