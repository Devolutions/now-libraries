//! Active policy inspection endpoint models.

#![allow(
    unused_qualifications,
    reason = "schemars schema_with expansion triggers this lint for an unqualified function name"
)]

use now_policy::PolicyDocument;
use schemars::{JsonSchema, Schema, SchemaGenerator};
use serde::{Deserialize, Serialize};

use super::api::ServerContext;
use super::{ApiVersion, PolicyResponseKind};

/// Response body for `GET /v1/policy`.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyResponse")]
#[serde(rename_all = "PascalCase")]
pub struct PolicyResponse {
    /// Response discriminator.
    pub response_kind: PolicyResponseKind,

    /// Server-side API version used to construct the response.
    pub response_version: ApiVersion,

    /// Server context.
    pub server: ServerContext,

    /// Active parsed policy document.
    #[schemars(schema_with = "policy_document_schema")]
    pub policy: PolicyDocument,
}

fn policy_document_schema(_generator: &mut SchemaGenerator) -> Schema {
    schemars::json_schema!({
        "$ref": "#/components/schemas/PolicyDocument",
    })
}
