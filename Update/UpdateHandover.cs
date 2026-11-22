using System.Diagnostics;
using Microsoft.Win32;

namespace PokeRedSync.Update;

public interface IUpdateHandoverPlatform
{
    void ResetReady();
    void SetUpdatedFrom(string tag);
    void ClearUpdatedFrom();
    void SetFailedTag(string tag);
    object StartTray(string folder);
    bool WaitReady(object process, TimeSpan timeout);
    void Stop(object process);
    void Restore(string folder);
}

public sealed class UpdateHandover(IUpdateHandoverPlatform? platform = null)
{
    private readonly IUpdateHandoverPlatform platform = platform ?? new WindowsUpdateHandoverPlatform();
    private const string RegistryPath = @"Software\PokeRedSync";
    private const string ReadyName = @"Local\PokeRedSync.Tray.Ready";

    // 呼び出し側は Mutex を解放してから呼び出す。
    public bool Run(string folder, StagedUpdate staged, string oldTag, Action<string> log)
    {
        object? process = null;
        try
        {
            platform.ResetReady();
            platform.SetUpdatedFrom(oldTag);
            process = platform.StartTray(folder);
            if (platform.WaitReady(process, TimeSpan.FromSeconds(60)))
            {
                log($"更新の入れ替え: 成功 {oldTag} → {staged.Tag}");
                UpdateScheduler.Cleanup(staged);
                return true;
            }
        }
        catch (Exception ex) { log($"更新の入れ替え: {ex.Message}"); }
        if (process is not null)
        {
            try { platform.Stop(process); }
            catch (Exception ex) { log($"更新プロセスの終了: {ex.Message}"); }
        }
        try { platform.ClearUpdatedFrom(); }
        catch (Exception ex) { log($"更新の記録の削除: {ex.Message}"); }
        try { platform.Restore(folder); }
        catch (Exception ex) { log($"更新の復元: 失敗 {ex.Message}"); }
        try { platform.SetFailedTag(staged.Tag); }
        catch (Exception ex) { log($"更新の失敗の記録: {ex.Message}"); }
        try { platform.StartTray(folder); }
        catch (Exception ex) { log($"元の版の起動: 失敗 {ex.Message}"); }
        log("更新の入れ替え: 失敗、元の版に戻しました");
        return false;
    }

    public static void SignalReady()
    {
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyName);
        ready.Set();
    }

    public static string? ReadUpdatedFrom()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return key?.GetValue("UpdatedFrom") as string;
    }

    public static void ClearUpdatedFrom()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
        key?.DeleteValue("UpdatedFrom", false);
    }

    private sealed class WindowsUpdateHandoverPlatform : IUpdateHandoverPlatform
    {
        private EventWaitHandle? ready;
        public void ResetReady()
        {
            ready?.Dispose();
            ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyName);
            ready.Reset();
        }
        public void SetUpdatedFrom(string tag) => SetValue("UpdatedFrom", tag);
        public void ClearUpdatedFrom() => UpdateHandover.ClearUpdatedFrom();
        public void SetFailedTag(string tag) => SetValue("UpdateFailedTag", tag);
        private static void SetValue(string name, string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue(name, value, RegistryValueKind.String);
        }
        public object StartTray(string folder)
        {
            var info = new ProcessStartInfo(Path.Combine(folder, "PokeRedSync.exe")) { UseShellExecute = false };
            return Process.Start(info) ?? throw new InvalidOperationException("Process start failed");
        }
        public bool WaitReady(object process, TimeSpan timeout)
        {
            var p = (Process)process;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < timeout)
            {
                if (ready!.WaitOne(TimeSpan.FromMilliseconds(100))) return true;
                if (p.HasExited) return false;
            }
            return ready!.WaitOne(0);
        }
        public void Stop(object process)
        {
            using var p = (Process)process;
            if (!p.HasExited) { p.Kill(); p.WaitForExit(); }
        }
        public void Restore(string folder) => UpdateInstaller.Restore(folder);
    }
}
