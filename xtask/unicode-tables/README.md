# unicode-tables

Generates the Unicode tables that the policy evaluators use from the published UCD files.

The tables have a language-neutral shape, sorted code point pairs and sorted inclusive ranges, so the generator can emit them for another language.

This crate is outside the root workspace, so `cargo build --workspace` never compiles it or its `sha2` dependency.
Run it from the repository root with:

```shell
cargo xtask unicode tables
```

The task downloads the data files with `curl` into `target/ucd-16.0.0` unless they are already there, then rewrites `policies/rust/now-policy-eval/src/unicode_tables.rs`.
The generated header records the URL and SHA-256 of each input file.

To run the tool directly, pass the download directory and the Rust output file:

```shell
cargo run --manifest-path xtask/unicode-tables/Cargo.toml -- <download-dir> <rust-output>
```
