using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Model;

public static class PolicyJson
{
    public static readonly JsonSerializerOptions Options = new(PolicyJsonSerializerContext.Default.Options)
    {
    };

    public static readonly JsonSerializerOptions StrictOptions = new(Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PolicyDocument))]
[JsonSerializable(typeof(PolicyMetadata))]
[JsonSerializable(typeof(PolicyEnforcement))]
[JsonSerializable(typeof(PolicyRule))]
[JsonSerializable(typeof(PolicyMatch))]
[JsonSerializable(typeof(VersionRange))]
[JsonSerializable(typeof(PolicyConstraints))]
internal sealed partial class PolicyJsonSerializerContext : JsonSerializerContext;