#![doc = include_str!("../README.md")]
#![allow(clippy::nonminimal_bool, reason = "checks are stated positively, as in `!(a < b)`")]

mod document;
mod evaluate;
mod json;
mod reading;
mod request;
mod text;
mod timestamp;
mod unicode_tables;
mod values;
mod version;

// `--all-targets` builds a library test target despite `test = false`,
// where the dev-dependencies, which serve the integration tests only, would otherwise be unused.
#[cfg(test)]
use {proptest as _, rstest as _, serde_json as _};

pub use document::{ErrorCode, Policy, ValidationError, parse_policy};
pub use evaluate::{Check, Decision, DecisionRule, Outcome, evaluate};
pub use request::{
    CustomInstallLocation, CustomParameters, InstallerArguments, KillBeforeOperation, OptIns, Package, PrePostCommands,
    Request, Source,
};
pub use timestamp::{InvalidTimestamp, Timestamp};
pub use values::{Architecture, Elevation, InstallerArgumentsMode, Manager, Operation, OptIn, Scope};
