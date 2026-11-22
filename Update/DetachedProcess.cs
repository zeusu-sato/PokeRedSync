using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PokeRedSync.Update;

public sealed class DetachedProcess : IDisposable
{
    private readonly SafeWaitHandle handle;
    public bool BrokeAway { get; }
    public bool? IsInJob => IsProcessInJob(handle, IntPtr.Zero, out bool inJob) ? inJob : null;
    private DetachedProcess(IntPtr process, bool brokeAway) { handle = new SafeWaitHandle(process, true); BrokeAway = brokeAway; }
    public static DetachedProcess Start(string executable, string arguments, string folder)
    {
        const uint breakaway = 0x01000000, flags = 0x00000200 | 0x00000400;
        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        string command = $"\"{executable}\" {arguments}";
        bool separated = true;
        if (!CreateProcess(executable, new System.Text.StringBuilder(command), IntPtr.Zero, IntPtr.Zero, false, flags | breakaway, IntPtr.Zero, folder, ref startup, out var info))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 5) throw new Win32Exception(error);
            separated = false;
            if (!CreateProcess(executable, new System.Text.StringBuilder(command), IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, folder, ref startup, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        CloseHandle(info.thread);
        return new DetachedProcess(info.process, separated);
    }
    public bool Wait(TimeSpan timeout) => WaitForSingleObject(handle, (uint)Math.Clamp((long)timeout.TotalMilliseconds, 0, uint.MaxValue - 1)) == 0;
    public int ExitCode { get { if (!GetExitCodeProcess(handle, out uint code)) throw new Win32Exception(Marshal.GetLastWin32Error()); return unchecked((int)code); } }
    public void Stop() { if (!Wait(TimeSpan.Zero)) { if (!TerminateProcess(handle, 1) && !Wait(TimeSpan.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error()); WaitForSingleObject(handle, uint.MaxValue); } }
    public void Dispose() => handle.Dispose();
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo { public int cb; public string? reserved, desktop, title; public int x, y, xSize, ySize, xCountChars, yCountChars, fillAttribute, flags; public short showWindow, reserved2; public IntPtr reservedPtr, stdInput, stdOutput, stdError; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr process, thread; public uint processId, threadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateProcessW")] private static extern bool CreateProcess(string app, System.Text.StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeWaitHandle handle, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeWaitHandle handle, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessInJob(SafeWaitHandle handle, IntPtr job, out bool result);
}
