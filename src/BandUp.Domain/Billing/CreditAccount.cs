using BandUp.Domain.Common;

namespace BandUp.Domain.Billing;

public sealed class CreditAccount : Entity
{
    public Guid UserId { get; private set; }
    public decimal Balance { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    // Required for EF Core
    private CreditAccount() { }

    private CreditAccount(Guid userId, decimal initialBalance, DateTimeOffset now)
    {
        UserId = userId;
        Balance = initialBalance;
        CreatedAt = now;
    }

    public static CreditAccount Open(Guid userId, decimal initialBalance, DateTimeOffset now)
    {
        return new CreditAccount(userId, initialBalance, now);
    }

    public void AdjustBalance(decimal amount, DateTimeOffset now)
    {
        Balance += amount;
        UpdatedAt = now;
    }
}
