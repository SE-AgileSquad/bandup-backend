using System.Text.Json;
using BandUp.Infrastructure.Logging;
using Serilog.Events;
using Serilog.Parsing;
using Shouldly;

namespace BandUp.UnitTests.Logging;

public sealed class LoggingTests
{
    private static readonly MessageTemplateParser Parser = new();

    [Fact]
    public void INF_11_Log_Line_Is_Json_With_The_Required_Keys()
    {
        JsonElement line = Format(CreateEvent(LogEventLevel.Information, "Handled {RequestName}", "SendPing"));

        line.ValueKind.ShouldBe(JsonValueKind.Object);

        foreach (string key in new[]
                 {
                     BandUpLogProperties.Timestamp,
                     BandUpLogProperties.Level,
                     BandUpLogProperties.Message,
                     BandUpLogProperties.RequestId,
                     BandUpLogProperties.Route,
                     BandUpLogProperties.StatusCode,
                     BandUpLogProperties.DurationMs,
                     BandUpLogProperties.Service,
                 })
        {
            line.TryGetProperty(key, out _).ShouldBeTrue($"'{key}' must be present on every line.");
        }
    }

    [Fact]
    public void INF_11_One_Line_Is_Written_Per_Event()
    {
        StringWriter output = new();
        BandUpJsonFormatter formatter = new();

        formatter.Format(CreateEvent(LogEventLevel.Information, "first"), output);
        formatter.Format(CreateEvent(LogEventLevel.Information, "second"), output);

        string[] lines = output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(2);

        foreach (string line in lines)
        {
            using JsonDocument document = JsonDocument.Parse(line);
            document.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        }
    }

    [Fact]
    public void INF_11_Timestamp_Is_Iso_8601_Utc_And_Level_Is_Textual()
    {
        JsonElement line = Format(CreateEvent(LogEventLevel.Warning, "Careful"));

        line.GetProperty(BandUpLogProperties.Level).GetString().ShouldBe("WARN");

        string timestamp = line.GetProperty(BandUpLogProperties.Timestamp).GetString()!;

        DateTimeOffset.TryParse(timestamp, out DateTimeOffset parsed).ShouldBeTrue();
        parsed.Offset.ShouldBe(TimeSpan.Zero);
        timestamp.ShouldEndWith("Z");
    }

    [Fact]
    public void INF_11_Message_Is_Rendered_From_The_Template()
    {
        JsonElement line = Format(CreateEvent(LogEventLevel.Information, "Handled {RequestName}", "SendPing"));

        // Serilog's renderer quotes string properties; the structured values carry the
        // unquoted form, so query on those rather than parsing the message.
        line.GetProperty(BandUpLogProperties.Message).GetString().ShouldBe("Handled \"SendPing\"");
    }

    [Fact]
    public void INF_11_Reserved_Properties_Are_Lifted_To_The_Top_Level()
    {
        LogEvent logEvent = CreateEvent(
            LogEventLevel.Information,
            "Handled",
            properties:
            [
                new(BandUpLogProperties.RequestId, new ScalarValue("abc123")),
                new(BandUpLogProperties.Route, new ScalarValue("/api/system/ping")),
                new(BandUpLogProperties.StatusCode, new ScalarValue(202)),
                new(BandUpLogProperties.DurationMs, new ScalarValue(12.5)),
                new(BandUpLogProperties.Service, new ScalarValue("api")),
            ]);

        JsonElement line = Format(logEvent);

        line.GetProperty(BandUpLogProperties.RequestId).GetString().ShouldBe("abc123");
        line.GetProperty(BandUpLogProperties.Route).GetString().ShouldBe("/api/system/ping");
        line.GetProperty(BandUpLogProperties.StatusCode).GetInt32().ShouldBe(202);
        line.GetProperty(BandUpLogProperties.DurationMs).GetDouble().ShouldBe(12.5);
        line.GetProperty(BandUpLogProperties.Service).GetString().ShouldBe("api");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("PasswordHash")]
    [InlineData("confirmPassword")]
    [InlineData("refreshToken")]
    [InlineData("Authorization")]
    [InlineData("cookieHeader")]
    [InlineData("clientSecret")]
    [InlineData("EssayText")]
    public void INF_11_Sensitive_Properties_Are_Masked(string propertyName)
    {
        LogEvent logEvent = CreateEvent(
            LogEventLevel.Information,
            "Handled",
            properties: [new(propertyName, new ScalarValue("super-secret-value"))]);

        JsonElement line = Format(logEvent);

        line.GetProperty(propertyName).GetString().ShouldBe("***");
    }

    [Fact]
    public void INF_11_Email_Is_Partially_Masked()
    {
        LogEvent logEvent = CreateEvent(
            LogEventLevel.Information,
            "Handled",
            properties: [new("email", new ScalarValue("student@example.com"))]);

        JsonElement line = Format(logEvent);

        line.GetProperty("email").GetString().ShouldBe("s***@example.com");
    }

    [Fact]
    public void INF_11_Non_Sensitive_Properties_Are_Unchanged()
    {
        LogEvent logEvent = CreateEvent(
            LogEventLevel.Information,
            "Handled",
            properties: [new("questionId", new ScalarValue("q-17"))]);

        JsonElement line = Format(logEvent);

        line.GetProperty("questionId").GetString().ShouldBe("q-17");
    }

    [Fact]
    public void INF_11_Exception_Is_Included_When_Present()
    {
        LogEvent logEvent = new(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            new InvalidOperationException("boom"),
            Parser.Parse("Failed"),
            []);

        JsonElement line = Format(logEvent);

        string? exception = line.GetProperty(BandUpLogProperties.Exception).GetString();

        exception.ShouldNotBeNull();
        exception.ShouldContain("InvalidOperationException");
        exception.ShouldContain("boom");
    }

    [Fact]
    public void INF_11_Exception_Is_Absent_When_The_Event_Has_None()
    {
        JsonElement line = Format(CreateEvent(LogEventLevel.Information, "Fine"));

        line.TryGetProperty(BandUpLogProperties.Exception, out _).ShouldBeFalse();
    }

    private static LogEvent CreateEvent(
        LogEventLevel level,
        string template,
        string? argument = null,
        LogEventProperty[]? properties = null)
    {
        MessageTemplate parsed = Parser.Parse(template);

        List<LogEventProperty> all = properties?.ToList() ?? [];

        if (argument is not null)
        {
            string name = parsed.Tokens.OfType<PropertyToken>().First().PropertyName;
            all.Add(new LogEventProperty(name, new ScalarValue(argument)));
        }

        return new LogEvent(DateTimeOffset.UtcNow, level, null, parsed, all);
    }

    private static JsonElement Format(LogEvent logEvent)
    {
        StringWriter output = new();
        new BandUpJsonFormatter().Format(logEvent, output);

        return JsonDocument.Parse(output.ToString()).RootElement;
    }
}