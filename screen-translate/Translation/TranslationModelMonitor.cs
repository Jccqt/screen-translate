namespace screen_translate.Translation;

/// <summary>Observes package changes, including creation/replacement of the configured root.</summary>
public sealed class TranslationModelMonitor : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private string? _directory;
    private string? _parent;
    private bool _rootExists;
    private long _revision;
    private int _failed;
    private DateTime _retryAfter;
    public long Revision => Interlocked.Read(ref _revision);

    // Called on the owning UI thread. A failed watcher is retried by the UI timer;
    // periodic full scans also recover from lost filesystem notifications.
    public void Watch(string directory)
    {
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string? parent = Directory.GetParent(root)?.FullName ?? root;
            while (parent is not null && !Directory.Exists(parent)) parent = Directory.GetParent(parent)?.FullName;
            bool exists = Directory.Exists(root);
            if (_directory == root && _parent == parent && _rootExists == exists &&
                (_watchers.Count > 0 || DateTime.UtcNow < _retryAfter) &&
                Volatile.Read(ref _failed) == 0) return;
            ClearWatchers();
            _directory = root;
            _parent = parent;
            _rootExists = exists;
            Volatile.Write(ref _failed, 0);
            _retryAfter = DateTime.UtcNow.AddSeconds(5);
            // Reattaching may have missed a write, or followed creation of a missing ancestor.
            Interlocked.Increment(ref _revision);
            bool Relevant(string path) => string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                root.StartsWith(Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            void Changed(object sender, FileSystemEventArgs e)
            {
                if (Relevant(e.FullPath)) Interlocked.Increment(ref _revision);
            }
            void AddWatcher(string path, bool recursive)
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = recursive,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _watchers.Add(watcher);
                watcher.Changed += Changed;
                watcher.Created += Changed;
                watcher.Deleted += Changed;
                watcher.Renamed += (_, e) =>
                {
                    if (Relevant(e.FullPath) || Relevant(e.OldFullPath)) Interlocked.Increment(ref _revision);
                };
                watcher.Error += (_, _) =>
                {
                    Volatile.Write(ref _failed, 1);
                    Interlocked.Increment(ref _revision);
                };
                watcher.EnableRaisingEvents = true;
            }
            // Watch only packages recursively. Observing the parent nonrecursively detects root
            // replacement without subscribing to every unrelated file under a profile or drive.
            if (parent is not null && parent != root) AddWatcher(parent, recursive: false);
            if (exists) AddWatcher(root, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ClearWatchers();
            // The catalog reports folder errors; observation must not crash configuration.
        }
    }

    private void ClearWatchers()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }

    public void Dispose() => ClearWatchers();
}
