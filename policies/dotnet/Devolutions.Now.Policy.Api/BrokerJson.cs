using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Api;

/// <summary>Canonical schema URI used in the <c>$schema</c> field of policy documents.</summary>
public static class SchemaUris
{
    public const string Policy = "https://devolutions.net/schemas/now-policy.schema.1.0.json";
}

/// <summary>Shared <see cref="JsonSerializerOptions"/> for broker documents.</summary>
public static class BrokerJson
{
    /// <summary>
    /// Serialization options matching the broker wire format: PascalCase property names
    /// (via explicit <c>[JsonPropertyName]</c> attributes), PascalCase enum values, and
    /// null optionals omitted (mirroring the Rust <c>skip_serializing_if = "Option::is_none"</c>).
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static readonly JsonSerializerOptions PrettyOptions = new(Options)
    {
        WriteIndented = true,
    };
}