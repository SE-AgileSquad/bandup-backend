using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BandUp.Infrastructure;

/// <summary>
/// The single JSON contract for everything that crosses a process boundary: cache values,
/// message bodies and the API document. Adapters must not pick their own options, or a value
/// written by the API becomes unreadable to the Worker.
/// </summary>
public static class JsonSerialization
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}