using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CharacterBuilderLoader
{
    //Thanks goes to Arik Poznanski for P/Invokes and methods needed to read and write the Memory
    //For more information refer to "Minesweeper, Behind the scenes" article by Arik Poznanski at Codeproject.com
    public class ProcessMemoryReader
    {

        public ProcessMemoryReader()
        {
        }

        /// <summary>	
        /// Process from which to read		
        /// </summary>
        public uint ReadProcessID
        {
            get
            {
                return m_ReadProcess;
            }
            set
            {
                m_ReadProcess = value;
            }
        }

        private uint m_ReadProcess;

        private IntPtr m_hProcess = IntPtr.Zero;

        public void OpenProcess()
        {
            //			m_hProcess = ProcessMemoryReaderApi.OpenProcess(ProcessMemoryReaderApi.PROCESS_VM_READ, 1, (uint)m_ReadProcess.Id);
            ProcessMemoryReaderApi.ProcessAccessType access;
            access = ProcessMemoryReaderApi.ProcessAccessType.PROCESS_VM_READ
                | ProcessMemoryReaderApi.ProcessAccessType.PROCESS_VM_WRITE
                | ProcessMemoryReaderApi.ProcessAccessType.PROCESS_VM_OPERATION
                | ProcessMemoryReaderApi.ProcessAccessType.PROCESS_QUERY_INFORMATION;
            m_hProcess = ProcessMemoryReaderApi.OpenProcess((uint)access, 1, (uint)m_ReadProcess);

            //bool retVal;
            //IntPtr htok = IntPtr.Zero;
            //CharacterBuilderLoader.ProcessMemoryReader.ProcessMemoryReaderApi.TOKEN_PRIVILEGES tp;
            //retVal = ProcessMemoryReaderApi.OpenProcessToken(m_hProcess,
            //    ProcessMemoryReaderApi.TOKEN_ADJUST_PRIVILEGES | ProcessMemoryReaderApi.TOKEN_QUERY, ref htok);

            //tp.PrivilegeCount = 1;
            //tp.Luid = 0;
            //tp.Attributes = ProcessMemoryReaderApi.SE_PRIVILEGE_ENABLED;
            //retVal = ProcessMemoryReaderApi.LookupPrivilegeValue(null, "SeDebugPrivilege", ref tp.Luid);
            //retVal = ProcessMemoryReaderApi.AdjustTokenPrivileges(htok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);

        }

        public void CloseHandle()
        {
            int iRetValue;
            iRetValue = ProcessMemoryReaderApi.CloseHandle(m_hProcess);
            if (iRetValue == 0)
            {
                throw new Exception("CloseHandle failed");
            }
        }

        public byte[] ReadProcessMemory(IntPtr MemoryAddress, uint bytesToRead, out int bytesRead)
        {
            byte[] buffer = new byte[bytesToRead];

            IntPtr ptrBytesRead;
            int code = ProcessMemoryReaderApi.ReadProcessMemory(m_hProcess, MemoryAddress, buffer, bytesToRead, out ptrBytesRead);

            bytesRead = ptrBytesRead.ToInt32();

            return buffer;
        }

        public void WriteProcessMemory(IntPtr MemoryAddress, byte[] bytesToWrite, out int bytesWritten)
        {
            IntPtr ptrBytesWritten;
            uint oldProtect;
            bool res = ProcessMemoryReaderApi.VirtualProtectEx(m_hProcess,
                MemoryAddress, new UIntPtr((uint)bytesToWrite.Length),
                0x40, out oldProtect);
            int code;
            code = Marshal.GetLastWin32Error();
            ProcessMemoryReaderApi.WriteProcessMemory(m_hProcess, MemoryAddress, bytesToWrite, (uint)bytesToWrite.Length, out ptrBytesWritten);
            code = Marshal.GetLastWin32Error();
            bytesWritten = ptrBytesWritten.ToInt32();
        }



        /// <summary>
        /// ProcessMemoryReader is a class that enables direct reading a process memory
        /// </summary>
        class ProcessMemoryReaderApi
        {

            [DllImport("kernel32.dll", SetLastError=true)]
            public static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress,
               UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);


            // constants information can be found in <winnt.h>
            [Flags]
            public enum ProcessAccessType
            {
                PROCESS_TERMINATE = (0x0001),
                PROCESS_CREATE_THREAD = (0x0002),
                PROCESS_SET_SESSIONID = (0x0004),
                PROCESS_VM_OPERATION = (0x0008),
                PROCESS_VM_READ = (0x0010),
                PROCESS_VM_WRITE = (0x0020),
                PROCESS_DUP_HANDLE = (0x0040),
                PROCESS_CREATE_PROCESS = (0x0080),
                PROCESS_SET_QUOTA = (0x0100),
                PROCESS_SET_INFORMATION = (0x0200),
                PROCESS_QUERY_INFORMATION = (0x0400)
            }
            public const UInt32 SE_PRIVILEGE_ENABLED_BY_DEFAULT = 0x00000001;
            public const UInt32 SE_PRIVILEGE_ENABLED = 0x00000002;
            public const UInt32 SE_PRIVILEGE_REMOVED = 0x00000004;
            public const UInt32 SE_PRIVILEGE_USED_FOR_ACCESS = 0x80000000;
            internal const int TOKEN_QUERY = 0x00000008;
            internal const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;

            [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
            internal static extern bool OpenProcessToken(IntPtr h, int acc, ref IntPtr phtok);
            [StructLayout(LayoutKind.Sequential)]
            public struct TOKEN_PRIVILEGES
            {
                public UInt32 PrivilegeCount;
                public long Luid;
                public UInt32 Attributes;
            }
            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool LookupPrivilegeValue(string host, string name, ref long pluid);

            // Use this signature if you do not want the previous state
            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle,
               [MarshalAs(UnmanagedType.Bool)]bool DisableAllPrivileges,
               ref TOKEN_PRIVILEGES NewState,
               UInt32 Zero,
               IntPtr Null1,
               IntPtr Null2);

            // function declarations are found in the MSDN and in <winbase.h> 

            //		HANDLE OpenProcess(
            //			DWORD dwDesiredAccess,  // access flag
            //			BOOL bInheritHandle,    // handle inheritance option
            //			DWORD dwProcessId       // process identifier
            //			);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr OpenProcess(UInt32 dwDesiredAccess, Int32 bInheritHandle, UInt32 dwProcessId);

            //		BOOL CloseHandle(
            //			HANDLE hObject   // handle to object
            //			);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern Int32 CloseHandle(IntPtr hObject);

            //		BOOL ReadProcessMemory(
            //			HANDLE hProcess,              // handle to the process
            //			LPCVOID lpBaseAddress,        // base of memory area
            //			LPVOID lpBuffer,              // data buffer
            //			SIZE_T nSize,                 // number of bytes to read
            //			SIZE_T * lpNumberOfBytesRead  // number of bytes read
            //			);
            [DllImport("kernel32.dll", SetLastError=true)]
            public static extern Int32 ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [In, Out] byte[] buffer, UInt32 size, out IntPtr lpNumberOfBytesRead);

            //		BOOL WriteProcessMemory(
            //			HANDLE hProcess,                // handle to process
            //			LPVOID lpBaseAddress,           // base of memory area
            //			LPCVOID lpBuffer,               // data buffer
            //			SIZE_T nSize,                   // count of bytes to write
            //			SIZE_T * lpNumberOfBytesWritten // count of bytes written
            //			);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern Int32 WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [In, Out] byte[] buffer, UInt32 size, out IntPtr lpNumberOfBytesWritten);


        }
    }
}
