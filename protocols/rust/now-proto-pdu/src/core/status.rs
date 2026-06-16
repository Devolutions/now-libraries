use alloc::borrow::Cow;
use alloc::fmt;
use core::ops::Deref;

use bitflags::bitflags;
use ironrdp_core::{
    ensure_fixed_part_size, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor,
};

use crate::NowVarStr;

bitflags! {
    /// NOW-PROTO: NOW_STATUS flags field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    struct NowStatusFlags: u16 {
        /// This flag set for all error statuses. If flag is not set, operation was successful.
        ///
        /// NOW-PROTO: NOW_STATUS_ERROR
        const ERROR = 0x0001;
        /// Set if `errorMessage` contains optional error message.
        ///
        /// NOW-PROTO: NOW_STATUS_ERROR_MESSAGE
        const ERROR_MESSAGE = 0x0002;
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
struct RawNowStatusKind(pub u16);

impl RawNowStatusKind {
    const GENERIC: Self = Self(0x0000);
    const NOW: Self = Self(0x0001);
    const WINAPI: Self = Self(0x0002);
    const UNIX: Self = Self(0x0003);
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
struct RawNowProtoError(pub u32);

impl RawNowProtoError {
    const IN_USE: Self = Self(0x0001);
    const INVALID_REQUEST: Self = Self(0x0002);
    const ABORTED: Self = Self(0x0003);
    const NOT_FOUND: Self = Self(0x0004);
    const ACCESS_DENIED: Self = Self(0x0005);
    const INTERNAL: Self = Self(0x0006);
    const NOT_IMPLEMENTED: Self = Self(0x0007);
    const PROTOCOL_VERSION: Self = Self(0x0008);
}

/// `code` field value of `NOW_STATUS` message if `kind` is `NOW_STATUS_ERROR_KIND_NOW`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NowProtoError {
    /// Resource (e.g. exec session id is already in use).
    ///
    /// NOW-PROTO: NOW_CODE_IN_USE
    InUse,
    /// Sent request is invalid (e.g. invalid exec request params).
    ///
    /// NOW-PROTO: NOW_CODE_INVALID_REQUEST
    InvalidRequest,
    /// Operation has been aborted on the server side.
    ///
    /// NOW-PROTO: NOW_CODE_ABORTED
    Aborted,
    /// Resource not found.
    ///
    /// NOW-PROTO: NOW_CODE_NOT_FOUND
    NotFound,
    /// Resource can't be accessed.
    ///
    /// NOW-PROTO: NOW_CODE_ACCESS_DENIED
    AccessDenied,
    /// Internal error.
    ///
    /// NOW-PROTO: NOW_CODE_INTERNAL
    Internal,
    /// Operation is not implemented on current platform.
    ///
    /// NOW-PROTO: NOW_CODE_NOT_IMPLEMENTED
    NotImplemented,
    /// Incompatible protocol versions.
    ///
    /// NOW-PROTO: NOW_CODE_PROTOCOL_VERSION
    ProtocolVersion,
    /// Other error code.
    Other(u32),
}

impl From<RawNowProtoError> for NowProtoError {
    fn from(code: RawNowProtoError) -> Self {
        match code {
            RawNowProtoError::IN_USE => NowProtoError::InUse,
            RawNowProtoError::INVALID_REQUEST => NowProtoError::InvalidRequest,
            RawNowProtoError::ABORTED => NowProtoError::Aborted,
            RawNowProtoError::NOT_FOUND => NowProtoError::NotFound,
            RawNowProtoError::ACCESS_DENIED => NowProtoError::AccessDenied,
            RawNowProtoError::INTERNAL => NowProtoError::Internal,
            RawNowProtoError::NOT_IMPLEMENTED => NowProtoError::NotImplemented,
            RawNowProtoError::PROTOCOL_VERSION => NowProtoError::ProtocolVersion,
            RawNowProtoError(code) => NowProtoError::Other(code),
        }
    }
}

impl From<NowProtoError> for RawNowProtoError {
    fn from(err: NowProtoError) -> Self {
        match err {
            NowProtoError::InUse => RawNowProtoError::IN_USE,
            NowProtoError::InvalidRequest => RawNowProtoError::INVALID_REQUEST,
            NowProtoError::Aborted => RawNowProtoError::ABORTED,
            NowProtoError::NotFound => RawNowProtoError::NOT_FOUND,
            NowProtoError::AccessDenied => RawNowProtoError::ACCESS_DENIED,
            NowProtoError::Internal => RawNowProtoError::INTERNAL,
            NowProtoError::NotImplemented => RawNowProtoError::NOT_IMPLEMENTED,
            NowProtoError::ProtocolVersion => RawNowProtoError::PROTOCOL_VERSION,
            NowProtoError::Other(code) => RawNowProtoError(code),
        }
    }
}

impl fmt::Display for NowProtoError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            NowProtoError::InUse => write!(f, "resource is already in use"),
            NowProtoError::InvalidRequest => write!(f, "invalid request"),
            NowProtoError::Aborted => write!(f, "operation has been aborted"),
            NowProtoError::NotFound => write!(f, "resource not found"),
            NowProtoError::AccessDenied => write!(f, "access denied"),
            NowProtoError::Internal => write!(f, "internal error"),
            NowProtoError::NotImplemented => write!(f, "operation is not implemented"),
            NowProtoError::ProtocolVersion => write!(f, "incompatible protocol versions"),
            NowProtoError::Other(code) => write!(f, "unknown error code {}", code),
        }
    }
}

/// Mapped NOW_STATUS error kinds with their respective error codes inside enum variants represented
/// as rust enum for convenient error handling.
///
/// Converted internally by the library to/from `kind` and `code` fields of `NOW_STATUS` message.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NowStatusErrorKind {
    /// `code` value is undefined and could be ignored.
    ///
    /// NOW-PROTO: NOW_STATUS_ERROR_KIND_GENERIC
    Generic(u32),
    /// `code` contains NowProto-defined error code (see `NOW_STATUS_ERROR_KIND_NOW`).
    ///
    /// NOW-PROTO: NOW_STATUS_ERROR_KIND_NOW
    Now(NowProtoError),
    /// `code` field contains Windows error code.
    ///
    /// NOW-PROTO: NOW_STATUS_ERROR_KIND_WINAPI
    WinApi(u32),
    /// `code` field contains Unix error code.
    ///
    /// NOW-PROTO: NOW_STATUS_ERROR_KIND_UNIX
    Unix(u32),
    /// Unknown error kind.
    Unknown { kind: u16, code: u32 },
}

impl From<NowProtoError> for NowStatusErrorKind {
    fn from(err: NowProtoError) -> Self {
        NowStatusErrorKind::Now(err)
    }
}

impl fmt::Display for NowStatusErrorKind {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match self {
            NowStatusErrorKind::Generic(code) => write!(f, "generic error code {}", code),
            NowStatusErrorKind::Now(error) => {
                write!(f, "NOW-proto error: ")?;
                error.fmt(f)
            }
            NowStatusErrorKind::WinApi(code) => write!(f, "WinAPI error code {}", code),
            NowStatusErrorKind::Unix(code) => write!(f, "Unix error code {}", code),
            NowStatusErrorKind::Unknown { kind, code } => {
                write!(f, "unknown error: kind({}), code({})", kind, code)
            }
        }
    }
}

impl NowStatusErrorKind {
    fn status_kind(&self) -> RawNowStatusKind {
        match self {
            NowStatusErrorKind::Generic(_) => RawNowStatusKind::GENERIC,
            NowStatusErrorKind::Now(_) => RawNowStatusKind::NOW,
            NowStatusErrorKind::WinApi(_) => RawNowStatusKind::WINAPI,
            NowStatusErrorKind::Unix(_) => RawNowStatusKind::UNIX,
            NowStatusErrorKind::Unknown { kind, .. } => RawNowStatusKind(*kind),
        }
    }

    fn status_code(&self) -> u32 {
        match self {
            NowStatusErrorKind::Generic(code) => *code,
            NowStatusErrorKind::Now(error) => RawNowProtoError::from(*error).0,
            NowStatusErrorKind::WinApi(code) => *code,
            NowStatusErrorKind::Unix(code) => *code,
            NowStatusErrorKind::Unknown { code, .. } => *code,
        }
    }

    fn from_parts(kind: u16, code: u32) -> Self {
        match RawNowStatusKind(kind) {
            RawNowStatusKind::GENERIC => NowStatusErrorKind::Generic(code),
            RawNowStatusKind::NOW => NowStatusErrorKind::Now(RawNowProtoError(code).into()),
            RawNowStatusKind::WINAPI => NowStatusErrorKind::WinApi(code),
            RawNowStatusKind::UNIX => NowStatusErrorKind::Unix(code),
            _ => NowStatusErrorKind::Unknown { kind, code },
        }
    }
}

/// Wrapper type around NOW_STATUS errors. Provides rust-friendly interface for error handling.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowStatusError {
    kind: NowStatusErrorKind,
    message: NowVarStr<'static>,
}

impl NowStatusError {
    pub fn new_generic(code: u32) -> Self {
        Self {
            kind: NowStatusErrorKind::Generic(code),
            message: Default::default(),
        }
    }

    pub fn new_proto(error: NowProtoError) -> Self {
        Self {
            kind: NowStatusErrorKind::Now(error),
            message: Default::default(),
        }
    }

    pub fn new_winapi(code: u32) -> Self {
        Self {
            kind: NowStatusErrorKind::WinApi(code),
            message: Default::default(),
        }
    }

    pub fn new_unix(code: u32) -> Self {
        Self {
            kind: NowStatusErrorKind::Unix(code),
            message: Default::default(),
        }
    }

    pub fn kind(&self) -> NowStatusErrorKind {
        self.kind
    }

    pub fn message(&self) -> &str {
        &self.message
    }

    /// Attach optional message to NOW_STATUS error.
    pub fn with_message(self, message: impl Into<Cow<'static, str>>) -> EncodeResult<Self> {
        Ok(Self {
            kind: self.kind,
            message: NowVarStr::new(message)?,
        })
    }
}

impl core::fmt::Display for NowStatusError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        fmt::Display::fmt(&self.kind, f)?;

        // Write optional message if provided.
        if !self.message.is_empty() {
            write!(f, " ({})", self.message.deref())?;
        }

        Ok(())
    }
}

impl From<NowStatusErrorKind> for NowStatusError {
    fn from(kind: NowStatusErrorKind) -> Self {
        Self {
            kind,
            message: Default::default(),
        }
    }
}

impl From<NowProtoError> for NowStatusError {
    fn from(err: NowProtoError) -> Self {
        NowStatusErrorKind::from(err).into()
    }
}

impl core::error::Error for NowStatusError {}

/// Channel or session operation status.
///
/// NOW-PROTO: NOW_STATUS
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct NowStatus<'a> {
    flags: NowStatusFlags,
    kind: RawNowStatusKind,
    code: u32,
    message: NowVarStr<'a>,
}

impl IntoOwned for NowStatus<'_> {
    type Owned = NowStatus<'static>;

    fn into_owned(self) -> Self::Owned {
        Self::Owned {
            flags: self.flags,
            kind: self.kind,
            code: self.code,
            message: self.message.into_owned(),
        }
    }
}

impl NowStatus<'_> {
    const NAME: &'static str = "NOW_STATUS";
    const FIXED_PART_SIZE: usize = 8;

    /// Create a new success status.
    pub(crate) fn new_success() -> Self {
        Self {
            flags: NowStatusFlags::empty(),
            kind: RawNowStatusKind::GENERIC,
            code: 0,
            message: Default::default(),
        }
    }

    /// Create a new status with error.
    pub(crate) fn new_error(error: impl Into<NowStatusError>) -> Self {
        let error: NowStatusError = error.into();

        let flags = if error.message.is_empty() {
            NowStatusFlags::ERROR
        } else {
            NowStatusFlags::ERROR | NowStatusFlags::ERROR_MESSAGE
        };

        Self {
            flags,
            kind: error.kind.status_kind(),
            code: error.kind.status_code(),
            message: error.message,
        }
    }

    /// Convert status to result with 'static error.
    pub(crate) fn to_result(&self) -> Result<(), NowStatusError> {
        if !self.flags.contains(NowStatusFlags::ERROR) {
            return Ok(());
        }

        Err(NowStatusError {
            kind: NowStatusErrorKind::from_parts(self.kind.0, self.code),
            message: self.message.clone().into_owned(),
        })
    }
}

impl Encode for NowStatus<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        ensure_fixed_part_size!(in: dst);
        dst.write_u16(self.flags.bits());
        dst.write_u16(self.kind.0);
        dst.write_u32(self.code);

        self.message.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.message.size()
    }
}

impl<'de> Decode<'de> for NowStatus<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);
        let flags = NowStatusFlags::from_bits_retain(src.read_u16());
        let kind = RawNowStatusKind(src.read_u16());
        let code = src.read_u32();

        let message = NowVarStr::decode(src)?;

        Ok(NowStatus {
            flags,
            kind,
            code,
            message,
        })
    }
}
