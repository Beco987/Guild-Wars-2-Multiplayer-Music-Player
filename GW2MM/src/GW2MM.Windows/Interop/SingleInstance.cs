namespace GW2MM.Windows.Interop;

/// <summary>
/// "#SingleInstance Force": starting a second copy of the same role (per data folder) closes the running one.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _closeRequest;
    private readonly RegisteredWaitHandle _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle closeRequest, Action onCloseRequested)
    {
        _mutex = mutex;
        _closeRequest = closeRequest;
        _wait = ThreadPool.RegisterWaitForSingleObject(closeRequest, (_, _) => onCloseRequested(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    public static SingleInstance Acquire(string key, Action onCloseRequested)
    {
        var name = "GW2MM." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key.ToUpperInvariant())));
        var mutex = new Mutex(false, @"Local\" + name + ".lock");
        var closeRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".close");
        bool owned;
        try { owned = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            closeRequest.Set();
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new InvalidOperationException("The running instance did not close.");
        }
        closeRequest.Reset();
        return new SingleInstance(mutex, closeRequest, onCloseRequested);
    }

    public void Dispose()
    {
        _wait.Unregister(null);
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
        _closeRequest.Dispose();
    }
}
