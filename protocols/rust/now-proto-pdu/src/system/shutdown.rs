use alloc::borrow::Cow;
use core::time;

use bitflags::bitflags;
use ironrdp_core::{
    cast_length, ensure_fixed_part_size, invalid_field_err, Decode as _, DecodeResult, Encode, EncodeResult, IntoOwned,
    ReadCursor, WriteCursor,
};

use crate::system::NowSystemMessageKind;
use crate::{NowHeader, NowMessage, NowMessageClass, NowSystemMessage, NowVarStr};

bitflags! {
    /// NOW_PROTO: NOW_SYSTEM_SHUTDOWN_FLAG_* constants.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    struct NowSystemShutdownFlags: u16 {
        /// Force shutdown
        ///
        /// NOW-PROTO: NOW_SHUTDOWN_FLAG_FORCE
        const FORCE = 0x0001;
        /// Reboot after shutdown
        ///
        /// NOW-PROTO: NOW_SHUTDOWN_FLAG_REBOOT
        const REBOOT = 0x0002;
    }
}

/// The NOW_SYSTEM_SHUTDOWN_MSG structure is used to request a system shutdown.
///
/// NOW_PROTO: NOW_SYSTEM_SHUTDOWN_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowSystemShutdownMsg<'a> {
    flags: NowSystemShutdownFlags,
    /// This system shutdown timeout, in seconds.
    timeout: u32,
    /// Optional shutdown message.
    message: NowVarStr<'a>,
}

pub type OwnedNowSystemShutdownMsg = NowSystemShutdownMsg<'static>;

impl IntoOwned for NowSystemShutdownMsg<'_> {
    type Owned = OwnedNowSystemShutdownMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowSystemShutdownMsg {
            flags: self.flags,
            timeout: self.timeout,
            message: self.message.into_owned(),
        }
    }
}

impl<'a> NowSystemShutdownMsg<'a> {
    const NAME: &'static str = "NOW_SYSTEM_SHUTDOWN_MSG";
    const FIXED_PART_SIZE: usize = 4 /* u32 timeout */;

    pub fn new(timeout: time::Duration, message: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        // Sanity check: Limit shutdown timeout to ~1 year.
        const MAX_SHUTDOWN_TINEOUT: time::Duration = time::Duration::from_secs(60 * 60 * 24 * 365);

        if timeout > MAX_SHUTDOWN_TINEOUT {
            return Err(invalid_field_err!("timeout", "too big shutdown timeout"));
        }

        let timeout = u32::try_from(timeout.as_secs()).expect("timeout is within u32 range");

        let msg = Self {
            flags: NowSystemShutdownFlags::empty(),
            timeout,
            message: NowVarStr::new(message)?,
        };

        ensure_now_message_size!(Self::FIXED_PART_SIZE, msg.message.size());

        Ok(msg)
    }

    #[must_use]
    pub fn with_force_shutdown(mut self) -> Self {
        self.flags |= NowSystemShutdownFlags::FORCE;
        self
    }

    #[must_use]
    pub fn with_reboot(mut self) -> Self {
        self.flags |= NowSystemShutdownFlags::REBOOT;
        self
    }

    pub fn is_force_shutdown(&self) -> bool {
        self.flags.contains(NowSystemShutdownFlags::FORCE)
    }

    pub fn is_reboot(&self) -> bool {
        self.flags.contains(NowSystemShutdownFlags::REBOOT)
    }

    pub fn timeout(&self) -> time::Duration {
        time::Duration::from_secs(self.timeout.into())
    }

    pub fn message(&self) -> &str {
        &self.message
    }

    // LINTS: Overall message size is validated in the constructor/decode method
    #[allow(clippy::arithmetic_side_effects)]
    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.message.size()
    }

    pub fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let timeout = src.read_u32();
        let message = NowVarStr::decode(src)?;

        let msg = Self {
            flags: NowSystemShutdownFlags::from_bits_retain(header.flags),
            timeout,
            message,
        };

        Ok(msg)
    }
}

impl Encode for NowSystemShutdownMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::SYSTEM,
            kind: NowSystemMessageKind::SHUTDOWN.0,
            flags: self.flags.bits(),
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.timeout);
        self.message.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    // LINTS: See body_size()
    #[allow(clippy::arithmetic_side_effects)]
    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.body_size()
    }
}

impl<'a> From<NowSystemShutdownMsg<'a>> for NowMessage<'a> {
    fn from(msg: NowSystemShutdownMsg<'a>) -> Self {
        NowMessage::System(NowSystemMessage::Shutdown(msg))
    }
}
