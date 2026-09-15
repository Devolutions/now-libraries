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
