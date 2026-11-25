using BandUp.Application;
using BandUp.Application.Abstractions;
using BandUp.Application.Behaviors;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.ForgotPassword;
using BandUp.Application.Features.Identity.Register;
using BandUp.Application.Features.Identity.ResetPassword;
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
        new ResetPasswordValidator().Validate(new ResetPasswordCommand(email!, "012345", Password)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(320, true)]
    [InlineData(321, false)]
    public void AUTH_15_Recovery_Uses_The_Registration_Email_Length_Limit(int length, bool accepted)
    {
        string email = new string('a', length - "@example.com".Length) + "@example.com";

        new ForgotPasswordValidator().Validate(new ForgotPasswordCommand(email)).IsValid.ShouldBe(accepted);
        new ResetPasswordValidator().Validate(new ResetPasswordCommand(email, "012345", Password)).IsValid.ShouldBe(accepted);
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("012345")]
    [InlineData("999999")]
    public void AUTH_15_Code_Validation_Preserves_Leading_Zeroes(string code)
    {
        new ResetPasswordValidator().Validate(new ResetPasswordCommand(Email, code, Password)).IsValid.ShouldBeTrue();
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
        new ResetPasswordValidator().Validate(new ResetPasswordCommand(Email, code!, Password)).IsValid.ShouldBeFalse();
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
        ValidationBehavior<ResetPasswordCommand, ResetPasswordOutcome> behavior = new([new ResetPasswordValidator()]);
        bool handlerRan = false;

        Result<ResetPasswordOutcome> result = await behavior.HandleAsync(
            new ResetPasswordCommand("invalid-email", code, password),
            CancellationToken.None,
            () =>
            {
                handlerRan = true;
                return Task.FromResult(Result<ResetPasswordOutcome>.Success(new ResetPasswordOutcome.Completed()));
            });

        handlerRan.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(Error.ValidationCode);
        result.Error.ValidationErrors.Keys.ShouldBe(
            [nameof(ResetPasswordCommand.Email), nameof(ResetPasswordCommand.Code), nameof(ResetPasswordCommand.NewPassword)],
            ignoreOrder: true);
        string messages = string.Join(" ", result.Error.ValidationErrors.Values.SelectMany(errors => errors));
        messages.ShouldNotContain(code);
        messages.ShouldNotContain(password);
    }

    [Fact]
    public async Task AUTH_15_Rejected_Recovery_Outcome_Commits_Attempt_State_Before_Public_Rejection()
    {
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        TransactionBehavior<ResetPasswordCommand, ResetPasswordOutcome> behavior = new(unitOfWork, new MessageBuffer());

        Result<ResetPasswordOutcome> result = await behavior.HandleAsync(
            new ResetPasswordCommand(Email, "012345", Password),
            CancellationToken.None,
            () => Task.FromResult(Result<ResetPasswordOutcome>.Success(new ResetPasswordOutcome.Rejected())));

        result.Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
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
        scope.ServiceProvider.GetRequiredService<IValidator<ResetPasswordCommand>>()
            .ShouldBeOfType<ResetPasswordValidator>();
    }

    private static void AssertPasswordAcceptedByBoth(string password, bool accepted)
    {
        new RegisterValidator().Validate(new RegisterCommand(Email, password, "Recovery", null, null))
            .IsValid.ShouldBe(accepted);
        new ResetPasswordValidator().Validate(new ResetPasswordCommand(Email, "012345", password))
            .IsValid.ShouldBe(accepted);
    }
}
