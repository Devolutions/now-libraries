//! Buffer types for NOW protocol.

use alloc::borrow::Cow;
use core::ops::Deref;

use ironrdp_core::{
    cast_length, ensure_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor,
    WriteCursor,
};

use crate::VarU32;

/// Buffer up to 2^30 bytes long (Length has compact variable length encoding).
///
/// NOW-PROTO: NOW_VARBUF
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub(crate) struct NowVarBuf<'a>(Cow<'a, [u8]>);

impl IntoOwned for NowVarBuf<'_> {
    type Owned = NowVarBuf<'static>;

    fn into_owned(self) -> Self::Owned {
        NowVarBuf(Cow::Owned(self.0.into_owned()))
    }
}

impl<'a> NowVarBuf<'a> {
    const NAME: &'static str = "NOW_VARBUF";

    /// Create a new `NowVarBuf` instance. Returns an error if the provided value is too large.
    pub(crate) fn new(value: impl Into<Cow<'a, [u8]>>) -> EncodeResult<Self> {
        let value = value.into();

        let _: u32 = value
            .len()
            .try_into()
            .ok()
            .and_then(|val| if val <= VarU32::MAX { Some(val) } else { None })
            .ok_or_else(|| invalid_field_err!("data", "too large buffer"))?;

        Ok(NowVarBuf(value))
    }
}

impl Encode for NowVarBuf<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let encoded_size = self.size();
        ensure_size!(in: dst, size: encoded_size);

        let len: u32 = self.0.len().try_into().expect("BUG: validated in constructor");

        VarU32::new(len)?.encode(dst)?;
        dst.write_slice(&self.0);

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        // <variable-length size> + <data bytes>
        // NOTE: Wrapping add will not overflow because the size is limited by VarU32::MAX
        VarU32::new(self.0.len().try_into().expect("buffer size always fits into u32"))
            .expect("buffer size is validated in constructor and should not overflow")
            .size()
            .wrapping_add(self.0.len())
    }
}

impl<'de> Decode<'de> for NowVarBuf<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let len_u32 = VarU32::decode(src)?.value();
        let len: usize = cast_length!("len", len_u32)?;

        ensure_size!(in: src, size: len);
        let bytes = src.read_slice(len);

        Ok(NowVarBuf(Cow::Borrowed(bytes)))
    }
}

impl Deref for NowVarBuf<'_> {
    type Target = [u8];

    fn deref(&self) -> &Self::Target {
        &self.0
    }
}
