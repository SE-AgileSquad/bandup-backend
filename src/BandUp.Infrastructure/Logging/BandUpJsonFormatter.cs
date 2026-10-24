using System.Globalization;
using System.Text.Json;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace BandUp.Infrastructure.Logging;

/// <summary>
/// Writes one JSON object per line with the keys the platform standardises on, so every
/// service's logs are queryable the same way.
/// </summary>
public sealed class BandUpJsonFormatter : ITextFormatter
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public void Format(LogEvent logEvent, TextWriter output)
    {
        Dictionary<string, LogEventPropertyValue> properties = [];

        foreach ((string key, LogEventPropertyValue value) in logEvent.Properties)
        {
            properties[key] = value;
        }

        using MemoryStream buffer = new();

        using (Utf8JsonWriter writer = new(buffer, WriterOptions))
        {
            writer.WriteStartObject();

            writer.WriteString(BandUpLogProperties.Timestamp, logEvent.Timestamp.UtcDateTime);
            writer.WriteString(BandUpLogProperties.Level, Level(logEvent.Level));
            writer.WriteString(BandUpLogProperties.Message, RenderMessage(logEvent));

            WriteReserved(writer, BandUpLogProperties.RequestId, properties);
            WriteReserved(writer, BandUpLogProperties.Route, properties);
            WriteReserved(writer, BandUpLogProperties.StatusCode, properties);
            WriteReserved(writer, BandUpLogProperties.DurationMs, properties);
            WriteReserved(writer, BandUpLogProperties.Service, properties);

            // userId is optional and only appears once a request is authenticated.
            if (properties.ContainsKey(BandUpLogProperties.UserId))
            {
                WriteReserved(writer, BandUpLogProperties.UserId, properties);
            }

            if (logEvent.Exception is not null)
            {
                writer.WriteString(BandUpLogProperties.Exception, logEvent.Exception.ToString());
            }

            foreach ((string name, LogEventPropertyValue value) in properties)
            {
                if (!BandUpLogProperties.Reserved.Contains(name))
                {
                    WriteProperty(writer, name, value);
                }
            }

            writer.WriteEndObject();
        }

        output.Write(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
        output.Write(Environment.NewLine);
    }

    private static string Level(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "VERBOSE",
        LogEventLevel.Debug => "DEBUG",
        LogEventLevel.Information => "INFO",
        LogEventLevel.Warning => "WARN",
        LogEventLevel.Error => "ERROR",
        _ => "FATAL",
    };

    private static string RenderMessage(LogEvent logEvent) =>
        logEvent.RenderMessage(CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes a reserved key. The key is always present so a consumer can rely on the shape;
    /// it is null when the event has no such property.
    /// </summary>
    private static void WriteReserved(
        Utf8JsonWriter writer,
        string name,
        IDictionary<string, LogEventPropertyValue> properties)
    {
        if (properties.TryGetValue(name, out LogEventPropertyValue? value))
        {
            WriteProperty(writer, name, value);
            properties.Remove(name);
            return;
        }

        writer.WriteNull(name);
    }

    /// <summary>
    /// Writes <c>"name": value</c>. Sensitivity is decided from the property name, so a
    /// destructured object's members are masked by their own names.
    /// </summary>
    private static void WriteProperty(Utf8JsonWriter writer, string name, LogEventPropertyValue value)
    {
        if (SensitiveDataMasker.IsSensitive(name))
        {
            writer.WriteString(name, SensitiveDataMasker.Mask(name, Unwrap(value)));
            return;
        }

        writer.WritePropertyName(name);
        WriteValue(writer, value);
    }

    private static void WriteValue(Utf8JsonWriter writer, LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue scalar:
                WriteScalar(writer, scalar.Value);
                break;
            case SequenceValue sequence:
                writer.WriteStartArray();
                foreach (LogEventPropertyValue item in sequence.Elements)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            case StructureValue structure:
                writer.WriteStartObject();
                foreach (LogEventProperty property in structure.Properties)
                {
                    WriteProperty(writer, property.Name, property.Value);
                }

                writer.WriteEndObject();
                break;
            case DictionaryValue dictionary:
                writer.WriteStartObject();
                foreach (KeyValuePair<ScalarValue, LogEventPropertyValue> entry in dictionary.Elements)
                {
                    WriteProperty(writer, Unwrap(entry.Key), entry.Value);
                }

                writer.WriteEndObject();
                break;
        }
    }

    private static void WriteScalar(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case int or long or short or byte or sbyte or uint or ulong or ushort or decimal:
                writer.WriteNumberValue(Convert.ToDecimal(value));
                break;
            case double or float:
                writer.WriteNumberValue(Convert.ToDouble(value));
                break;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime.ToUniversalTime());
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset.UtcDateTime);
                break;
            case Guid or TimeSpan or Enum:
                writer.WriteStringValue(Convert.ToString(value));
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static string Unwrap(LogEventPropertyValue value) =>
        value is ScalarValue scalar ? scalar.Value?.ToString() ?? string.Empty : value.ToString();
}