//! Variable-length number types.
use ironrdp_core::{
    ensure_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeError, EncodeResult, ReadCursor, WriteCursor,
};

/// Variable-length encoded u32.
/// Value range: `[0..0x3FFFFFFF]`
///
/// NOW-PROTO: NOW_VARU32
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub struct VarU32(u32);

impl VarU32 {
    pub const MIN: u32 = 0x00000000;
    pub const MAX: u32 = 0x3FFFFFFF;

    const NAME: &'static str = "NOW_VARU32";

    pub fn new(value: u32) -> EncodeResult<Self> {
        if value > Self::MAX {
            return Err(invalid_field_err!("value", "too large number"));
        }

        Ok(VarU32(value))
    }

    pub fn value(&self) -> u32 {
        self.0
    }
}

impl Encode for VarU32 {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let encoded_size = self.size();

        ensure_size!(in: dst, size: encoded_size);

        // LINTS: encoded_size will always be [1..4], therefore following arithmetic is safe
        #[allow(clippy::arithmetic_side_effects)]
        let mut shift = (encoded_size - 1) * 8;
        let mut bytes = [0u8; 4];

        for byte in bytes.iter_mut().take(encoded_size) {
            *byte = ((self.0 >> shift) & 0xFF).try_into().expect("always <= 0xFF");

            // LINTS: as per code above, shift is always 8, 16, 24
            #[allow(clippy::arithmetic_side_effects)]
            if shift != 0 {
                shift -= 8;
            }
        }

        // LINTS: encoded_size is always >= 1
        #[allow(clippy::arithmetic_side_effects)]
        let c: u8 = (encoded_size - 1).try_into().expect("always fits into u8");
        bytes[0] |= c << 6;

        dst.write_slice(&bytes[..encoded_size]);

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        match self.0 {
            0x00..=0x3F => 1,
            0x40..=0x3FFF => 2,
            0x4000..=0x3FFFFF => 3,
            0x400000..=0x3FFFFFFF => 4,
            _ => unreachable!("BUG: value is out of range!"),
        }
    }
}

impl Decode<'_> for VarU32 {
    fn decode(src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        // Ensure we have at least 1 byte available to determine the size of the value
        ensure_size!(in: src, size: 1);

        let header = src.read_u8();
        let c: usize = ((header >> 6) & 0x03).into();

        if c == 0 {
            return Ok(VarU32((header & 0x3F).into()));
        }

        ensure_size!(in: src, size: c);
        let bytes = src.read_slice(c);

        let val1 = header & 0x3F;

        // LINTS: c is always [1..4]
        #[allow(clippy::arithmetic_side_effects)]
        let mut shift = c * 8;
        let mut num = u32::from(val1) << shift;

        // Read val2..valN
        // LINTS: shift is always 8, 16, 24
        #[allow(clippy::arithmetic_side_effects)]
        for val in bytes.iter().take(c) {
            shift -= 8;
            num |= (u32::from(*val)) << shift;
        }

        Ok(VarU32(num))
    }
}

impl From<VarU32> for u32 {
    fn from(value: VarU32) -> Self {
        value.value()
    }
}

impl TryFrom<u32> for VarU32 {
    type Error = EncodeError;

    fn try_from(value: u32) -> Result<Self, Self::Error> {
        Self::new(value)
    }
}
