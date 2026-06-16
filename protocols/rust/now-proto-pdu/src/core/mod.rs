//! This module contains `NOW-PROTO` core types definitions.
//!
//! Note that these types are not intended to be used directly by the user, and not exported in the
//! public API.

mod buffer;
mod guid;
mod header;
mod number;
mod status;
mod string;

pub(crate) use buffer::NowVarBuf;
pub(crate) use guid::NowGuid;
pub(crate) use header::{NowHeader, NowMessageClass};
pub(crate) use number::VarU32;
pub(crate) use status::NowStatus;
// Only public-exported type is the status error, which should be available to the user for error
// handling.
pub use status::{NowProtoError, NowStatusError, NowStatusErrorKind};
pub(crate) use string::NowVarStr;
