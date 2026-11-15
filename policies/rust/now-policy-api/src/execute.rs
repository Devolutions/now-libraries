//! Package operation execution models.
//!
//! Execution uses the shared [`PackageRequest`](crate::PackageRequest) request model.

use core::marker::PhantomData;

use chrono::{DateTime, Utc};
use schemars::JsonSchema;
use serde::de::value::MapAccessDeserializer;
use serde::de::{MapAccess, Visitor};
use serde::{Deserialize, Serialize};

use super::api::{DecisionInfo, OperationDiagnostics, RequestSummary, ResponsePolicyInfo, ServerContext};
use super::enums::OperationStatus;
use super::event_channel::EventChannel;
use super::{
    ApiVersion, ExecutionResponseKind, GrantExecutionResponseKind, GrantResponseVersion, ModelValidationError,
    PackageIdentifier, ResourceId,
};

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
#[serde(remote = "Self")]
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
    #[serde(deserialize_with = "object")]
    pub request: RequestSummary,

    /// Authorization under which the operation ran.
    #[serde(deserialize_with = "object")]
    pub authorization: GrantAuthorization,

    /// Submitted operation.
    #[serde(deserialize_with = "object")]
    pub operation: GrantOperation,
}

impl GrantExecutionResponse {
    /// Checks the constraints that the public field types do not enforce on construction.
    pub fn validate(&self) -> Result<(), ModelValidationError> {
        if !is_lowercase_uuid(&self.request_id) {
            return Err(invalid("RequestId", "must be a lowercase hyphenated UUID"));
        }
        check_server_version(&self.server.server_version)?;
        ResourceId::parse(&self.operation.operation_id)?;
        if let Some(package_id) = &self.request.package_id {
            PackageIdentifier::parse(package_id)?;
        }
        if let Some(channel) = &self.operation.event_channel {
            check_event_channel_path(&channel.path)?;
        }
        Ok(())
    }
}

impl Serialize for GrantExecutionResponse {
    fn serialize<S: serde::Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        self.validate().map_err(serde::ser::Error::custom)?;
        Self::serialize(self, serializer)
    }
}

impl<'de> Deserialize<'de> for GrantExecutionResponse {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        struct ResponseObject;

        impl<'de> Visitor<'de> for ResponseObject {
            type Value = GrantExecutionResponse;

            fn expecting(&self, formatter: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
                formatter.write_str("a GrantExecutionResponse object")
            }

            fn visit_map<A: MapAccess<'de>>(self, map: A) -> Result<Self::Value, A::Error> {
                GrantExecutionResponse::deserialize(MapAccessDeserializer::new(map))
            }
        }

        deserializer.deserialize_map(ResponseObject)
    }
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

fn is_lowercase_uuid(value: &str) -> bool {
    let groups: Vec<&str> = value.split('-').collect();
    groups.len() == 5
        && groups.iter().zip([8, 4, 4, 4, 12]).all(|(group, len)| {
            group.len() == len && group.bytes().all(|byte| matches!(byte, b'0'..=b'9' | b'a'..=b'f'))
        })
}

fn invalid(type_name: &'static str, reason: &str) -> ModelValidationError {
    ModelValidationError::Invalid {
        type_name,
        reason: reason.to_owned(),
    }
}

// Lengths are counted in Unicode characters, as the contract specifies.
fn check_server_version(value: &str) -> Result<(), ModelValidationError> {
    if (1..=128).contains(&value.chars().count()) {
        Ok(())
    } else {
        Err(invalid("ServerVersion", "must be 1 to 128 characters"))
    }
}

fn check_event_channel_path(value: &str) -> Result<(), ModelValidationError> {
    if (1..=1024).contains(&value.chars().count()) {
        Ok(())
    } else {
        Err(invalid("EventChannel.Path", "must be 1 to 1024 characters"))
    }
}

fn uuid_resource_id<'de, D>(deserializer: D) -> Result<ResourceId, D::Error>
where
    D: serde::Deserializer<'de>,
{
    let value = String::deserialize(deserializer)?;
    if is_lowercase_uuid(&value) {
        Ok(ResourceId(value))
    } else {
        Err(serde::de::Error::custom(
            "RequestId must be a lowercase hyphenated UUID",
        ))
    }
}

fn bounded_server_context<'de, D>(deserializer: D) -> Result<ServerContext, D::Error>
where
    D: serde::Deserializer<'de>,
{
    let server: ServerContext = object(deserializer)?;
    check_server_version(&server.server_version).map_err(serde::de::Error::custom)?;
    Ok(server)
}

fn bounded_event_channel<'de, D>(deserializer: D) -> Result<Option<EventChannel>, D::Error>
where
    D: serde::Deserializer<'de>,
{
    struct OptionalObject;

    impl<'de> Visitor<'de> for OptionalObject {
        type Value = Option<EventChannel>;

        fn expecting(&self, formatter: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
            formatter.write_str("an EventChannel object or null")
        }

        fn visit_none<E: serde::de::Error>(self) -> Result<Self::Value, E> {
            Ok(None)
        }

        fn visit_unit<E: serde::de::Error>(self) -> Result<Self::Value, E> {
            Ok(None)
        }

        fn visit_some<D: serde::Deserializer<'de>>(self, deserializer: D) -> Result<Self::Value, D::Error> {
            object(deserializer).map(Some)
        }
    }

    let channel = deserializer.deserialize_option(OptionalObject)?;
    if let Some(channel) = &channel {
        check_event_channel_path(&channel.path).map_err(serde::de::Error::custom)?;
    }
    Ok(channel)
}

// A derived struct deserializer also accepts a JSON array, so each grant response object is read as a map only.
fn object<'de, D, T>(deserializer: D) -> Result<T, D::Error>
where
    D: serde::Deserializer<'de>,
    T: Deserialize<'de>,
{
    struct ObjectOnly<T>(PhantomData<T>);

    impl<'de, T: Deserialize<'de>> Visitor<'de> for ObjectOnly<T> {
        type Value = T;

        fn expecting(&self, formatter: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
            formatter.write_str("a JSON object")
        }

        fn visit_map<A: MapAccess<'de>>(self, map: A) -> Result<Self::Value, A::Error> {
            T::deserialize(MapAccessDeserializer::new(map))
        }
    }

    deserializer.deserialize_map(ObjectOnly(PhantomData))
}

#[cfg(test)]
mod tests {
    use serde_json::{Value, json};

    use super::*;

    fn valid() -> Value {
        json!({
            "ResponseKind": "GrantExecutionResponse",
            "ResponseVersion": "1.0",
            "Server": { "ServerVersion": "1.2.3", "Transport": "HttpNamedPipe" },
            "RequestId": "0b3e6f0e-5d58-4a0e-9d2c-1f4f1c6c8a11",
            "ReceivedAt": "2026-11-15T05:07:42Z",
            "CompletedAt": "2026-11-15T05:07:43Z",
            "Request": { "Manager": "Winget", "Source": "winget", "PackageId": "Microsoft.VisualStudioCode", "Operation": "Install" },
            "Authorization": { "Kind": "RemoteGrant" },
            "Operation": {
                "OperationId": "op-7f3a",
                "Status": "Running",
                "SubmittedAt": "2026-11-15T05:07:42Z",
                "EventChannel": { "Kind": "LocalPipe", "Path": "events" }
            }
        })
    }

    // oracle: contract - each grant response member is a JSON object; a positional array of the same values is not.
    #[test]
    fn objects_written_as_full_arrays_are_rejected() {
        let mut cases = Vec::new();
        for pointer in [
            "/Server",
            "/Request",
            "/Authorization",
            "/Operation",
            "/Operation/EventChannel",
        ] {
            let mut value = valid();
            let target = value.pointer_mut(pointer).expect("valid response has the member");
            *target = Value::Array(
                target
                    .as_object()
                    .expect("member is an object")
                    .values()
                    .cloned()
                    .collect(),
            );
            cases.push((pointer, value));
        }
        let root = valid();
        cases.push((
            "/",
            Value::Array(root.as_object().expect("root is an object").values().cloned().collect()),
        ));

        for (pointer, value) in cases {
            assert!(
                serde_json::from_value::<GrantExecutionResponse>(value).is_err(),
                "{pointer} accepted as an array"
            );
        }
    }

    // oracle: contract - RFC 3339 requires seconds.
    #[test]
    fn timestamps_without_seconds_are_rejected() {
        let mut value = valid();
        value["ReceivedAt"] = json!("2026-11-15T05:07Z");
        assert!(serde_json::from_value::<GrantExecutionResponse>(value).is_err());
    }

    #[test]
    fn serialization_rejects_values_that_deserialization_rejects() {
        let response: GrantExecutionResponse = serde_json::from_value(valid()).expect("valid response");
        type Mutation = fn(&mut GrantExecutionResponse);
        let invalid: [(&str, Mutation); 6] = [
            ("non-UUID RequestId", |response| {
                response.request_id = ResourceId::from("op-1")
            }),
            ("empty ServerVersion", |response| {
                response.server.server_version = String::new()
            }),
            ("129-character ServerVersion", |response| {
                response.server.server_version = "v".repeat(129)
            }),
            ("invalid OperationId", |response| {
                response.operation.operation_id = ResourceId::from("op 1")
            }),
            ("invalid PackageId", |response| {
                response.request.package_id = Some(PackageIdentifier::from("foo;bar".to_owned()))
            }),
            ("empty EventChannel.Path", |response| {
                response
                    .operation
                    .event_channel
                    .as_mut()
                    .expect("valid response has a channel")
                    .path = String::new();
            }),
        ];
        for (name, mutate) in invalid {
            let mut response = response.clone();
            mutate(&mut response);
            assert!(serde_json::to_string(&response).is_err(), "{name} serialized");
        }
    }
}
