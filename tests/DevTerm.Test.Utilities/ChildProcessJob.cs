using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DevTerm.Test.Utilities;

/// <summary>
/// Wraps a Windows Job Object configured with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> so every
/// <see cref="Process"/> added via <see cref="Add"/> is forcibly terminated the instant this job's
/// handle closes — including implicitly, if the test host process itself crashes or is killed
/// (CI timeout, Ctrl+C, a hung test runner) without this type's <see cref="Dispose"/> ever running.
/// Windows closes every handle a process still holds when that process exits, regardless of whether
/// any .NET finally/using block got to run, so the kill-on-close semantics apply either way.
///
/// <c>ConsoleAppCliTests</c>/<c>RealHardwareCliTests</c> spawn a real <c>dotnet DevTerm.Console.dll</c>
/// child process per test (the only place in this codebase <see cref="Process.Start(ProcessStartInfo)"/>
/// is used — production code never spawns child processes, which is why this lives here rather than in
/// <c>DevTerm.Core</c>). Wrap each spawn in a <c>using var job = new ChildProcessJob(); job.Add(process);</c>
/// pair: a normal test still explicitly closes its own process (stdin close + WaitForExitAsync), but if
/// an assertion throws first, <see langword="using"/>'s stack unwind disposes the job and kills the
/// still-running child immediately rather than leaking it for the rest of the test run.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ChildProcessJob : IDisposable
{
    private readonly SafeJobObjectHandle _handle;

    public ChildProcessJob()
    {
        _handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create the child-process tracking job object.");
        }

        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };

        var length = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var infoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, infoPtr, fDeleteOld: false);
            if (!NativeMethods.SetInformationJobObject(_handle, NativeMethods.JobObjectInfoType.ExtendedLimitInformation, infoPtr, (uint)length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to configure the child-process tracking job object.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    /// <summary>
    /// Assigns <paramref name="process"/> to this job. A process that has already exited by the time
    /// this is called can no longer be assigned — that fails, but there is nothing left to track, so
    /// it's a no-op rather than an error.
    /// </summary>
    public void Add(Process process)
    {
        if (!NativeMethods.AssignProcessToJobObject(_handle, process.SafeHandle) && !process.HasExited)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to assign the child process to the tracking job object.");
        }
    }

    public void Dispose() => _handle.Dispose();

    private sealed class SafeJobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobObjectHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    private static class NativeMethods
    {
        internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeJobObjectHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeJobObjectHandle hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeJobObjectHandle hJob, SafeHandle hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        internal enum JobObjectInfoType
        {
            ExtendedLimitInformation = 9,
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
