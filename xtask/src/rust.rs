use crate::prelude::*;

pub fn fmt(sh: &Shell) -> anyhow::Result<()> {
    let _s = Section::new("RUST-FORMATTING");

    let output = cmd!(sh, "{CARGO} fmt --all -- --check").ignore_status().output()?;

    if !output.status.success() {
        anyhow::bail!("Bad formatting, please run 'cargo +stable fmt --all'");
    }

    println!("All good!");

    Ok(())
}

pub fn lints(sh: &Shell) -> anyhow::Result<()> {
    let _s = Section::new("RUST-LINTS");

    cmd!(
        sh,
        "{CARGO} clippy --workspace --all-targets --all-features --locked --keep-going -- -D warnings"
    )
    .run()?;

    println!("All good!");

    Ok(())
}

pub fn tests_compile(sh: &Shell) -> anyhow::Result<()> {
    let _s = Section::new("RUST-TESTS-COMPILE");

    cmd!(sh, "{CARGO} test --workspace --locked --no-run").run()?;

    println!("All good!");

    Ok(())
}

pub fn tests_run(sh: &Shell) -> anyhow::Result<()> {
    let _s = Section::new("RUST-TESTS-RUN");

    cmd!(sh, "{CARGO} test --workspace --all-features --locked").run()?;

    println!("All good!");

    Ok(())
}
