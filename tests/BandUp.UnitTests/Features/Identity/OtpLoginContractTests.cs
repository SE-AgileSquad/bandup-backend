using System.Text.Json;
using BandUp.Application;
using BandUp.Application.Abstractions;
using BandUp.Application.Behaviors;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.RequestOtpLogin;
using BandUp.Application.Features.Identity.VerifyOtpLogin;
using BandUp.Infrastructure.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class OtpLoginContractTests
{
    private const string Email = "login@example.com";
    private const string CaptchaToken = "fake-captcha-token";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("missing-at.example.com")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public void AUTH_16_Both_Requests_Reject_Invalid_Email(string? email)
    {
        new RequestOtpLoginValidator().Validate(new RequestOtpLoginCommand(email!, CaptchaToken))
            .IsValid.ShouldBeFalse();
        new VerifyOtpLoginValidator().Validate(new VerifyOtpLoginCommand(email!, "012345"))
            .IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(320, true)]
    [InlineData(321, false)]
    public void AUTH_16_Both_Requests_Enforce_The_Registration_Email_Length_Limit(int length, bool accepted)
    {
        string email = new string('a', length - "@example.com".Length) + "@example.com";

        new RequestOtpLoginValidator().Validate(new RequestOtpLoginCommand(email, CaptchaToken))
            .IsValid.ShouldBe(accepted);
        new VerifyOtpLoginValidator().Validate(new VerifyOtpLoginCommand(email, "012345"))
            .IsValid.ShouldBe(accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AUTH_16_Request_Requires_A_Captcha_Token(string? captchaToken)
    {
        new RequestOtpLoginValidator().Validate(new RequestOtpLoginCommand(Email, captchaToken))
            .IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public void AUTH_16_Request_Bounds_The_Captcha_Token_Length(int length, bool accepted)
    {
        new RequestOtpLoginValidator().Validate(new RequestOtpLoginCommand(Email, new string('a', length)))
            .IsValid.ShouldBe(accepted);
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("012345")]
    [InlineData("999999")]
    public void AUTH_16_Verification_Accepts_Six_Digit_Strings_With_Leading_Zeroes(string code)
    {
        new VerifyOtpLoginValidator().Validate(new VerifyOtpLoginCommand(Email, code))
            .IsValid.ShouldBeTrue();
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
    public void AUTH_16_Verification_Rejects_Malformed_Or_Non_Ascii_Codes(string? code)
    {
        new VerifyOtpLoginValidator().Validate(new VerifyOtpLoginCommand(Email, code!))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task AUTH_16_Invalid_Input_Stops_Verification_Without_Echoing_The_Code()
    {
        const string code = "12345X";
        ValidationBehavior<VerifyOtpLoginCommand, VerifyOtpLoginOutcome> behavior = new([new VerifyOtpLoginValidator()]);
        bool handlerRan = false;

        Result<VerifyOtpLoginOutcome> result = await behavior.HandleAsync(
            new VerifyOtpLoginCommand("invalid-email", code),
            CancellationToken.None,
            () =>
            {
                handlerRan = true;
                return Task.FromResult(Result<VerifyOtpLoginOutcome>.Success(new VerifyOtpLoginOutcome.Rejected()));
            });

        handlerRan.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(Error.ValidationCode);
        result.Error.ValidationErrors.Keys.ShouldBe(
            [nameof(VerifyOtpLoginCommand.Email), nameof(VerifyOtpLoginCommand.Code)], ignoreOrder: true);
        string.Join(" ", result.Error.ValidationErrors.Values.SelectMany(errors => errors))
            .ShouldNotContain(code);
    }

    [Fact]
    public void AUTH_16_Request_Acknowledgement_Contains_Only_The_Generic_Message()
    {
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(
            new RequestOtpLoginOutcome(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        json.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["message"]);
        json.RootElement.GetProperty("message").GetString().ShouldBe(
            "If an eligible account exists for this email, we'll send a sign-in code.");
    }

    [Fact]
    public async Task AUTH_16_Rejected_Verification_Commits_Guess_State_Before_Public_Rejection()
    {
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        TransactionBehavior<VerifyOtpLoginCommand, VerifyOtpLoginOutcome> behavior = new(unitOfWork, new MessageBuffer());

        Result<VerifyOtpLoginOutcome> result = await behavior.HandleAsync(
            new VerifyOtpLoginCommand(Email, "012345"),
            CancellationToken.None,
            () => Task.FromResult(Result<VerifyOtpLoginOutcome>.Success(new VerifyOtpLoginOutcome.Rejected())));

        result.Value.ShouldBeOfType<VerifyOtpLoginOutcome.Rejected>();
        Received.InOrder(() =>
        {
            unitOfWork.SaveChangesAsync(CancellationToken.None);
            unitOfWork.CommitTransactionAsync(CancellationToken.None);
        });
        await unitOfWork.DidNotReceive().RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AUTH_16_Application_Scanning_Registers_Both_Validators()
    {
        ServiceCollection services = new();
        services.AddApplication();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IValidator<RequestOtpLoginCommand>>()
            .ShouldBeOfType<RequestOtpLoginValidator>();
        scope.ServiceProvider.GetRequiredService<IValidator<VerifyOtpLoginCommand>>()
            .ShouldBeOfType<VerifyOtpLoginValidator>();
    }

    [Fact]
    public void AUTH_16_Public_Code_Rejection_And_Client_Throttle_Have_Separate_Error_Types()
    {
        Error invalidCode = Error.InvalidOtpLogin();
        invalidCode.Code.ShouldBe("INVALID_OTP_LOGIN_CODE");
        invalidCode.Type.ShouldBe(ErrorType.Unauthorized);

        Error throttled = Error.OtpLoginRateLimited();
        throttled.Code.ShouldBe("OTP_LOGIN_RATE_LIMITED");
        throttled.Type.ShouldBe(ErrorType.TooManyRequests);
    }
}
