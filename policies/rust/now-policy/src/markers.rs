//! Marker types -- zero-size structs that serialize to a fixed string constant.

use schemars::{JsonSchema, Schema, SchemaGenerator, json_schema};
use serde::{Deserialize, Serialize};

macro_rules! fixed_string_marker {
    (
        $(#[$attr:meta])*
        $vis:vis struct $name:ident => $value:expr;
    ) => {
        $(#[$attr])*
        #[derive(Debug, Clone, Copy, PartialEq, Eq)]
        $vis struct $name;

        impl Serialize for $name {
            fn serialize<S: serde::Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
                serializer.serialize_str($value)
            }
        }

        impl<'de> Deserialize<'de> for $name {
            fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
                let value = String::deserialize(deserializer)?;
                if value == $value {
                    Ok(Self)
                } else {
                    Err(serde::de::Error::custom(format_args!(
                        "expected {:?}, got {:?}",
                        $value, value
                    )))
                }
            }
        }

        impl JsonSchema for $name {
            fn schema_name() -> std::borrow::Cow<'static, str> {
                stringify!($name).into()
            }

            fn json_schema(_gen: &mut SchemaGenerator) -> Schema {
                json_schema!({
                    "type": "string",
                    "enum": [$value],
                })
            }
        }
    };
}

fixed_string_marker! {
    /// Marker type for policy type: serializes to `"PackageBrokerPolicy"`.
    pub struct PackageBrokerPolicy => "PackageBrokerPolicy";
}

/// Schema URI for package policy documents.
pub const POLICY_SCHEMA_URI: &str = "https://devolutions.net/schemas/now-policy.schema.1.0.json";

/// Schema URI for editable package policy draft documents.
pub const POLICY_DRAFT_SCHEMA_URI: &str = "https://devolutions.net/schemas/now-policy-draft.schema.1.0.json";

fixed_string_marker! {
    /// Marker type for the policy `$schema` field.
    /// Serializes to the canonical policy schema URI.
    pub struct PolicySchemaUri => POLICY_SCHEMA_URI;
}

fixed_string_marker! {
    /// Marker type for the policy draft `$schema` field.
    /// Serializes to the canonical policy draft schema URI.
    pub struct PolicyDraftSchemaUri => POLICY_DRAFT_SCHEMA_URI;
}
