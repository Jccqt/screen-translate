namespace screen_translate.Interface;

/// <summary>Owns cancellable work and resources that must not outlive the main window.</summary>
public sealed class ApplicationLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<IDisposable> _resources = [];
    public CancellationToken Token { get; }
    public bool IsStopped { get; private set; }

    public ApplicationLifetime() => Token = _cancellation.Token;

    public void Own(IDisposable resource)
    {
        if (IsStopped) resource.Dispose();
        else _resources.Add(resource);
    }

    public void Dispose()
    {
        if (IsStopped) return;
        IsStopped = true;
        // Even a faulty cancellation callback or resource must not prevent the rest of shutdown.
        try { _cancellation.Cancel(); }
        catch (AggregateException) { }
        foreach (var resource in _resources.AsEnumerable().Reverse())
        {
            try { resource.Dispose(); }
            catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
        }
        _resources.Clear();
        _cancellation.Dispose();
    }
}
