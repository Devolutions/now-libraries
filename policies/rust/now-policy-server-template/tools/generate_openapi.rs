//! Generates the OpenAPI 3.1 description of the NOW package broker API.

#![allow(
    clippy::print_stdout,
    unused_crate_dependencies,
    reason = "this is a developer-facing CLI tool"
)]

use std::path::Path;

fn main() {
    let crate_dir = Path::new(env!("CARGO_MANIFEST_DIR"));
    let policy_crates_dir = crate_dir
        .parent()
        .expect("BUG: now-policy-server-template should live under policies/rust");
    let out_path = policy_crates_dir
        .join("now-policy-api")
        .join("openapi")
        .join("now-policy-api.yaml");

    let yaml = serde_yaml::to_string(&now_policy_server_template::server::openapi())
        .expect("BUG: OpenAPI serialization failed");

    std::fs::create_dir_all(out_path.parent().expect("BUG: OpenAPI output should have a parent"))
        .unwrap_or_else(|e| panic!("failed to create {}: {e}", out_path.display()));
    std::fs::write(&out_path, &yaml).unwrap_or_else(|e| panic!("failed to write {}: {e}", out_path.display()));

    println!("Wrote {}", out_path.display());
}
