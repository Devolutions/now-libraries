//! Generates the JSON schemas for committed and editable policy documents.
//!
//! Usage: `cargo run -p now-policy --bin generate-now-policy-schema`

#![allow(clippy::print_stdout, reason = "this is a developer-facing CLI tool")]
#![allow(clippy::std_instead_of_core, unused_crate_dependencies)]

use std::path::Path;

use now_policy::schema::{policy_draft_schema_json, policy_schema_json};
use now_policy::{POLICY_DRAFT_SCHEMA_URI, POLICY_SCHEMA_URI};
use serde_json::{Map, Value};

fn main() {
    let crate_dir = Path::new(env!("CARGO_MANIFEST_DIR"));
    write_schema(
        &crate_dir.join("schema").join("devolutions.now-policy.schema.json"),
        policy_schema_json(),
        POLICY_SCHEMA_URI,
    );
    write_schema(
        &crate_dir
            .join("schema")
            .join("devolutions.now-policy-draft.schema.json"),
        policy_draft_schema_json(),
        POLICY_DRAFT_SCHEMA_URI,
    );
}

fn write_schema(path: &Path, schema: Value, id: &str) {
    let json = serde_json::to_string_pretty(&with_id(schema, id)).expect("BUG: schema serialization failed");
    std::fs::write(path, &json).unwrap_or_else(|e| panic!("failed to write {}: {e}", path.display()));
    println!("Wrote {}", path.display());
}

fn with_id(schema: Value, id: &str) -> Value {
    let Value::Object(existing) = schema else {
        panic!("BUG: schema root is not an object");
    };

    let mut object = Map::new();
    object.insert("$id".to_owned(), Value::String(id.to_owned()));
    object.extend(existing);

    Value::Object(object)
}
