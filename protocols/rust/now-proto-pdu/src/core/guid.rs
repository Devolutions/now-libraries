//! GUID type for NOW-PROTO

use alloc::string::ToString;
use core::fmt;
use ironrdp_core::{DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor};
use uuid::Uuid;

use crate::NowVarStr;

/// A GUID (Globally Unique Identifier) represented as a lowercase string
/// in the format "00112233-4455-6677-8899-aabbccddeeff"
///
/// NOW-PROTO: NOW_GUID (encoded as NOW_VARSTR)
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub(crate) struct NowGuid {
    inner: Uuid,
}

impl NowGuid {
    const NAME: &'static str = "NOW_GUID";

    /// Create a new GUID from a UUID
    pub(crate) fn new(uuid: Uuid) -> Self {
        Self { inner: uuid }
    }

    /// Create a new GUID from a string representation
    /// String must be in the format "00112233-4455-6677-8899-aabbccddeeff" (lowercase)
    fn from_str_with_validation(s: &str) -> Result<Self, uuid::Error> {
        let uuid = Uuid::try_parse(s)?;
        Ok(Self::new(uuid))
    }

    /// Get the UUID representation
    pub(crate) fn as_uuid(&self) -> Uuid {
        self.inner
    }
}

impl Default for NowGuid {
    fn default() -> Self {
        Self::new(Uuid::nil())
    }
}

impl fmt::Display for NowGuid {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        // UUID's hyphenated format is already lowercase, so we can use it directly
        write!(f, "{}", self.inner.hyphenated())
    }
}

impl IntoOwned for NowGuid {
    type Owned = NowGuid;

    fn into_owned(self) -> Self::Owned {
        self
    }
}

impl<'de> ironrdp_core::Decode<'de> for NowGuid {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let var_str = NowVarStr::decode(src)?;
        Self::from_str_with_validation(&var_str)
            .map_err(|_| ironrdp_core::invalid_field_err!("NOW_GUID", "invalid UUID format"))
    }
}

impl Encode for NowGuid {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        // Convert UUID to string - this creates an owned String
        let guid_str = self.inner.hyphenated().to_string();
        let var_str = NowVarStr::new(guid_str)?;
        var_str.encode(dst)
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        // Convert UUID to string for size calculation
        let guid_str = self.inner.hyphenated().to_string();
        NowVarStr::new(guid_str)
            .expect("Encoding a valid GUID should never fail")
            .size()
    }
}
