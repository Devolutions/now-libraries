//! Tests of the public API, one module per contract area.

use {proptest as _, serde_json as _};

mod common;
mod documents;
mod evaluation;
mod malformed;
mod reading;
mod timestamps;
