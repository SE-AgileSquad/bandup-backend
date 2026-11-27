using BandUp.Application.Features.Identity;
using StackExchange.Redis;

namespace BandUp.Infrastructure.Security;

public sealed class RedisPasswordResetRateLimiter(IConnectionMultiplexer redis, PasswordResetRateLimitKeys keys)
    : IPasswordResetRateLimiter
{
    // All keys share a hash tag. Redis TIME defines fixed UTC windows consistently across API instances.
    private const string Script = """
        local now = tonumber(redis.call('TIME')[1])
        local window = tonumber(ARGV[1])
        local bucket = math.floor(now / window)
        local function acquire(key, limit)
            local stored = tonumber(redis.call('HGET', key, 'window'))
            local count = 0
            if stored == bucket then count = tonumber(redis.call('HGET', key, 'count')) or 0 end
            if count >= limit then return false end
            redis.call('HSET', key, 'window', bucket, 'count', count + 1)
            redis.call('EXPIRE', key, (bucket + 1) * window - now + 1)
            return true
        end
        if not acquire(KEYS[1], tonumber(ARGV[2])) then return 2 end
        if tonumber(ARGV[4]) > 0 and redis.call('EXISTS', KEYS[3]) == 1 then return 1 end
        if not acquire(KEYS[2], tonumber(ARGV[3])) then return 1 end
        if tonumber(ARGV[4]) > 0 then redis.call('SET', KEYS[3], '1', 'PX', ARGV[4]) end
        return 0
        """;

    public Task<PasswordResetRateLimitDecision> AcquireRequestAsync(string email, string? clientIp, CancellationToken cancellationToken) =>
        AcquireAsync("request", email, clientIp, PasswordResetPolicy.RequestWindow,
            PasswordResetPolicy.MaxRequestsPerIp, PasswordResetPolicy.MaxRequestsPerEmail, PasswordResetPolicy.ResendCooldown, cancellationToken);

    public Task<PasswordResetRateLimitDecision> AcquireVerificationAsync(string email, string? clientIp, CancellationToken cancellationToken) =>
        AcquireAsync("verify", email, clientIp, PasswordResetPolicy.VerificationWindow,
            PasswordResetPolicy.MaxVerificationAttemptsPerIp, PasswordResetPolicy.MaxVerificationAttemptsPerEmail, TimeSpan.Zero, cancellationToken);

    public Task<PasswordResetRateLimitDecision> AcquireResetAsync(string tokenHash, string? clientIp, CancellationToken cancellationToken) =>
        AcquireAsync("reset", tokenHash, clientIp, PasswordResetPolicy.ResetWindow,
            PasswordResetPolicy.MaxResetAttemptsPerIp, PasswordResetPolicy.MaxResetAttemptsPerToken, TimeSpan.Zero, cancellationToken);

    private async Task<PasswordResetRateLimitDecision> AcquireAsync(
        string operation, string email, string? clientIp, TimeSpan window, int ipLimit, int emailLimit,
        TimeSpan cooldown, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string prefix = $"bandup:{{password-reset}}:{operation}:";
        string subject = operation == "reset" ? keys.ResetToken(email) : keys.Email(email);
        string subjectType = operation == "reset" ? "token:" : "email:";
        RedisKey[] redisKeys = [prefix + "ip:" + keys.ClientIp(clientIp), prefix + subjectType + subject, prefix + "cooldown:" + subject];
        RedisValue[] arguments = [(long)window.TotalSeconds, ipLimit, emailLimit, (long)cooldown.TotalMilliseconds];
        RedisResult result = await redis.GetDatabase().ScriptEvaluateAsync(Script, redisKeys, arguments).WaitAsync(cancellationToken);
        return (int)result switch
        {
            0 => PasswordResetRateLimitDecision.Allowed,
            1 => PasswordResetRateLimitDecision.AccountSuppressed,
            2 => PasswordResetRateLimitDecision.ClientRateLimited,
            _ => throw new InvalidOperationException("Unexpected recovery quota response."),
        };
    }
}
