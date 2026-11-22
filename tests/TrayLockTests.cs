using PokeRedSync;
using PokeRedSync.Update;
using Xunit;

public class TrayLockTests
{
    private static string Name() => @"Local\PokeRedSync.Test." + Guid.NewGuid();
    [Fact] public void ExistingUnownedHandleCanBeAcquired() { string name = Name(); using var other = new Mutex(false, name); using var tray = new TrayLock(name); Assert.True(tray.TryAcquire(TimeSpan.Zero)); }
    [Fact] public void OwnedMutexCannotBeAcquiredImmediately() { string name = Name(); using var ready = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var thread = new Thread(() => { using var m = new Mutex(false, name); m.WaitOne(); ready.Set(); release.Wait(); m.ReleaseMutex(); }); thread.Start(); ready.Wait(); try { using var tray = new TrayLock(name); Assert.False(tray.TryAcquire(TimeSpan.Zero)); } finally { release.Set(); thread.Join(); } }
    [Fact] public void AcquiresAfterRelease() { string name = Name(); using var ready = new ManualResetEventSlim(); var thread = new Thread(() => { using var m = new Mutex(false, name); m.WaitOne(); ready.Set(); Thread.Sleep(150); m.ReleaseMutex(); }); thread.Start(); ready.Wait(); try { using var tray = new TrayLock(name); Assert.True(tray.TryAcquire(TimeSpan.FromSeconds(5))); } finally { thread.Join(); } }
    [Fact] public void AcquiresAbandonedMutex() { string name = Name(); using var keep = new Mutex(false, name); var thread = new Thread(() => { using var m = new Mutex(false, name); m.WaitOne(); }); thread.Start(); thread.Join(); using var tray = new TrayLock(name); Assert.True(tray.TryAcquire(TimeSpan.Zero)); }
}
public class DetachedProcessTests
{
    [Fact] public void ReturnsExitCode() { using var p = DetachedProcess.Start(Environment.GetEnvironmentVariable("ComSpec")!, "/c exit 3", Path.GetTempPath()); Assert.True(p.Wait(TimeSpan.FromSeconds(10))); Assert.Equal(3, p.ExitCode); }
    [Fact] public void StopsProcess() { using var p = DetachedProcess.Start(Environment.GetEnvironmentVariable("ComSpec")!, "/c ping -n 30 127.0.0.1 > nul", Path.GetTempPath()); p.Stop(); Assert.True(p.Wait(TimeSpan.FromSeconds(5))); }
}
