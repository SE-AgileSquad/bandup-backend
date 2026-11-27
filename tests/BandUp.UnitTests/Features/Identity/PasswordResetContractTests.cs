using BandUp.Application;
using BandUp.Application.Abstractions;
using BandUp.Application.Behaviors;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.ForgotPassword;
using BandUp.Application.Features.Identity.Register;
using BandUp.Application.Features.Identity.ResetPassword;
using BandUp.Application.Features.Identity.VerifyResetCode;
using BandUp.Infrastructure.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class PasswordResetContractTests
{
    private const string Email = "recovery@example.com";
    private const string Password = "ExamplePass123!";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("missing-at.example.com")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public void AUTH_15_Both_Recovery_Requests_Reject_Invalid_Email(string? email)
    {
        new ForgotPasswordValidator().Validate(new ForgotPasswordCommand(email!)).IsValid.ShouldBeFalse();
        new VerifyResetCodeValidator().Validate(new VerifyResetCodeCommand(email!, "012345")).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(320, true)]
    [InlineData(321, false)]
    public void AUTH_15_Recovery_Uses_The_Registration_Email_Length_Limit(int length, bool accepted)
    {
        string email = new string('a', length - "@example.com".Length) + "@example.com";

        new ForgotPasswordValidator().Validate(new ForgotPasswordCommand(email)).IsValid.ShouldBe(accepted);
        new VerifyResetCodeValidator().Validate(new VerifyResetCodeCommand(email, "012345")).IsValid.ShouldBe(accepted);
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("012345")]
    [InlineData("999999")]
    public void AUTH_15_Code_Validation_Preserves_Leading_Zeroes(string code)
    {
        new VerifyResetCodeValidator().Validate(new VerifyResetCodeCommand(Email, code)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData(" 12345")]
    [InlineData("12345 ")]
    [InlineData("123456\n")]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("１２３４５６")]
    public void AUTH_15_Code_Validation_Rejects_Non_Ascii_Or_Malformed_Codes(string? code)
    {
        new VerifyResetCodeValidator().Validate(new VerifyResetCodeCommand(Email, code!)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void AUTH_15_Registration_And_Reset_Share_Password_Length_Boundaries(int length, bool accepted)
    {
        string password = new string('a', length - 1) + "1";

        AssertPasswordAcceptedByBoth(password, accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("          ")]
    [InlineData("letterswithoutdigits")]
    [InlineData("123456789012")]
    public void AUTH_15_Registration_And_Reset_Reject_Weak_Or_Missing_Passwords(string? password)
    {
        AssertPasswordAcceptedByBoth(password!, false);
    }

    [Fact]
    public async Task AUTH_15_Invalid_Input_Reports_All_Fields_Without_Executing_Reset_Or_Echoing_Credentials()
    {
        const string code = "12345X";
        const string password = "secretpass";
        ValidationBehavior<VerifyResetCodeCommand, VerifyResetCodeOutcome> behavior = new([new VerifyResetCodeValidator()]);
        bool handlerRan = false;

        Result<VerifyResetCodeOutcome> result = await behavior.HandleAsync(
            new VerifyResetCodeCommand("invalid-email", code),
            CancellationToken.None,
            () =>
            {
                handlerRan = true;
                return Task.FromResult(Result<VerifyResetCodeOutcome>.Success(new VerifyResetCodeOutcome.Rejected()));
            });

        handlerRan.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(Error.ValidationCode);
        result.Error.ValidationErrors.Keys.ShouldBe(
            [nameof(VerifyResetCodeCommand.Email), nameof(VerifyResetCodeCommand.Code)],
            ignoreOrder: true);
        string messages = string.Join(" ", result.Error.ValidationErrors.Values.SelectMany(errors => errors));
        messages.ShouldNotContain(code);
        messages.ShouldNotContain(password);
    }

    [Fact]
    public async Task AUTH_15_Rejected_Recovery_Outcome_Commits_Attempt_State_Before_Public_Rejection()
    {
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        TransactionBehavior<VerifyResetCodeCommand, VerifyResetCodeOutcome> behavior = new(unitOfWork, new MessageBuffer());

        Result<VerifyResetCodeOutcome> result = await behavior.HandleAsync(
            new VerifyResetCodeCommand(Email, "012345"),
            CancellationToken.None,
            () => Task.FromResult(Result<VerifyResetCodeOutcome>.Success(new VerifyResetCodeOutcome.Rejected())));

        result.Value.ShouldBeOfType<VerifyResetCodeOutcome.Rejected>();
        Received.InOrder(() =>
        {
            unitOfWork.SaveChangesAsync(CancellationToken.None);
            unitOfWork.CommitTransactionAsync(CancellationToken.None);
        });
        await unitOfWork.DidNotReceive().RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AUTH_15_Application_Scanning_Registers_Recovery_Validators()
    {
        ServiceCollection services = new();
        services.AddApplication();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IValidator<ForgotPasswordCommand>>()
            .ShouldBeOfType<ForgotPasswordValidator>();
        scope.ServiceProvider.GetRequiredService<IValidator<VerifyResetCodeCommand>>()
            .ShouldBeOfType<VerifyResetCodeValidator>();
        scope.ServiceProvider.GetRequiredService<IValidator<ResetPasswordCommand>>()
            .ShouldBeOfType<ResetPasswordValidator>();
    }

    private static void AssertPasswordAcceptedByBoth(string password, bool accepted)
    {
        new RegisterValidator().Validate(new RegisterCommand(Email, password, "Recovery", null, null))
            .IsValid.ShouldBe(accepted);
        new ResetPasswordValidator().Validate(new ResetPasswordCommand(new string('A', 43), password))
            .IsValid.ShouldBe(accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("012345")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    public void AUTH_17_Reset_Requires_A_Well_Formed_Reset_Token(string? token) =>
        new ResetPasswordValidator().Validate(new ResetPasswordCommand(token!, Password)).IsValid.ShouldBeFalse();

    [Fact]
    public async Task AUTH_17_Reset_Validation_Does_Not_Echo_Token_Or_Password()
    {
        const string token = "private-token-marker";
        const string password = "secretpass";
        var behavior = new ValidationBehavior<ResetPasswordCommand, ResetPasswordOutcome>([new ResetPasswordValidator()]);
        var result = await behavior.HandleAsync(new ResetPasswordCommand(token, password), CancellationToken.None,
            () => throw new InvalidOperationException("Invalid input reached the handler."));
        result.Error!.ValidationErrors.Keys.ShouldBe([nameof(ResetPasswordCommand.ResetToken), nameof(ResetPasswordCommand.NewPassword)], ignoreOrder: true);
        string messages = string.Join(' ', result.Error.ValidationErrors.Values.SelectMany(errors => errors));
        messages.ShouldNotContain(token);
        messages.ShouldNotContain(password);
    }
}
