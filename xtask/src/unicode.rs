use crate::prelude::*;

const TOOL_MANIFEST: &str = "xtask/unicode-tables/Cargo.toml";
const DOWNLOAD_DIR: &str = "target/ucd-16.0.0";
const RUST_OUTPUT: &str = "policies/rust/now-policy-eval/src/unicode_tables.rs";

/// Runs the standalone `unicode-tables` tool, which is compiled only when this task runs.
///
/// The tool is outside the workspace so that its dependencies stay out of `xtask` and the workspace lock file.
pub fn tables(sh: &Shell) -> anyhow::Result<()> {
    let _s = Section::new("UNICODE-TABLES");

    cmd!(
        sh,
        "{CARGO} run --quiet --locked --manifest-path {TOOL_MANIFEST} -- {DOWNLOAD_DIR} {RUST_OUTPUT}"
    )
    .run()?;

    Ok(())
}
