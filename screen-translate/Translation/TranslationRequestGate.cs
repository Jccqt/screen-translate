namespace screen_translate.Translation;

/// <summary>Drops overlapping requests instead of cancelling or queuing them.</summary>
internal sealed class TranslationRequestGate
{
    private int _active;
    public bool IsBusy => Volatile.Read(ref _active) != 0;

    public async Task RunAsync(Func<Task> operation)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) return;
        try { await operation(); }
        finally { Volatile.Write(ref _active, 0); }
    }
}
