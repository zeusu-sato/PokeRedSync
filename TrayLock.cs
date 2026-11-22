namespace PokeRedSync;

public sealed class TrayLock : IDisposable
{
    private readonly Mutex mutex;
    private bool owned;
    public TrayLock(string name) => mutex = new Mutex(false, name);
    public bool TryAcquire(TimeSpan timeout)
    {
        try { owned = mutex.WaitOne(timeout); }
        catch (AbandonedMutexException) { owned = true; }
        return owned;
    }
    public void Dispose()
    {
        if (owned) { mutex.ReleaseMutex(); owned = false; }
        mutex.Dispose();
    }
}
