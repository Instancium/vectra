namespace Instancium.Vectra.Storage.Disk;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Single source of JSON conventions for index artifacts. The writer (stage 1), the reader (stage 2)
/// and doctor (stage 6) must serialize and parse identically to keep the on-disk format stable.
/// </summary>
internal static class DiskIndexJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] SerializeToUtf8Bytes<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T DeserializeFromUtf8Bytes<T>(byte[] utf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(utf8Bytes);
        return JsonSerializer.Deserialize<T>(utf8Bytes, Options)
            ?? throw new InvalidOperationException($"Deserialized value of {typeof(T).Name} was null.");
    }
}
