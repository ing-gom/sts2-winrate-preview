using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sts2WinratePreview;

/// <summary>
/// OS-level lifetime tether for the headless helper processes (Windows).
///
/// Every helper is assigned to a single Job Object created with
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE. The job handle is held (and never closed)
/// for as long as the game process lives, so the instant the game process goes
/// away — clean quit, crash, Steam force-close, taskkill — the kernel closes the
/// handle and terminates every helper in the job.
///
/// Why this is needed on top of the existing teardown paths:
///   * <c>AppDomain.ProcessExit</c> (MainFile) is managed code. Under Godot's
///     native host it is not reliably raised, gets a ~2s budget, and never runs
///     at all on a crash / force-close.
///   * The helper's stdin-EOF fallback (its ReadLine loop ends when the game
///     closes the pipe) only fires when the helper is actually *blocked on
///     ReadLine*. A helper stuck inside a query — the engine's known synchronous
///     combat loop — never returns to ReadLine, never sees EOF, and would
///     otherwise survive the game forever burning a core.
///
/// The job is the only mechanism that covers that last case from this side; the
/// helper's own watchdog (--parent-pid / hang timeout) covers it from the other.
/// </summary>
internal static class HelperJob
{
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    private static IntPtr _job = IntPtr.Zero;   // deliberately never closed
    private static bool _tried;
    private static readonly object _lock = new();

    /// <summary>
    /// Put <paramref name="proc"/> under the kill-on-close job. Best-effort: on
    /// any failure (non-Windows, job APIs denied) it logs once and returns false,
    /// leaving the existing ProcessExit + stdin-EOF paths as the only teardown.
    /// </summary>
    public static bool TryAssign(Process proc)
    {
        try
        {
            IntPtr job = EnsureJob();
            if (job == IntPtr.Zero) return false;

            if (AssignProcessToJobObject(job, proc.Handle)) return true;

            MainFile.Logger.Warn($"[{MainFile.ModId}] AssignProcessToJobObject failed " +
                                 $"(win32={Marshal.GetLastWin32Error()}); helper pid {proc.Id} not job-tethered.");
            return false;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] job tether unavailable: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static IntPtr EnsureJob()
    {
        lock (_lock)
        {
            if (_tried) return _job;
            _tried = true;

            if (!OperatingSystem.IsWindows())
                return IntPtr.Zero;   // job objects are a Windows concept

            IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] CreateJobObject failed (win32={Marshal.GetLastWin32Error()}).");
                return IntPtr.Zero;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, buf, fDeleteOld: false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buf, (uint)size))
                {
                    MainFile.Logger.Warn($"[{MainFile.ModId}] SetInformationJobObject failed (win32={Marshal.GetLastWin32Error()}).");
                    CloseHandle(job);
                    return IntPtr.Zero;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }

            _job = job;
            MainFile.Logger.Info($"[{MainFile.ModId}] helper job created (kill-on-close) — helpers die with the game.");
            return _job;
        }
    }

    // ---- Win32 ----

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    // Layout must match the Win32 headers exactly (natural alignment, SIZE_T = nuint).
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
