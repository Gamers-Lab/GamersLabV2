using BattleRecordsRouter.Models;

namespace BattleRecordsRouter.Helper;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// JSON converter that handles deserialization of Device enum from both numeric and string values.
/// </summary>
public class ControllerDeviceToEnum : JsonConverter<Device>
{
    /// <summary>
    /// Reads and converts JSON to a Device enum, accepting both numeric and string representations.
    /// </summary>
    /// <param name="reader">The Utf8JsonReader to read from.</param>
    /// <param name="typeToConvert">The type to convert to (Device).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The deserialized Device enum value.</returns>
    /// <exception cref="JsonException">Thrown when the value is invalid or not a valid Device enum value.</exception>
    public override Device Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Handle numeric input
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetByte(out var byteValue))
        {
            if (Enum.IsDefined(typeof(Device), byteValue))
                return (Device)byteValue;

            throw new JsonException($"Invalid numeric value for Device: {byteValue}");
        }

        // Handle string input
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (Enum.TryParse<Device>(str, ignoreCase: true, out var result))
                return result;

            throw new JsonException($"Invalid string value for Device: {str}");
        }

        throw new JsonException("Invalid value type for Device");
    }

    /// <summary>
    /// Writes the Device enum value as a numeric byte value to JSON.
    /// </summary>
    /// <param name="writer">The Utf8JsonWriter to write to.</param>
    /// <param name="value">The Device enum value to serialize.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Device value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((byte)value);
    }
}