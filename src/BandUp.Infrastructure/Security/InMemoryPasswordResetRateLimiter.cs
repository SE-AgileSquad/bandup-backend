using BandUp.Application.Features.Identity;

namespace BandUp.Infrastructure.Security;

/// <summary>Process-local, atomic fake for offline development/tests; Redis supplies shared production quotas.</summary>
public sealed class InMemoryPasswordResetRateLimiter(PasswordResetRateLimitKeys keys, TimeProvider timeProvider)
    : IPasswordResetRateLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Counter> _counters = [];
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = [];

    public Task<PasswordResetRateLimitDecision> AcquireRequestAsync(string email, string? clientIp, CancellationToken cancellationToken) =>
        AcquireAsync("request", email, clientIp, PasswordResetPolicy.RequestWindow,
            PasswordResetPolicy.MaxRequestsPerIp, PasswordResetPolicy.MaxRequestsPerEmail, PasswordResetPolicy.ResendCooldown, cancellationToken);

    public Task<PasswordResetRateLimitDecision> AcquireVerificationAsync(string email, string? clientIp, CancellationToken cancellationToken) =>
        AcquireAsync("verify", email, clientIp, PasswordResetPolicy.VerificationWindow,
            PasswordResetPolicy.MaxVerificationAttemptsPerIp, PasswordResetPolicy.MaxVerificationAttemptsPerEmail, TimeSpan.Zero, cancellationToken);

    private Task<PasswordResetRateLimitDecision> AcquireAsync(
        string operation, string email, string? clientIp, TimeSpan window, int ipLimit, int emailLimit,
        TimeSpan cooldown, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            foreach (string key in _counters.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
            {
                _counters.Remove(key);
            }

            foreach (string key in _cooldowns.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            {
                _cooldowns.Remove(key);
            }

            string ipKey = operation + ":ip:" + keys.ClientIp(clientIp);
            string emailKey = operation + ":email:" + keys.Email(email);
            if (!Acquire(ipKey, ipLimit, window, now))
            {
                return Task.FromResult(PasswordResetRateLimitDecision.ClientRateLimited);
            }

            if (cooldown > TimeSpan.Zero && _cooldowns.ContainsKey(emailKey))
            {
                return Task.FromResult(PasswordResetRateLimitDecision.AccountSuppressed);
            }

            if (!Acquire(emailKey, emailLimit, window, now))
            {
                return Task.FromResult(PasswordResetRateLimitDecision.AccountSuppressed);
            }

            if (cooldown > TimeSpan.Zero)
            {
                _cooldowns[emailKey] = now.Add(cooldown);
            }

            return Task.FromResult(PasswordResetRateLimitDecision.Allowed);
        }
    }

    private bool Acquire(string key, int limit, TimeSpan window, DateTimeOffset now)
    {
        if (!_counters.TryGetValue(key, out Counter? counter))
        {
            long seconds = (long)window.TotalSeconds;
            DateTimeOffset expires = DateTimeOffset.FromUnixTimeSeconds((now.ToUnixTimeSeconds() / seconds + 1) * seconds);
            counter = new Counter(0, expires);
        }
        if (counter.Count >= limit)
        {
            return false;
        }

        _counters[key] = counter with { Count = counter.Count + 1 };
        return true;
    }

    private sealed record Counter(int Count, DateTimeOffset ExpiresAt);
}
