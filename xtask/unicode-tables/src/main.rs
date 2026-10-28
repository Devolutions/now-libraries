//! Generates the Unicode tables that the policy evaluators use from the published UCD files.
//!
//! The tables have a language-neutral shape, sorted code point pairs and sorted inclusive ranges, so the generator can emit them for another language.
//!
//! Usage: `unicode-tables <download-dir> <rust-output>`.

use core::error::Error;
use core::fmt::Write as _;
use std::io::Write as _;
use std::path::Path;
use std::process::Command;
use std::{fs, io};

use sha2::{Digest as _, Sha256};

const UCD_BASE_URL: &str = "https://www.unicode.org/Public/16.0.0/ucd/";

/// A published file and the SHA-256 it must have, so that a stale or altered cache cannot change the tables unnoticed.
struct PinnedFile {
    name: &'static str,
    sha256: &'static str,
}

const UNICODE_DATA: PinnedFile = PinnedFile {
    name: "UnicodeData.txt",
    sha256: "ff58e5823bd095166564a006e47d111130813dcf8bf234ef79fa51a870edb48f",
};
const PROP_LIST: PinnedFile = PinnedFile {
    name: "PropList.txt",
    sha256: "53d614508e2a0b2305a8aa21cd60d993de9326cdf65993660dfcce4503548583",
};
const DERIVED_CORE_PROPERTIES: PinnedFile = PinnedFile {
    name: "DerivedCoreProperties.txt",
    sha256: "39d35161f2954497f69e08bdb9e701493f476a3d30222de20028feda36c1dabd",
};

struct Input {
    url: String,
    sha256: String,
    text: String,
}

struct Tables {
    simple_uppercase: Vec<(u32, u32)>,
    white_space: Vec<(u32, u32)>,
    default_ignorable: Vec<(u32, u32)>,
}

fn main() -> Result<(), Box<dyn Error>> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let [download_dir, rust_output] = args.as_slice() else {
        return Err("usage: unicode-tables <download-dir> <rust-output>".into());
    };
    let download_dir = Path::new(download_dir);
    let rust_output = Path::new(rust_output);

    fs::create_dir_all(download_dir)?;

    let unicode_data = fetch(download_dir, &UNICODE_DATA)?;
    let prop_list = fetch(download_dir, &PROP_LIST)?;
    let derived_core_properties = fetch(download_dir, &DERIVED_CORE_PROPERTIES)?;

    let tables = Tables {
        simple_uppercase: parse_simple_uppercase(&unicode_data.text)?,
        white_space: parse_property(&prop_list.text, "White_Space")?,
        default_ignorable: parse_property(&derived_core_properties.text, "Default_Ignorable_Code_Point")?,
    };

    let rust = emit_rust(&[&unicode_data, &prop_list, &derived_core_properties], &tables);
    fs::write(rust_output, rust)?;

    writeln!(io::stdout(), "Wrote {}", rust_output.display())?;

    Ok(())
}

fn fetch(download_dir: &Path, file: &PinnedFile) -> Result<Input, Box<dyn Error>> {
    let file_name = file.name;
    let url = format!("{UCD_BASE_URL}{file_name}");
    let path = download_dir.join(file_name);

    if !path.exists() {
        let status = Command::new("curl")
            .args(["--fail", "--silent", "--show-error", "--location", "--output"])
            .arg(&path)
            .arg(&url)
            .status()?;

        if !status.success() {
            return Err(format!("failed to download {url}").into());
        }
    }

    let bytes = fs::read(&path)?;
    let sha256 = Sha256::digest(&bytes).iter().fold(String::new(), |mut hex, byte| {
        let _ = write!(hex, "{byte:02x}");
        hex
    });
    if sha256 != file.sha256 {
        return Err(format!(
            "{} has SHA-256 {sha256} but Unicode 16.0.0 requires {}; delete it to download it again",
            path.display(),
            file.sha256,
        )
        .into());
    }

    let text = String::from_utf8(bytes).map_err(|_| format!("{file_name} is not UTF-8"))?;

    Ok(Input { url, sha256, text })
}

/// Reads the simple uppercase mapping, field 12 of `UnicodeData.txt`, as sorted (code point, uppercase) pairs.
fn parse_simple_uppercase(text: &str) -> Result<Vec<(u32, u32)>, Box<dyn Error>> {
    let mut pairs = Vec::new();

    for line in text.lines().filter(|line| !line.is_empty()) {
        let fields: Vec<&str> = line.split(';').collect();

        if fields.len() != 15 {
            return Err(format!("unexpected UnicodeData.txt line: {line}").into());
        }

        if fields[12].is_empty() {
            continue;
        }

        pairs.push((parse_code_point(fields[0])?, parse_code_point(fields[12])?));
    }

    pairs.sort_unstable();

    Ok(pairs)
}

/// Reads the code points that have `property` as sorted, merged, inclusive ranges.
fn parse_property(text: &str, property: &str) -> Result<Vec<(u32, u32)>, Box<dyn Error>> {
    let mut ranges = Vec::new();

    for line in text.lines() {
        let data = line.split('#').next().unwrap_or_default().trim();
        let Some((code_points, name)) = data.split_once(';') else {
            continue;
        };

        if name.trim() != property {
            continue;
        }

        let code_points = code_points.trim();
        let range = match code_points.split_once("..") {
            Some((first, last)) => (parse_code_point(first)?, parse_code_point(last)?),
            None => {
                let code_point = parse_code_point(code_points)?;
                (code_point, code_point)
            }
        };
        ranges.push(range);
    }

    if ranges.is_empty() {
        return Err(format!("no code point has the property {property}").into());
    }

    ranges.sort_unstable();

    let mut merged: Vec<(u32, u32)> = Vec::with_capacity(ranges.len());
    for (first, last) in ranges {
        match merged.last_mut() {
            Some(previous) if first <= previous.1 + 1 => previous.1 = previous.1.max(last),
            _ => merged.push((first, last)),
        }
    }

    Ok(merged)
}

fn parse_code_point(text: &str) -> Result<u32, Box<dyn Error>> {
    u32::from_str_radix(text.trim(), 16).map_err(|_| format!("invalid code point: {text}").into())
}

fn emit_rust(inputs: &[&Input], tables: &Tables) -> String {
    let mut out = String::new();

    out.push_str("// Unicode 16.0.0 tables generated by `cargo xtask unicode tables`; do not edit.\n");
    out.push_str("//\n");
    out.push_str("// Inputs:\n");
    for input in inputs {
        let _ = writeln!(out, "// - {}", input.url);
        let _ = writeln!(out, "//   SHA-256: {}", input.sha256);
    }

    emit_rust_table(
        &mut out,
        "Simple uppercase mappings from `UnicodeData.txt`, as (code point, uppercase) pairs sorted by code point.",
        "SIMPLE_UPPERCASE",
        &tables.simple_uppercase,
    );
    emit_rust_table(
        &mut out,
        "Code points with the `White_Space` property from `PropList.txt`, as sorted inclusive ranges.",
        "WHITE_SPACE",
        &tables.white_space,
    );
    emit_rust_table(
        &mut out,
        "Code points with the `Default_Ignorable_Code_Point` property from `DerivedCoreProperties.txt`, as sorted inclusive ranges.",
        "DEFAULT_IGNORABLE_CODE_POINT",
        &tables.default_ignorable,
    );

    out
}

fn emit_rust_table(out: &mut String, doc: &str, name: &str, entries: &[(u32, u32)]) {
    let _ = writeln!(out);
    let _ = writeln!(out, "/// {doc}");
    let _ = writeln!(out, "pub(crate) static {name}: &[(u32, u32)] = &[");
    for (first, second) in entries {
        let _ = writeln!(out, "    (0x{first:04X}, 0x{second:04X}),");
    }
    let _ = writeln!(out, "];");
}
