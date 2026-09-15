using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Model;

public static class PolicyFormatVersions
{
    public const string Current = "1.0.0";
    public const ulong SupportedMajor = 1;
}

[JsonConverter(typeof(PolicyFormatVersionJsonConverter))]
public sealed class PolicyFormatVersion : IEquatable<PolicyFormatVersion>
{
    private PolicyFormatVersion(string value)
    {
        Value = value;
    }

    public static PolicyFormatVersion Current { get; } = new(PolicyFormatVersions.Current);

    public string Value { get; }

    public static PolicyFormatVersion Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 128)
        {
            throw new FormatException("PolicyFormatVersion must contain at most 128 characters.");
        }

        var components = value.Split('.');
        if (components.Length != 3
            || !TryParseComponent(components[0], out var major)
            || !TryParseComponent(components[1], out _)
            || !TryParseComponent(components[2], out _))
        {
            throw new FormatException(
                "PolicyFormatVersion must contain three canonical unsigned integer components of at most 18 digits.");
        }
        if (major != PolicyFormatVersions.SupportedMajor)
        {
            throw new NotSupportedException(
                $"Policy format major version {major} is unsupported; supported major version is {PolicyFormatVersions.SupportedMajor}.");
        }

        return new PolicyFormatVersion(value);
    }

    private static bool TryParseComponent(string component, out ulong value)
    {
        value = 0;
        return component.Length is >= 1 and <= 18
            && (component.Length == 1 || component[0] != '0')
            && ulong.TryParse(
            component,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    public bool Equals(PolicyFormatVersion? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as PolicyFormatVersion);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

internal sealed class PolicyFormatVersionJsonConverter : JsonConverter<PolicyFormatVersion>
{
    public override PolicyFormatVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("PolicyFormatVersion must be a string.");
        }

        try
        {
            return PolicyFormatVersion.Parse(reader.GetString()!);
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, PolicyFormatVersion value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>A policy document governing which package operations are allowed or denied.</summary>
public sealed class PolicyDocument
{
    /// <summary>
    /// Software-managed document-format version. Applications must not expose
    /// this as publisher-authored editable metadata.
    /// </summary>
    [JsonPropertyName("PolicyFormatVersion")]
    [JsonRequired]
    public PolicyFormatVersion PolicyFormatVersion { get; init; } = PolicyFormatVersion.Current;

    [JsonPropertyName("PolicyType")]
    [JsonRequired]
    public string PolicyType { get; set; } = "PackageBrokerPolicy";

    [JsonPropertyName("Metadata")]
    [JsonRequired]
    public PolicyMetadata Metadata { get; set; } = new();

    [JsonPropertyName("Enforcement")]
    [JsonRequired]
    public PolicyEnforcement Enforcement { get; set; } = new();

    [JsonPropertyName("Rules")]
    [JsonRequired]
    public List<PolicyRule> Rules { get; set; } = [];

    public static PolicyDocument Create(string id, string publisher, Decision defaultDecision = Decision.Deny)
    {
        return new PolicyDocument
        {
            Metadata = new PolicyMetadata
            {
                Id = id,
                Publisher = publisher,
                Revision = 1,
                PublishedAt = DateTimeOffset.UtcNow,
            },
            Enforcement = new PolicyEnforcement
            {
                DefaultDecision = defaultDecision,
                RulePrecedence = RulePrecedence.PriorityThenDeny,
            },
        };
    }

    public static PolicyDocument ParseJson(string json)
    {
        return PolicySerializer.DeserializePolicyDocumentStrict(json)
            ?? throw new JsonException("policy document was null");
    }

    public PolicyDraftDocument ToDraft()
    {
        return new PolicyDraftDocument
        {
            PolicyFormatVersion = PolicyFormatVersion,
            PolicyType = PolicyType,
            Metadata = PolicyModelClone.ToDraftMetadata(Metadata),
            Enforcement = PolicyModelClone.Enforcement(Enforcement),
            Rules = PolicyModelClone.Rules(Rules),
        };
    }

    public string ToJson() => PolicySerializer.Serialize(this);
}

/// <summary>An editable policy document without server-managed commit metadata.</summary>
public sealed class PolicyDraftDocument
{
    private const uint MaxRevision = int.MaxValue;

    /// <summary>
    /// Software-managed document-format version. Applications must stamp the
    /// current value for new drafts and must not expose it as authored metadata.
    /// </summary>
    [JsonPropertyName("PolicyFormatVersion")]
    [JsonRequired]
    public PolicyFormatVersion PolicyFormatVersion { get; init; } = PolicyFormatVersion.Current;

    [JsonPropertyName("PolicyType")]
    [JsonRequired]
    public string PolicyType { get; set; } = "PackageBrokerPolicy";

    [JsonPropertyName("Metadata")]
    [JsonRequired]
    public PolicyDraftMetadata Metadata { get; set; } = new();

    [JsonPropertyName("Enforcement")]
    [JsonRequired]
    public PolicyEnforcement Enforcement { get; set; } = new();

    [JsonPropertyName("Rules")]
    [JsonRequired]
    public List<PolicyRule> Rules { get; set; } = [];

    public static PolicyDraftDocument Create(
        string id,
        string publisher,
        Decision defaultDecision = Decision.Deny)
    {
        return new PolicyDraftDocument
        {
            Metadata = new PolicyDraftMetadata
            {
                Id = id,
                Publisher = publisher,
            },
            Enforcement = new PolicyEnforcement
            {
                DefaultDecision = defaultDecision,
                RulePrecedence = RulePrecedence.PriorityThenDeny,
            },
        };
    }

    public static PolicyDraftDocument ParseJson(string json)
    {
        return PolicySerializer.DeserializePolicyDraftDocumentStrict(json)
            ?? throw new JsonException("policy draft document was null");
    }

    public PolicyDocument ToPolicyDocument(uint revision, DateTimeOffset publishedAt)
    {
        if (revision is 0 or > MaxRevision)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision),
                $"Policy revisions must be between 1 and {MaxRevision}.");
        }

        return new PolicyDocument
        {
            PolicyFormatVersion = PolicyFormatVersion,
            PolicyType = PolicyType,
            Metadata = PolicyModelClone.ToCommittedMetadata(Metadata, revision, publishedAt),
            Enforcement = PolicyModelClone.Enforcement(Enforcement),
            Rules = PolicyModelClone.Rules(Rules),
        };
    }

    public string ToJson() => PolicySerializer.Serialize(this);
}

public sealed class PolicyMetadata
{
    [JsonPropertyName("Id")]
    [JsonRequired]
    public string Id { get; set; } = "";

    [JsonPropertyName("Publisher")]
    [JsonRequired]
    public string Publisher { get; set; } = "";

    [JsonPropertyName("Revision")]
    [JsonRequired]
    public uint Revision { get; set; }

    [JsonPropertyName("PublishedAt")]
    [JsonRequired]
    public DateTimeOffset PublishedAt { get; set; }

    [JsonPropertyName("ValidFrom")]
    public DateTimeOffset? ValidFrom { get; set; }

    [JsonPropertyName("ValidUntil")]
    public DateTimeOffset? ValidUntil { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("SupportUrl")]
    public string? SupportUrl { get; set; }
}

public sealed class PolicyDraftMetadata
{
    [JsonPropertyName("Id")]
    [JsonRequired]
    public string Id { get; set; } = "";

    [JsonPropertyName("Publisher")]
    [JsonRequired]
    public string Publisher { get; set; } = "";

    [JsonPropertyName("ValidFrom")]
    public DateTimeOffset? ValidFrom { get; set; }

    [JsonPropertyName("ValidUntil")]
    public DateTimeOffset? ValidUntil { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("SupportUrl")]
    public string? SupportUrl { get; set; }
}

public sealed class PolicyEnforcement
{
    [JsonPropertyName("DefaultDecision")]
    [JsonRequired]
    public Decision DefaultDecision { get; set; }

    [JsonPropertyName("RulePrecedence")]
    [JsonRequired]
    public RulePrecedence RulePrecedence { get; set; }

    [JsonPropertyName("AuditMode")]
    public bool? AuditMode { get; set; }
}

public sealed class PolicyRule
{
    [JsonPropertyName("Id")]
    [JsonRequired]
    public string Id { get; set; } = "";

    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("Priority")]
    [JsonRequired]
    public uint Priority { get; set; }

    [JsonPropertyName("Decision")]
    [JsonRequired]
    public Decision Decision { get; set; }

    [JsonPropertyName("Reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("Match")]
    [JsonRequired]
    public PolicyMatch Match { get; set; } = new();

    [JsonPropertyName("Constraints")]
    public PolicyConstraints? Constraints { get; set; }
}

public sealed class PolicyMatch
{
    [JsonPropertyName("Operations")]
    public List<Operation> Operations { get; set; } = [];

    [JsonPropertyName("Managers")]
    public List<ManagerName> Managers { get; set; } = [];

    [JsonPropertyName("Sources")]
    public List<string> Sources { get; set; } = [];

    [JsonPropertyName("PackageIdentifiers")]
    public List<string> PackageIdentifiers { get; set; } = [];

    [JsonPropertyName("PackageNames")]
    public List<string> PackageNames { get; set; } = [];

    [JsonPropertyName("Versions")]
    public List<string> Versions { get; set; } = [];

    [JsonPropertyName("VersionRange")]
    public VersionRange? VersionRange { get; set; }

    [JsonPropertyName("Scopes")]
    public List<Scope> Scopes { get; set; } = [];

    [JsonPropertyName("Architectures")]
    public List<Architecture> Architectures { get; set; } = [];

    [JsonPropertyName("Elevation")]
    public List<Elevation> Elevation { get; set; } = [];

    [JsonPropertyName("Interactive")]
    public List<bool> Interactive { get; set; } = [];

    [JsonPropertyName("SkipHashCheck")]
    public List<bool> SkipHashCheck { get; set; } = [];

    [JsonPropertyName("PreRelease")]
    public List<bool> PreRelease { get; set; } = [];

    [JsonPropertyName("HasCustomParameters")]
    public List<bool> HasCustomParameters { get; set; } = [];

    [JsonPropertyName("HasCustomInstallLocation")]
    public List<bool> HasCustomInstallLocation { get; set; } = [];

    [JsonPropertyName("HasPrePostCommands")]
    public List<bool> HasPrePostCommands { get; set; } = [];

    [JsonPropertyName("HasKillBeforeOperation")]
    public List<bool> HasKillBeforeOperation { get; set; } = [];

    [JsonPropertyName("HasUninstallPrevious")]
    public List<bool> HasUninstallPrevious { get; set; } = [];
}

public sealed class VersionRange
{
    [JsonPropertyName("MinVersion")]
    public string? MinVersion { get; set; }

    [JsonPropertyName("MaxVersion")]
    public string? MaxVersion { get; set; }

    [JsonPropertyName("IncludePrerelease")]
    public bool IncludePrerelease { get; set; }
}

public sealed class PolicyConstraints
{
    [JsonPropertyName("AllowInteractive")]
    public bool AllowInteractive { get; set; } = true;

    [JsonPropertyName("AllowSkipHashCheck")]
    public bool AllowSkipHashCheck { get; set; } = true;

    [JsonPropertyName("AllowPreRelease")]
    public bool AllowPreRelease { get; set; } = true;

    [JsonPropertyName("AllowCustomInstallLocation")]
    public bool AllowCustomInstallLocation { get; set; } = true;

    [JsonPropertyName("AllowedInstallLocationPatterns")]
    public List<string> AllowedInstallLocationPatterns { get; set; } = [];

    [JsonPropertyName("AllowCustomParameters")]
    public bool AllowCustomParameters { get; set; } = true;

    [JsonPropertyName("AllowedCustomParameters")]
    public List<string> AllowedCustomParameters { get; set; } = [];

    [JsonPropertyName("AllowedCustomParameterPatterns")]
    public List<string> AllowedCustomParameterPatterns { get; set; } = [];

    [JsonPropertyName("DeniedCustomParameters")]
    public List<string> DeniedCustomParameters { get; set; } = [];

    [JsonPropertyName("AllowPrePostCommands")]
    public bool AllowPrePostCommands { get; set; } = true;

    [JsonPropertyName("AllowKillBeforeOperation")]
    public bool AllowKillBeforeOperation { get; set; } = true;

    [JsonPropertyName("AllowUninstallPrevious")]
    public bool AllowUninstallPrevious { get; set; } = true;

    [JsonPropertyName("AllowUpgrade")]
    public bool AllowUpgrade { get; set; } = true;
}

internal static class PolicyModelClone
{
    internal static PolicyDraftMetadata ToDraftMetadata(PolicyMetadata value) => new()
    {
        Id = value.Id,
        Publisher = value.Publisher,
        ValidFrom = value.ValidFrom,
        ValidUntil = value.ValidUntil,
        Description = value.Description,
        SupportUrl = value.SupportUrl,
    };

    internal static PolicyMetadata ToCommittedMetadata(
        PolicyDraftMetadata value,
        uint revision,
        DateTimeOffset publishedAt) => new()
        {
            Id = value.Id,
            Publisher = value.Publisher,
            Revision = revision,
            PublishedAt = publishedAt,
            ValidFrom = value.ValidFrom,
            ValidUntil = value.ValidUntil,
            Description = value.Description,
            SupportUrl = value.SupportUrl,
        };

    internal static PolicyEnforcement Enforcement(PolicyEnforcement value) => new()
    {
        DefaultDecision = value.DefaultDecision,
        RulePrecedence = value.RulePrecedence,
        AuditMode = value.AuditMode,
    };

    internal static List<PolicyRule> Rules(IEnumerable<PolicyRule> values) => values.Select(Rule).ToList();

    private static PolicyRule Rule(PolicyRule value) => new()
    {
        Id = value.Id,
        Enabled = value.Enabled,
        Priority = value.Priority,
        Decision = value.Decision,
        Reason = value.Reason,
        Match = Match(value.Match),
        Constraints = value.Constraints is null ? null : Constraints(value.Constraints),
    };

    private static PolicyMatch Match(PolicyMatch value) => new()
    {
        Operations = [.. value.Operations],
        Managers = [.. value.Managers],
        Sources = [.. value.Sources],
        PackageIdentifiers = [.. value.PackageIdentifiers],
        PackageNames = [.. value.PackageNames],
        Versions = [.. value.Versions],
        VersionRange = value.VersionRange is null
            ? null
            : new VersionRange
            {
                MinVersion = value.VersionRange.MinVersion,
                MaxVersion = value.VersionRange.MaxVersion,
                IncludePrerelease = value.VersionRange.IncludePrerelease,
            },
        Scopes = [.. value.Scopes],
        Architectures = [.. value.Architectures],
        Elevation = [.. value.Elevation],
        Interactive = [.. value.Interactive],
        SkipHashCheck = [.. value.SkipHashCheck],
        PreRelease = [.. value.PreRelease],
        HasCustomParameters = [.. value.HasCustomParameters],
        HasCustomInstallLocation = [.. value.HasCustomInstallLocation],
        HasPrePostCommands = [.. value.HasPrePostCommands],
        HasKillBeforeOperation = [.. value.HasKillBeforeOperation],
        HasUninstallPrevious = [.. value.HasUninstallPrevious],
    };

    private static PolicyConstraints Constraints(PolicyConstraints value) => new()
    {
        AllowInteractive = value.AllowInteractive,
        AllowSkipHashCheck = value.AllowSkipHashCheck,
        AllowPreRelease = value.AllowPreRelease,
        AllowCustomInstallLocation = value.AllowCustomInstallLocation,
        AllowedInstallLocationPatterns = [.. value.AllowedInstallLocationPatterns],
        AllowCustomParameters = value.AllowCustomParameters,
        AllowedCustomParameters = [.. value.AllowedCustomParameters],
        AllowedCustomParameterPatterns = [.. value.AllowedCustomParameterPatterns],
        DeniedCustomParameters = [.. value.DeniedCustomParameters],
        AllowPrePostCommands = value.AllowPrePostCommands,
        AllowKillBeforeOperation = value.AllowKillBeforeOperation,
        AllowUninstallPrevious = value.AllowUninstallPrevious,
        AllowUpgrade = value.AllowUpgrade,
    };
}