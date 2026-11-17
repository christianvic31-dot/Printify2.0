using System.Collections.Concurrent;

namespace Printify.Backend.Services;

// Hinaharang ang paulit-ulit na maling login (anti brute-force).
public static class LoginThrottle
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockTime = TimeSpan.FromMinutes(15);

    private class Entry
    {
        public int Count;
        public DateTime LockedUntil = DateTime.MinValue;
    }

    private static readonly ConcurrentDictionary<string, Entry> Entries = new();

    public static bool IsLocked(string key)
    {
        if (!Entries.TryGetValue(key, out var entry)) return false;

        if (entry.LockedUntil > DateTime.UtcNow) return true;

        if (entry.LockedUntil != DateTime.MinValue) Entries.TryRemove(key, out _);

        return false;
    }

    public static void RecordFailure(string key)
    {
        var entry = Entries.GetOrAdd(key, _ => new Entry());

        lock (entry)
        {
            entry.Count++;
            if (entry.Count >= MaxAttempts) entry.LockedUntil = DateTime.UtcNow.Add(LockTime);
        }
    }

    public static void Reset(string key) => Entries.TryRemove(key, out _);
}
