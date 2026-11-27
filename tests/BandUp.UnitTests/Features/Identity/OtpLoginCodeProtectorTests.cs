using System.Security.Cryptography;
using System.Text;
using BandUp.Application.Features.Identity;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Persistence;
using BandUp.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class OtpLoginCodeProtectorTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly Guid ChallengeId = Guid.Parse("019a8000-1111-7000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("019a8000-2222-7000-8000-000000000002");

    [Fact]
    public void AUTH_16_Protection_Uses_A_Purpose_And_Identity_Bound_Keyed_Mac()
    {
        IOtpLoginCodeProtector protector = CreateProtector();
        string expected = Convert.ToHexStringLower(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(
            "bandup:otp-login:v1\n019a8000-1111-7000-8000-000000000001\n019a8000-2222-7000-8000-000000000002\n012345")));

        string mac = protector.Protect(ChallengeId, UserId, "012345");
        mac.ShouldBe(expected);
        protector.Verify(ChallengeId, UserId, "012345", mac).ShouldBeTrue();
        protector.Verify(ChallengeId, UserId, "112345", mac).ShouldBeFalse();
        protector.Verify(Guid.CreateVersion7(), UserId, "012345", mac).ShouldBeFalse();
        protector.Verify(ChallengeId, Guid.CreateVersion7(), "012345", mac).ShouldBeFalse();
        CreateProtector(Enumerable.Repeat((byte)42, 32).ToArray()).Verify(ChallengeId, UserId, "012345", mac).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("123456\n")]
    [InlineData("١٢٣٤٥٦")]
    public void AUTH_16_Malformed_Codes_Do_Not_Reach_Mac_Verification(string? code)
    {
        IOtpLoginCodeProtector protector = CreateProtector();
        protector.Verify(ChallengeId, UserId, code, new string('a', 64)).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => protector.Protect(ChallengeId, UserId, code!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("012345")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void AUTH_16_Malformed_Stored_Macs_Are_Rejected_Without_Exceptions(string? mac) =>
        CreateProtector().Verify(ChallengeId, UserId, "012345", mac).ShouldBeFalse();

    [Fact]
    public void AUTH_16_Generation_Always_Produces_Six_Ascii_Digits()
    {
        IOtpLoginCodeProtector protector = CreateProtector();
        for (int index = 0; index < 100; index++)
        {
            string code = protector.GenerateCode();
            code.Length.ShouldBe(6);
            code.All(char.IsAsciiDigit).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("YWJj")]
    public void AUTH_16_Missing_Or_Weak_Protection_Keys_Fail_With_A_Sanitized_Configuration_Error(string? key)
    {
        var exception = Should.Throw<OptionsValidationException>(() => new OtpLoginCodeProtector(
            Options.Create(new OtpLoginSettings { CodeProtectionKey = key })));
        exception.Message.ShouldContain("OtpLogin:CodeProtectionKey");
        if (!string.IsNullOrEmpty(key))
        {
            exception.Message.ShouldNotContain(key);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AUTH_16_Resolution_Validates_Otp_Login_Configuration_And_Registers_The_Store(bool configured)
    {
        Dictionary<string, string?> settings = new()
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=unused;Username=unused",
            ["Cache:Provider"] = "InMemory",
            ["MessagePublisher:Provider"] = "InMemory",
            ["OtpLogin:CodeProtectionKey"] = configured ? Convert.ToBase64String(Key) : null,
        };
        ServiceCollection services = new();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOtpLoginChallengeStore>().ShouldBeOfType<EfOtpLoginChallengeStore>();
        if (configured)
        {
            provider.GetRequiredService<IOtpLoginCodeProtector>().ShouldBeOfType<OtpLoginCodeProtector>();
        }
        else
        {
            Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOtpLoginCodeProtector>());
        }
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, true)]
    [InlineData(64, true)]
    public void AUTH_16_Key_Strength_Is_Measured_In_Decoded_Bytes(int bytes, bool accepted)
    {
        byte[] key = Enumerable.Repeat((byte)42, bytes).ToArray();
        if (accepted)
        {
            CreateProtector(key).Protect(ChallengeId, UserId, "012345").Length.ShouldBe(64);
        }
        else
        {
            Should.Throw<OptionsValidationException>(() => CreateProtector(key));
        }
    }

    private static OtpLoginCodeProtector CreateProtector(byte[]? key = null) =>
        new(Options.Create(new OtpLoginSettings { CodeProtectionKey = Convert.ToBase64String(key ?? Key) }));

    [Fact]
    public void AUTH_16_Login_And_Reset_Cannot_Verify_Each_Others_Macs_Even_With_The_Same_Key()
    {
        var login = CreateProtector();
        var reset = new PasswordResetCodeProtector(Options.Create(new PasswordResetSettings { CodeProtectionKey = Convert.ToBase64String(Key) }));
        login.Verify(ChallengeId, UserId, "012345", reset.Protect(ChallengeId, UserId, "012345")).ShouldBeFalse();
        reset.Verify(ChallengeId, UserId, "012345", login.Protect(ChallengeId, UserId, "012345")).ShouldBeFalse();
    }
}
