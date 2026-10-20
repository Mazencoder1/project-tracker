using Microsoft.Extensions.Caching.Memory;

namespace ProjectTracker.Web.Services;

/// <summary>
/// Limits failed password sign-ins in memory (no database columns): per account e-mail and per client IP.
/// Only failures count, so many employees behind one address are not blocked by each other's successful sign-ins.
/// </summary>
public sealed class LoginThrottle(IMemoryCache cache, TimeProvider clock)
{
    public const int MaxFailuresPerAccount = 5;
    public const int MaxFailuresPerIp = 30;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private sealed class Counter
    {
        public int Failures;
        public DateTimeOffset ResetAt;
    }

    private static string AccountKey(string email) => "login-fail:acct:" + email.Trim().ToLowerInvariant();
    private static string IpKey(string? ip) => "login-fail:ip:" + (ip ?? "unknown");

    /// <summary>Minutes left on the block, or null when the attempt may proceed.</summary>
    public int? BlockedMinutes(string email, string? ip)
    {
        var now = clock.GetUtcNow();
        foreach (var (key, max) in new[] { (AccountKey(email), MaxFailuresPerAccount), (IpKey(ip), MaxFailuresPerIp) })
        {
            if (cache.TryGetValue(key, out Counter? c) && c is not null && c.ResetAt > now && Volatile.Read(ref c.Failures) >= max)
                return Math.Max(1, (int)Math.Ceiling((c.ResetAt - now).TotalMinutes));
        }
        return null;
    }

    public void RecordFailure(string email, string? ip)
    {
        Increment(AccountKey(email));
        Increment(IpKey(ip));
    }

    /// <summary>A successful sign-in clears the account's counter (the IP counter keeps running).</summary>
    public void RecordSuccess(string email) => cache.Remove(AccountKey(email));

    private void Increment(string key)
    {
        var now = clock.GetUtcNow();
        var counter = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpiration = now + Window;
            return new Counter { ResetAt = now + Window };
        })!;
        Interlocked.Increment(ref counter.Failures);
    }
}
