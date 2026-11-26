using BandUp.Application.Features.Identity;
using StackExchange.Redis;

namespace BandUp.Infrastructure.Security;

public sealed class RedisOtpLoginRateLimiter(IConnectionMultiplexer redis, OtpLoginRateLimitKeys keys)
    : IOtpLoginRateLimiter
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

    public Task<OtpLoginRateLimitDecision> AcquireRequestAsync(string email, string? clientIp, CancellationToken cancellationToken) =>
        AcquireAsync("request", email, clientIp, OtpLoginPolicy.RequestWindow,
            OtpLoginPolicy.MaxRequestsPerIp, OtpLoginPolicy.MaxRequestsPerEmail, OtpLoginPolicy.ResendCooldown, cancellationToken);

    public Task<OtpLoginRateLimitDecision> AcquireVerificationAsync(string email, string? clientIp, CancellationToken cancellationToken) =>
        AcquireAsync("verify", email, clientIp, OtpLoginPolicy.VerificationWindow,
            OtpLoginPolicy.MaxVerificationAttemptsPerIp, OtpLoginPolicy.MaxVerificationAttemptsPerEmail, TimeSpan.Zero, cancellationToken);

    private async Task<OtpLoginRateLimitDecision> AcquireAsync(
        string operation, string email, string? clientIp, TimeSpan window, int ipLimit, int emailLimit,
        TimeSpan cooldown, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string prefix = $"bandup:{{otp-login}}:{operation}:";
        RedisKey[] redisKeys = [prefix + "ip:" + keys.ClientIp(clientIp), prefix + "email:" + keys.Email(email), prefix + "cooldown:" + keys.Email(email)];
        RedisValue[] arguments = [(long)window.TotalSeconds, ipLimit, emailLimit, (long)cooldown.TotalMilliseconds];
        RedisResult result = await redis.GetDatabase().ScriptEvaluateAsync(Script, redisKeys, arguments).WaitAsync(cancellationToken);
        return (int)result switch
        {
            0 => OtpLoginRateLimitDecision.Allowed,
            1 => OtpLoginRateLimitDecision.AccountSuppressed,
            2 => OtpLoginRateLimitDecision.ClientRateLimited,
            _ => throw new InvalidOperationException("Unexpected OTP sign-in quota response."),
        };
    }
}
