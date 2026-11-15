//! Package operation execution models.
//!
//! Execution uses the shared [`PackageRequest`](crate::PackageRequest) request model.

use chrono::{DateTime, Utc};
use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

use super::api::{DecisionInfo, OperationDiagnostics, RequestSummary, ResponsePolicyInfo, ServerContext};
use super::enums::OperationStatus;
use super::event_channel::EventChannel;
use super::{ApiVersion, ExecutionResponseKind, GrantExecutionResponseKind, GrantResponseVersion, ResourceId};

/// Response returned after an execute request is evaluated and, when allowed, submitted.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "ExecutionResponse")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct ExecutionResponse {
    /// Response discriminator.
    pub response_kind: ExecutionResponseKind,

    /// Server-side API version used to construct the response.
    pub response_version: ApiVersion,

    /// Server context.
    pub server: ServerContext,

    /// Echoed request id.
    pub request_id: ResourceId,

    /// UTC timestamp when broker received the request (RFC 3339).
    pub received_at: DateTime<Utc>,

    /// UTC timestamp when broker completed evaluation/submission (RFC 3339).
    pub completed_at: DateTime<Utc>,

    /// Parsed request summary.
    pub request: RequestSummary,

    /// Policy decision details.
    pub decision: DecisionInfo,

    /// Summary of the policy used.
    pub policy: ResponsePolicyInfo,

    /// Submitted operation. Omitted when the decision is deny.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub operation: Option<OperationSubmission>,

    /// Optional diagnostics. Command preview is omitted unless explicitly requested.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub diagnostics: Option<OperationDiagnostics>,
}

/// Execution submission returned for allowed execute requests.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "OperationSubmission")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct OperationSubmission {
    /// Server-issued stable operation identifier.
    pub operation_id: ResourceId,

    /// Initial operation status.
    pub status: OperationStatus,

    /// UTC timestamp when the operation was accepted.
    pub submitted_at: DateTime<Utc>,

    /// Per-operation event channel carrying `NOW_BROKER` event frames (status
    /// change notifications and, when the execute request opted in via
    /// `CaptureOutput`, stdout/stderr data). Present whenever the broker
    /// supports event channels; absent otherwise.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub event_channel: Option<EventChannel>,
}

/// Basis on which the broker executed an operation.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "GrantAuthorizationKind")]
pub enum GrantAuthorizationKind {
    /// The operation was authorized by a remote grant, not by local policy.
    RemoteGrant,
}

/// Authorization under which a grant-executed operation ran.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "GrantAuthorization")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct GrantAuthorization {
    /// Authorization kind.
    pub kind: GrantAuthorizationKind,
}

/// Response returned after a remotely granted operation is submitted.
///
/// It carries no policy decision, rule, diagnostics, grant value or captured output.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "GrantExecutionResponse")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct GrantExecutionResponse {
    /// Response discriminator.
    pub response_kind: GrantExecutionResponseKind,

    /// Response version, exactly `1.0`.
    pub response_version: GrantResponseVersion,

    /// Server context.
    #[serde(deserialize_with = "bounded_server_context")]
    pub server: ServerContext,

    /// Echoed request id, a lowercase hyphenated UUID.
    #[serde(deserialize_with = "uuid_resource_id")]
    #[schemars(regex(pattern = r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"))]
    pub request_id: ResourceId,

    /// UTC timestamp when broker received the request (RFC 3339).
    #[serde(deserialize_with = "utc_timestamp")]
    pub received_at: DateTime<Utc>,

    /// UTC timestamp when broker completed submission (RFC 3339).
    #[serde(deserialize_with = "utc_timestamp")]
    pub completed_at: DateTime<Utc>,

    /// Parsed request summary.
    #[serde(deserialize_with = "object_request_summary")]
    pub request: RequestSummary,

    /// Authorization under which the operation ran.
    pub authorization: GrantAuthorization,

    /// Submitted operation.
    pub operation: GrantOperation,
}

/// Operation submitted by a grant execution.
///
/// Carries the same identifier, status, time and event channel as [`OperationSubmission`].
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "GrantOperation")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct GrantOperation {
    /// Broker-issued stable operation identifier.
    pub operation_id: ResourceId,

    /// Initial operation status.
    pub status: OperationStatus,

    /// UTC timestamp when the operation was accepted.
    #[serde(deserialize_with = "utc_timestamp")]
    pub submitted_at: DateTime<Utc>,

    /// Per-operation event channel descriptor. Absent when the broker offers none.
    #[serde(
        default,
        skip_serializing_if = "Option::is_none",
        deserialize_with = "bounded_event_channel"
    )]
    pub event_channel: Option<EventChannel>,
}

impl From<GrantOperation> for OperationSubmission {
    fn from(operation: GrantOperation) -> Self {
        Self {
            operation_id: operation.operation_id,
            status: operation.status,
            submitted_at: operation.submitted_at,
            event_channel: operation.event_channel,
        }
    }
}

fn utc_timestamp<'de, D>(deserializer: D) -> Result<DateTime<Utc>, D::Error>
where
    D: serde::Deserializer<'de>,
{
    let value = String::deserialize(deserializer)?;
    if !(value.ends_with('Z') || value.ends_with("+00:00")) || value.as_bytes().get(10) != Some(&b'T') {
        return Err(serde::de::Error::custom("timestamp must be RFC 3339 UTC"));
    }
    DateTime::parse_from_rfc3339(&value)
        .map(|timestamp| timestamp.with_timezone(&Utc))
        .map_err(serde::de::Error::custom)
}

fn uuid_resource_id<'de, D>(deserializer: D) -> Result<ResourceId, D::Error>
where
    D: serde::Deserializer<'de>,
{
    let value = String::deserialize(deserializer)?;
    let groups: Vec<&str> = value.split('-').collect();
    let is_uuid = groups.len() == 5
        && groups.iter().zip([8, 4, 4, 4, 12]).all(|(group, len)| {
            group.len() == len && group.bytes().all(|byte| matches!(byte, b'0'..=b'9' | b'a'..=b'f'))
        });
    if is_uuid {
        Ok(ResourceId(value))
    } else {
        Err(serde::de::Error::custom(
            "RequestId must be a lowercase hyphenated UUID",
        ))
    }
}

// Lengths are counted in Unicode characters, as the contract specifies.
fn bounded_server_context<'de, D>(deserializer: D) -> Result<ServerContext, D::Error>
where
    D: serde::Deserializer<'de>,
{
    let server = ServerContext::deserialize(deserializer)?;
    if !(1..=128).contains(&server.server_version.chars().count()) {
        return Err(serde::de::Error::custom("ServerVersion must be 1 to 128 characters"));
    }
    Ok(server)
}

fn bounded_event_channel<'de, D>(deserializer: D) -> Result<Option<EventChannel>, D::Error>
where
    D: serde::Deserializer<'de>,
{
    let channel = Option::<EventChannel>::deserialize(deserializer)?;
    if let Some(channel) = &channel
        && !(1..=1024).contains(&channel.path.chars().count())
    {
        return Err(serde::de::Error::custom(
            "EventChannel.Path must be 1 to 1024 characters",
        ));
    }
    Ok(channel)
}

// A derived struct deserializer also accepts a JSON array, which all-optional `Request` would let through.
fn object_request_summary<'de, D>(deserializer: D) -> Result<RequestSummary, D::Error>
where
    D: serde::Deserializer<'de>,
{
    struct ObjectOnly;

    impl<'de> serde::de::Visitor<'de> for ObjectOnly {
        type Value = RequestSummary;

        fn expecting(&self, formatter: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
            formatter.write_str("a Request object")
        }

        fn visit_map<A: serde::de::MapAccess<'de>>(self, map: A) -> Result<Self::Value, A::Error> {
            RequestSummary::deserialize(serde::de::value::MapAccessDeserializer::new(map))
        }
    }

    deserializer.deserialize_map(ObjectOnly)
}
