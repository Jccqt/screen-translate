using System.Security.Cryptography;
using System.Text;

namespace screen_translate.Models;

public sealed class ModelInUseException() : IOException("Models in this folder are in use by an active operation. Wait for it to finish, then retry.");

/// <summary>Cross-process exclusion, held until the actual worker (including native loading) finishes.</summary>
public static class ModelUse
{
    private static readonly Dictionary<string, SharedRead> Readers = new(StringComparer.OrdinalIgnoreCase);
    private sealed class SharedRead(IDisposable lease)
    {
        public readonly IDisposable Lease = lease;
        public int Count = 1;
    }

    public static IDisposable AcquireRead(string directory)
    {
        string key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        lock (Readers)
        {
            if (Readers.TryGetValue(key, out var read)) read.Count++;
            else Readers.Add(key, new SharedRead(Acquire(directory)));
        }
        return new ReadLease(key);
    }

    private sealed class ReadLease(string key) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            lock (Readers)
            {
                if (_disposed) return;
                _disposed = true;
                var read = Readers[key];
                if (--read.Count == 0) { Readers.Remove(key); read.Lease.Dispose(); }
            }
        }
    }

    public static IDisposable Acquire(string directory)
    {
        string key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant();
        var semaphore = new Semaphore(1, 1, "Local\\ScreenTranslate.Models." +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
        if (!semaphore.WaitOne(0))
        {
            semaphore.Dispose();
            throw new ModelInUseException();
        }
        return new Lease(semaphore);
    }

    public static async Task RunAsync(string directory, Func<Task> work)
    {
        using var lease = Acquire(directory);
        await work().ConfigureAwait(false);
    }

    private sealed class Lease(Semaphore semaphore) : IDisposable
    {
        private Semaphore? _semaphore = semaphore;
        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref _semaphore, null);
            if (owned is null) return;
            owned.Release();
            owned.Dispose();
        }
    }
}
