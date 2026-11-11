using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Billing;

namespace BandUp.Application.Features.Billing;

public sealed class CreditService(
    ICreditAccountRepository accountRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICreditService
{
    private const decimal SignupCredit = 100.00m;

    public async Task<Result> OpenSignupAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        CreditAccount account = CreditAccount.Open(userId, SignupCredit, timeProvider.GetUtcNow());

        await accountRepository.AddAsync(account, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
