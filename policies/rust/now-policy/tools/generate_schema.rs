//! Generates the JSON schemas for committed and editable policy documents.
//!
//! Usage: `cargo run -p now-policy --bin generate-now-policy-schema`

#![allow(clippy::print_stdout, reason = "this is a developer-facing CLI tool")]
#![allow(clippy::std_instead_of_core, unused_crate_dependencies)]

use std::path::Path;

use now_policy::schema::{policy_draft_schema_json, policy_schema_json};
use serde_json::Value;

fn main() {
    let crate_dir = Path::new(env!("CARGO_MANIFEST_DIR"));
    write_schema(
        &crate_dir.join("schema").join("devolutions.now-policy.schema.json"),
        policy_schema_json(),
    );
    write_schema(
        &crate_dir
            .join("schema")
            .join("devolutions.now-policy-draft.schema.json"),
        policy_draft_schema_json(),
    );
}

fn write_schema(path: &Path, schema: Value) {
    let json = serde_json::to_string_pretty(&schema).expect("BUG: schema serialization failed");
    std::fs::write(path, &json).unwrap_or_else(|e| panic!("failed to write {}: {e}", path.display()));
    println!("Wrote {}", path.display());
}
