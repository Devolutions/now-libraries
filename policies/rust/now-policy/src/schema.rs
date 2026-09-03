//! Schema generation and parsing helpers for policy documents.

use schemars::generate::SchemaSettings;

use crate::{PolicyDocument, PolicyDraftDocument};

/// Get the generated policy schema as a JSON value.
pub fn policy_schema_json() -> serde_json::Value {
    let schema = SchemaSettings::draft07()
        .into_generator()
        .into_root_schema_for::<PolicyDocument>();
    serde_json::to_value(&schema).expect("BUG: schema serialization failed")
}

/// Get the generated editable policy draft schema as a JSON value.
pub fn policy_draft_schema_json() -> serde_json::Value {
    let schema = SchemaSettings::draft07()
        .into_generator()
        .into_root_schema_for::<PolicyDraftDocument>();
    serde_json::to_value(&schema).expect("BUG: schema serialization failed")
}

/// Validate a policy document by deserializing from a JSON value.
pub fn parse_policy(value: serde_json::Value) -> Result<PolicyDocument, String> {
    serde_json::from_value(value).map_err(|e| e.to_string())
}

/// Validate a policy document by deserializing from JSON text.
pub fn parse_policy_json(text: &str) -> Result<PolicyDocument, String> {
    serde_json::from_str(text).map_err(|e| e.to_string())
}
