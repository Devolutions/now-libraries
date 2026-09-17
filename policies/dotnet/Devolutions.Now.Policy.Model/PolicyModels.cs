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
                "PolicyFormatVersion must contain three canonical unsigned 64-bit integer components.");
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
        return component.Length >= 1
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
        PolicySerializer.ValidateRequiredCollectionElements(this);
        return new PolicyDraftDocument
        {
            PolicyFormatVersion = PolicyFormatVersion,
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

        PolicySerializer.ValidateRequiredCollectionElements(this);
        return new PolicyDocument
        {
            PolicyFormatVersion = PolicyFormatVersion,
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

    /// <summary>
    /// Earliest instant when the policy is active. When both bounds are present, this must be
    /// strictly earlier than <see cref="ValidUntil"/>.
    /// </summary>
    [JsonPropertyName("ValidFrom")]
    public DateTimeOffset? ValidFrom { get; set; }

    /// <summary>
    /// Instant after which the policy is inactive. When both bounds are present, this must be
    /// strictly later than <see cref="ValidFrom"/>.
    /// </summary>
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

    /// <summary>
    /// Earliest instant when the policy is active. When both bounds are present, this must be
    /// strictly earlier than <see cref="ValidUntil"/>.
    /// </summary>
    [JsonPropertyName("ValidFrom")]
    public DateTimeOffset? ValidFrom { get; set; }

    /// <summary>
    /// Instant after which the policy is inactive. When both bounds are present, this must be
    /// strictly later than <see cref="ValidFrom"/>.
    /// </summary>
    [JsonPropertyName("ValidUntil")]
    public DateTimeOffset? ValidUntil { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("SupportUrl")]
    public string? SupportUrl { get; set; }
}

/// <summary>
/// Enforcement configuration. Matching rules are evaluated by ascending priority. Deny wins
/// equal-priority Allow/Deny ties; remaining equal-priority ties retain document order.
/// </summary>
public sealed class PolicyEnforcement
{
    [JsonPropertyName("DefaultDecision")]
    [JsonRequired]
    public Decision DefaultDecision { get; set; }

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

    /// <summary>Additional safety limits for an Allow rule. Invalid on Deny rules.</summary>
    [JsonPropertyName("Constraints")]
    public PolicyConstraints? Constraints { get; set; }
}

/// <summary>
/// Conditions that must all match a request. At least one effective non-null, nonempty condition
/// is required when used by a <see cref="PolicyRule"/>.
/// </summary>
public sealed class PolicyMatch
{
    /// <summary>
    /// Optional operation filter. Omitted/empty does not narrow matching; canonical output omits empty.
    /// </summary>
    [JsonPropertyName("Operations")]
    public List<Operation> Operations { get; set; } = [];

    /// <summary>
    /// Optional manager filter. Omitted/empty does not narrow matching; canonical output omits empty.
    /// </summary>
    [JsonPropertyName("Managers")]
    public List<ManagerName> Managers { get; set; } = [];

    /// <summary>
    /// Exact configured package source names. Comparison follows the selected package manager's
    /// source-name semantics; wildcard characters are literal. Nonempty source names require
    /// exactly one manager. Omitted/empty does not narrow matching; canonical output omits empty.
    /// </summary>
    [JsonPropertyName("SourceNames")]
    public List<string> SourceNames { get; set; } = [];

    /// <summary>
    /// Optional package-identifier condition. Exact uses validated stable identifiers; Patterns
    /// explicitly uses wildcard patterns that may authorize multiple packages. Null does not
    /// narrow matching.
    /// </summary>
    [JsonPropertyName("PackageIdentifiers")]
    public PackageIdentifierCondition? PackageIdentifiers { get; set; }

    /// <summary>
    /// Optional package-version condition. Exact supports arbitrary package version strings;
    /// Range applies only to semantic versions. Null does not narrow matching.
    /// </summary>
    [JsonPropertyName("Version")]
    public VersionCondition? Version { get; set; }

    /// <summary>
    /// Optional scope filter. Omitted/empty does not narrow matching; canonical output omits empty.
    /// </summary>
    [JsonPropertyName("Scopes")]
    public List<Scope> Scopes { get; set; } = [];

    /// <summary>
    /// Optional architecture filter. Omitted/empty does not narrow matching; canonical output omits empty.
    /// </summary>
    [JsonPropertyName("Architectures")]
    public List<Architecture> Architectures { get; set; } = [];

    /// <summary>
    /// Optional effective execution-elevation filter. Elevated means the package operation will run
    /// with administrator privileges; Standard means it will not. Omitted/empty does not narrow
    /// matching; canonical output omits empty.
    /// </summary>
    [JsonPropertyName("ExecutionElevation")]
    public List<Elevation> ExecutionElevation { get; set; } = [];

    /// <summary>
    /// Optional condition on the request's interactive characteristic.
    /// Null means this characteristic does not affect matching.
    /// </summary>
    [JsonPropertyName("Interactive")]
    public bool? Interactive { get; set; }

    /// <summary>
    /// Optional condition on the request's skip-hash-check characteristic.
    /// Null means this characteristic does not affect matching.
    /// </summary>
    [JsonPropertyName("SkipHashCheck")]
    public bool? SkipHashCheck { get; set; }

    /// <summary>
    /// Optional condition on the request's pre-release characteristic.
    /// Null means this characteristic does not affect matching.
    /// </summary>
    [JsonPropertyName("PreRelease")]
    public bool? PreRelease { get; set; }

    /// <summary>
    /// Optional condition on whether the request has custom parameters.
    /// Null means this characteristic does not affect matching.
    /// </summary>
    [JsonPropertyName("HasCustomParameters")]
    public bool? HasCustomParameters { get; set; }

    /// <summary>
    /// Optional condition on whether the request has a custom install location.
    /// Null means this characteristic does not affect matching.
    /// </summary>
    [JsonPropertyName("HasCustomInstallLocation")]
    public bool? HasCustomInstallLocation { get; set; }

    /// <summary>
    /// Optional condition on whether the request has pre/post commands.
    /// Null means this characteristic does not affect matching.
    /// </summary>
    [JsonPropertyName("HasPrePostCommands")]
    public bool? HasPrePostCommands { get; set; }

    /// <summary>
    /// Optional condition on whether the request has kill-before-operation entries.
    /// Null means this characteristic does not affect matching.
    /// </summary>
    [JsonPropertyName("HasKillBeforeOperation")]
    public bool? HasKillBeforeOperation { get; set; }

    /// <summary>
    /// Optional condition on whether the request enables uninstall-previous.
    /// Null means this characteristic does not affect matching.
    /// </summary>
    [JsonPropertyName("HasUninstallPrevious")]
    public bool? HasUninstallPrevious { get; set; }
}

/// <summary>Exactly one package-version matching mode.</summary>
public sealed class VersionCondition
{
    private List<string>? _exact;
    private VersionRange? _range;

    /// <summary>One or more exact package version strings, including non-SemVer values.</summary>
    [JsonPropertyName("Exact")]
    public List<string>? Exact
    {
        get => _exact;
        set
        {
            ExactSpecified = true;
            _exact = value;
        }
    }

    /// <summary>A semantic-version range.</summary>
    [JsonPropertyName("Range")]
    public VersionRange? Range
    {
        get => _range;
        set
        {
            RangeSpecified = true;
            _range = value;
        }
    }

    /// <summary>Selects exact-version matching and clears the range mode.</summary>
    public void UseExact(List<string> exact)
    {
        ArgumentNullException.ThrowIfNull(exact);
        ExactSpecified = true;
        _exact = exact;
        RangeSpecified = false;
        _range = null;
    }

    /// <summary>Selects semantic-range matching and clears the exact mode.</summary>
    public void UseRange(VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        ExactSpecified = false;
        _exact = null;
        RangeSpecified = true;
        _range = range;
    }

    [JsonIgnore]
    internal bool ExactSpecified { get; private set; }

    [JsonIgnore]
    internal bool RangeSpecified { get; private set; }
}

/// <summary>Exactly one package-identifier matching mode.</summary>
public sealed class PackageIdentifierCondition
{
    private List<string>? _exact;
    private List<string>? _patterns;

    /// <summary>One or more validated stable package identifiers.</summary>
    [JsonPropertyName("Exact")]
    public List<string>? Exact
    {
        get => _exact;
        set
        {
            ExactSpecified = true;
            _exact = value;
        }
    }

    /// <summary>Explicit wildcard patterns that may authorize multiple package identifiers.</summary>
    [JsonPropertyName("Patterns")]
    public List<string>? Patterns
    {
        get => _patterns;
        set
        {
            PatternsSpecified = true;
            _patterns = value;
        }
    }

    /// <summary>Selects exact-identifier matching and clears the pattern mode.</summary>
    public void UseExact(List<string> exact)
    {
        ArgumentNullException.ThrowIfNull(exact);
        ExactSpecified = true;
        _exact = exact;
        PatternsSpecified = false;
        _patterns = null;
    }

    /// <summary>Selects pattern matching and clears the exact mode.</summary>
    public void UsePatterns(List<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ExactSpecified = false;
        _exact = null;
        PatternsSpecified = true;
        _patterns = patterns;
    }

    [JsonIgnore]
    internal bool ExactSpecified { get; private set; }

    [JsonIgnore]
    internal bool PatternsSpecified { get; private set; }
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

/// <summary>Additional safety limits applied after an Allow rule matches.</summary>
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
        SourceNames = [.. value.SourceNames],
        PackageIdentifiers = PackageIdentifiers(value.PackageIdentifiers),
        Version = Version(value.Version),
        Scopes = [.. value.Scopes],
        Architectures = [.. value.Architectures],
        ExecutionElevation = [.. value.ExecutionElevation],
        Interactive = value.Interactive,
        SkipHashCheck = value.SkipHashCheck,
        PreRelease = value.PreRelease,
        HasCustomParameters = value.HasCustomParameters,
        HasCustomInstallLocation = value.HasCustomInstallLocation,
        HasPrePostCommands = value.HasPrePostCommands,
        HasKillBeforeOperation = value.HasKillBeforeOperation,
        HasUninstallPrevious = value.HasUninstallPrevious,
    };

    private static PackageIdentifierCondition? PackageIdentifiers(PackageIdentifierCondition? value)
    {
        if (value?.Exact is { } exact)
        {
            return new PackageIdentifierCondition { Exact = [.. exact] };
        }
        if (value?.Patterns is { } patterns)
        {
            return new PackageIdentifierCondition { Patterns = [.. patterns] };
        }
        return null;
    }

    private static VersionCondition? Version(VersionCondition? value)
    {
        if (value?.Exact is { } exact)
        {
            return new VersionCondition { Exact = [.. exact] };
        }
        if (value?.Range is { } range)
        {
            return new VersionCondition
            {
                Range = new VersionRange
                {
                    MinVersion = range.MinVersion,
                    MaxVersion = range.MaxVersion,
                    IncludePrerelease = range.IncludePrerelease,
                },
            };
        }
        return null;
    }

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