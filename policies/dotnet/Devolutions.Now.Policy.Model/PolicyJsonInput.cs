using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Devolutions.Now.Policy.Model;

internal static class PolicyJsonInput
{
    internal static void RejectDuplicatePropertyNames(string json, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(json);

        var byteCount = Encoding.UTF8.GetByteCount(json);
        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var bytesWritten = Encoding.UTF8.GetBytes(json.AsSpan(), buffer);
            var reader = new Utf8JsonReader(
                buffer.AsSpan(0, bytesWritten),
                new JsonReaderOptions
                {
                    AllowTrailingCommas = options.AllowTrailingCommas,
                    CommentHandling = options.ReadCommentHandling,
                    MaxDepth = options.MaxDepth,
                });

            if (!reader.Read())
            {
                throw new JsonException("The JSON input is empty.");
            }

            RejectDuplicatePropertyNames(ref reader);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static void RejectDuplicatePropertyNames(ref Utf8JsonReader reader)
    {
        var scanner = reader;
        var rootToken = scanner.TokenType;
        var rootDepth = scanner.CurrentDepth;
        var objectProperties = new Stack<HashSet<string>>();

        ProcessToken(ref scanner, objectProperties);
        if (rootToken is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
        {
            return;
        }

        while (scanner.Read())
        {
            ProcessToken(ref scanner, objectProperties);
            if (scanner.CurrentDepth == rootDepth
                && ((rootToken == JsonTokenType.StartObject && scanner.TokenType == JsonTokenType.EndObject)
                    || (rootToken == JsonTokenType.StartArray && scanner.TokenType == JsonTokenType.EndArray)))
            {
                return;
            }
        }

        throw new JsonException("The JSON input ended before the current value was complete.");
    }

    private static void ProcessToken(
        ref Utf8JsonReader reader,
        Stack<HashSet<string>> objectProperties)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
                break;
            case JsonTokenType.EndObject:
                objectProperties.Pop();
                break;
            case JsonTokenType.PropertyName:
                string propertyName;
                try
                {
                    propertyName = reader.GetString()
                        ?? throw new JsonException("A JSON property name must not be null.");
                }
                catch (InvalidOperationException exception)
                {
                    throw new JsonException(
                        "A JSON property name contains an invalid Unicode escape sequence.",
                        exception);
                }

                if (!objectProperties.Peek().Add(propertyName))
                {
                    throw new JsonException(
                        $"Duplicate JSON property name '{propertyName}' is not allowed; property names are compared using ordinal, case-sensitive equality.");
                }
                break;
        }
    }
}

internal interface IDuplicatePropertyNameRejectingConverter;

internal sealed class DuplicatePropertyNameRejectingConverter<T>(
    JsonTypeInfo<T> fallbackTypeInfo,
    Action<T?>? validate = null)
    : JsonConverter<T>, IDuplicatePropertyNameRejectingConverter
{
    private readonly ConditionalWeakTable<JsonSerializerOptions, JsonTypeInfo<T>> _effectiveTypeInfos = new();

    public override T? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        PolicyJsonInput.RejectDuplicatePropertyNames(ref reader);
        var value = JsonSerializer.Deserialize(ref reader, EffectiveTypeInfo(options));
        validate?.Invoke(value);
        return value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
    {
        validate?.Invoke(value);
        JsonSerializer.Serialize(writer, value, EffectiveTypeInfo(options));
    }

    private JsonTypeInfo<T> EffectiveTypeInfo(JsonSerializerOptions options)
    {
        return _effectiveTypeInfos.GetValue(options, CreateEffectiveTypeInfo);
    }

    private JsonTypeInfo<T> CreateEffectiveTypeInfo(JsonSerializerOptions options)
    {
        if (options.TypeInfoResolver is null)
        {
            return fallbackTypeInfo;
        }

        var innerOptions = new JsonSerializerOptions(options);
        for (var index = innerOptions.Converters.Count - 1; index >= 0; index--)
        {
            if (innerOptions.Converters[index] is IDuplicatePropertyNameRejectingConverter)
            {
                innerOptions.Converters.RemoveAt(index);
            }
        }

        return (JsonTypeInfo<T>)innerOptions.GetTypeInfo(typeof(T));
    }
}