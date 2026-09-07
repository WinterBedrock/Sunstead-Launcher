using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SunsteadLauncher
{
    internal static class Injector
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Process32FirstW(IntPtr h, ref PROCESSENTRY32W pe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Process32NextW(IntPtr h, ref PROCESSENTRY32W pe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr proc, IntPtr addr, IntPtr size,
            uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr proc, IntPtr baseAddr, byte[] buf,
            IntPtr size, out IntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr proc, IntPtr attr, IntPtr stack,
            IntPtr start, IntPtr param, uint flags, out uint tid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr h, uint ms);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeThread(IntPtr h, out uint code);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr proc, IntPtr addr, IntPtr size, uint type);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MODULEENTRY32W
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public IntPtr modBaseAddr;
            public uint modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExePath;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Module32FirstW(IntPtr h, ref MODULEENTRY32W me);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Module32NextW(IntPtr h, ref MODULEENTRY32W me);

        private const uint TH32CS_SNAPMODULE = 0x8;
        private const uint TH32CS_SNAPMODULE32 = 0x10;

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValueW(string systemName, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
            ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previous, IntPtr returnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
        private const uint TOKEN_QUERY = 0x8;
        private const uint SE_PRIVILEGE_ENABLED = 0x2;

        /// enables SeDebugPrivilege in the launcher's own token
        public static bool EnableDebugPrivilege()
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
                return false;

            try
            {
                if (!LookupPrivilegeValueW(null, "SeDebugPrivilege", out LUID luid))
                    return false;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };
                return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { CloseHandle(token); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string name);

        private const uint TH32CS_SNAPPROCESS = 0x2;
        private const uint PROCESS_CREATE_THREAD = 0x0002;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const uint THREAD_ALL_ACCESS_NT = 0x1FFFFF;
        private const uint WAIT_OBJECT_0 = 0x0;
        private const uint WAIT_TIMEOUT = 0x102;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint NtCreateThreadExDelegate(
            out IntPtr threadHandle,
            uint desiredAccess,
            IntPtr objectAttributes,
            IntPtr processHandle,
            IntPtr startRoutine,
            IntPtr argument,
            uint createFlags,
            IntPtr zeroBits,
            IntPtr stackSize,
            IntPtr maxStackSize,
            IntPtr attributeList);

        /// Creates a remote thread via ntdll!NtCreateThreadEx
        private static uint NtCreateThreadEx(out IntPtr thread, IntPtr process,
            IntPtr startRoutine, IntPtr argument)
        {
            thread = IntPtr.Zero;
            IntPtr ntdll = GetModuleHandleW("ntdll.dll");
            if (ntdll == IntPtr.Zero) return 0xC0000001; // STATUS_UNSUCCESSFUL

            IntPtr fn = GetProcAddress(ntdll, "NtCreateThreadEx");
            if (fn == IntPtr.Zero) return 0xC0000001;

            var create = Marshal.GetDelegateForFunctionPointer<NtCreateThreadExDelegate>(fn);
            return create(out thread, THREAD_ALL_ACCESS_NT, IntPtr.Zero, process,
                          startRoutine, argument, 0 /* run immediately */,
                          IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        public const string GameProcessName = "Minecraft.Windows.exe";
        private const string GamePackageFamily = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";

        public const string ALREADY_INJECTED = "__ALREADY_INJECTED__";

        public static bool IsGameInstalled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                {
                    if (key == null) return false;
                    foreach (var name in key.GetSubKeyNames())
                        if (name.StartsWith("Microsoft.MinecraftUWP", StringComparison.OrdinalIgnoreCase))
                            return true;
                }
            }
            catch { }
            return false;
        }

        public static bool TryLaunchGame()
        {
            // trying aliases
            foreach (string alias in new[]
            {
                Environment.ExpandEnvironmentVariables(
                    @"%LOCALAPPDATA%\Microsoft\WindowsApps\Minecraft.exe"),
                Environment.ExpandEnvironmentVariables(
                    @"%LOCALAPPDATA%\Microsoft\WindowsApps\Minecraft.Windows.exe"),
            })
            {
                if (System.IO.File.Exists(alias))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = alias,
                            UseShellExecute = true
                        });
                        return true;
                    }
                    catch { }
                }
            }

            // fallback
            try
            {
                using (var ps = new Process())
                {
                    ps.StartInfo = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "shell:AppsFolder\\" + GamePackageFamily + "!App",
                        UseShellExecute = true
                    };
                    ps.Start();
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static List<uint> FindGamePids()
        {
            var result = new List<uint>();
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return result;

            try
            {
                var pe = new PROCESSENTRY32W();
                pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W));

                if (Process32FirstW(snap, ref pe))
                {
                    do
                    {
                        if (string.Equals(pe.szExeFile, GameProcessName, StringComparison.OrdinalIgnoreCase))
                            result.Add(pe.th32ProcessID);
                    } while (Process32NextW(snap, ref pe));
                }
            }
            finally { CloseHandle(snap); }

            return result;
        }

        public static bool IsDllLoaded(uint pid, string dllName)
        {
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
            if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return false;

            try
            {
                var me = new MODULEENTRY32W();
                me.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));

                if (Module32FirstW(snap, ref me))
                {
                    do
                    {
                        if (string.Equals(me.szModule, dllName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    } while (Module32NextW(snap, ref me));
                }
            }
            catch { }
            finally { CloseHandle(snap); }

            return false;
        }

        /// LoadLibraryW remote thread injection
        public static string Inject(uint pid, string dllPath)
        {
            EnableDebugPrivilege();

            if (!Environment.Is64BitProcess)
                return "Launcher is running as a 32-bit process - cannot inject " +
                       "into the 64-bit game. Rebuild with Prefer32Bit=false.";

            string dllName = System.IO.Path.GetFileName(dllPath);
            if (IsDllLoaded(pid, dllName))
                return ALREADY_INJECTED;

            IntPtr proc = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                false, pid);
            if (proc == IntPtr.Zero)
                return "OpenProcess failed (error " + Marshal.GetLastWin32Error() +
                       ") - try running the launcher as administrator.";

            IntPtr mem = IntPtr.Zero;
            IntPtr thread = IntPtr.Zero;
            bool threadObservedExit = false;

            try
            {
                byte[] pathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
                mem = VirtualAllocEx(proc, IntPtr.Zero, (IntPtr)pathBytes.Length,
                    MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (mem == IntPtr.Zero)
                    return "VirtualAllocEx failed (error " + Marshal.GetLastWin32Error() + ").";

                if (!WriteProcessMemory(proc, mem, pathBytes, (IntPtr)pathBytes.Length, out _))
                    return "WriteProcessMemory failed (error " + Marshal.GetLastWin32Error() + ").";

                IntPtr loadLibraryW = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
                if (loadLibraryW == IntPtr.Zero)
                    return "Cannot resolve LoadLibraryW.";

                uint status = NtCreateThreadEx(out thread, proc, loadLibraryW, mem);
                if (status != 0)
                {
                    thread = CreateRemoteThread(proc, IntPtr.Zero, IntPtr.Zero,
                        loadLibraryW, mem, 0, out _);
                    if (thread == IntPtr.Zero)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err == 5)
                            return "Access denied - restart the launcher as administrator (UAC).";
                        return "CreateRemoteThread failed (error " + err + ").";
                    }
                }

                // Wait for LoadLibraryW to finish inside the game
                uint wait = WaitForSingleObject(thread, 5000);
                if (wait == WAIT_OBJECT_0)
                {
                    threadObservedExit = true;
                    if (GetExitCodeThread(thread, out uint exitCode) && exitCode == 0)
                        return "LoadLibraryW returned NULL inside the game (dll rejected or its dependencies are missing).";
                }
                // WAIT_TIMEOUT: still loading then treat as success

                return null; // success
            }
            finally
            {
                if (thread != IntPtr.Zero) CloseHandle(thread);
                // Only free the path buffer once the remote thread is done with it
                if (mem != IntPtr.Zero && threadObservedExit)
                    VirtualFreeEx(proc, mem, IntPtr.Zero, MEM_RELEASE);
                CloseHandle(proc);
            }
        }
    }
}
