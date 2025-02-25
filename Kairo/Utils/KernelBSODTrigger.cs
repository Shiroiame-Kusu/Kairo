using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Utils
{
    internal class KernelBSODTrigger
    {
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 0x00000001;
        const uint FILE_SHARE_WRITE = 0x00000002;
        const uint OPEN_EXISTING = 3;
        private const uint IOCTL_TRIGGER_BSOD = 0x800;
        private static List<uint> bsodErrorCodes = new List<uint>
        {
            /**/
            //0xC000021A, //SYSTEM_SERVICE_EXCEPTION
            0x000000D1, //DRIVER_IRQL_NOT_LESS_OR_EQUAL
            0x0000000A,
            0x0000005A, //CRITICAL_SERVICE_FAILED
            0x00000077, //KERNEL_STACK_INPAGE_ERROR
            0x0000007A, //KERNEL_DATA_INPAGE_ERROR
            0x0000007E, //SYSTEM_THREAD_EXCEPTION_NOT_HANDLED
            0x0000003B, //SYSTEM_SERVICE_EXCEPTION
            0x00000050, //PAGE_FAULT_IN_NONPAGED_AREA
            0x00000133, //DPC_WATCHDOG_VIOLATION
            0x0000001E, //KMODE_EXCEPTION_NOT_HANDLED
            0x000000EF,  //CRITICAL_PROCESS_DIED
            0x00000000  // MANUALLY_INITIATED_CRASH
        };
        [StructLayout(LayoutKind.Sequential)]
        private struct BSODRequest
        {
            public uint BugCheckCode;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            ref BSODRequest lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        public static bool Trigger()
        {
            // Example list of bug check codes
            
            uint selectedCode = bsodErrorCodes[Random.Shared.Next(bsodErrorCodes.Count)];

            IntPtr hDevice = CreateFile(@"\\.\BSODDriver", GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (hDevice == IntPtr.Zero || hDevice == new IntPtr(-1))
            {
                Console.WriteLine("Failed to open handle to the device.");
                return false;
            }

            BSODRequest request = new BSODRequest { BugCheckCode = selectedCode };
            uint bytesReturned;
            if (!DeviceIoControl(hDevice, IOCTL_TRIGGER_BSOD, ref request, (uint)Marshal.SizeOf(request), IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                Console.WriteLine($"Failed to communicate with the driver.{error}");
                return false;
            }

            CloseHandle(hDevice);
            return true;
        }
    }
}
