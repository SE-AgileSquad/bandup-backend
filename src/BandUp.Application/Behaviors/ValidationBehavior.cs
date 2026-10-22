using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using FluentValidation;
using FluentValidation.Results;

namespace BandUp.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResponse> next)
    {
        IValidator<TRequest>[] applicableValidators = validators.ToArray();

        if (applicableValidators.Length == 0)
        {
            return await next();
        }

        ValidationContext<TRequest> context = new(request);
        ValidationResult[] results = await Task.WhenAll(
            applicableValidators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        List<ValidationFailure> failures = results
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return await next();
        }

        Dictionary<string, string[]> errors = failures
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        return Result<TResponse>.Failure(Error.Validation(errors));
    }
}