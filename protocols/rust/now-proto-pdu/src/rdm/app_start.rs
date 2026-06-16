use bitflags::bitflags;
use ironrdp_core::{cast_length, ensure_fixed_part_size, DecodeResult, Encode, EncodeResult, ReadCursor, WriteCursor};

use crate::{NowHeader, NowMessageClass, NowRdmMsgKind};

bitflags! {
    /// NOW-PROTO: NOW_RDM_APP_START_MSG launch_flags field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    struct NowRdmLaunchFlags: u32 {
        /// Launch RDM in Jump mode.
        ///
        /// NOW-PROTO: NOW_RDM_LAUNCH_FLAG_JUMP_MODE
        const JUMP_MODE = 0x00000001;
        /// Launch RDM maximized.
        ///
        /// NOW-PROTO: NOW_RDM_LAUNCH_FLAG_MAXIMIZED
        const MAXIMIZED = 0x00000002;
        /// Launch RDM in fullscreen mode.
        ///
        /// NOW-PROTO: NOW_RDM_LAUNCH_FLAG_FULLSCREEN
        const FULLSCREEN = 0x00000004;
    }
}

/// The NOW_RDM_APP_START_MSG message is used to launch RDM.
///
/// NOW-PROTO: NOW_RDM_APP_START_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowRdmAppStartMsg {
    launch_flags: NowRdmLaunchFlags,
    timeout: u32,
}

impl Default for NowRdmAppStartMsg {
    fn default() -> Self {
        Self {
            launch_flags: NowRdmLaunchFlags::empty(),
            timeout: 45, // Default timeout is 45 seconds
        }
    }
}

impl NowRdmAppStartMsg {
    const NAME: &'static str = "NOW_RDM_APP_START_MSG";
    const FIXED_PART_SIZE: usize = 8; // 4 + 4 bytes

    #[must_use]
    pub fn with_timeout(mut self, timeout: u32) -> Self {
        self.timeout = timeout;
        self
    }

    #[must_use]
    pub fn with_jump_mode(mut self) -> Self {
        self.launch_flags |= NowRdmLaunchFlags::JUMP_MODE;
        self
    }

    #[must_use]
    pub fn with_maximized(mut self) -> Self {
        self.launch_flags |= NowRdmLaunchFlags::MAXIMIZED;
        self
    }

    #[must_use]
    pub fn with_fullscreen(mut self) -> Self {
        self.launch_flags |= NowRdmLaunchFlags::FULLSCREEN;
        self
    }

    pub fn is_jump_mode(&self) -> bool {
        self.launch_flags.contains(NowRdmLaunchFlags::JUMP_MODE)
    }

    pub fn is_maximized(&self) -> bool {
        self.launch_flags.contains(NowRdmLaunchFlags::MAXIMIZED)
    }

    pub fn is_fullscreen(&self) -> bool {
        self.launch_flags.contains(NowRdmLaunchFlags::FULLSCREEN)
    }

    pub fn timeout(&self) -> u32 {
        self.timeout
    }

    fn body_size() -> usize {
        Self::FIXED_PART_SIZE
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let launch_flags = NowRdmLaunchFlags::from_bits_retain(src.read_u32());
        let timeout = src.read_u32();

        Ok(Self { launch_flags, timeout })
    }
}

impl Encode for NowRdmAppStartMsg {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", Self::body_size())?,
            class: NowMessageClass::RDM,
            kind: NowRdmMsgKind::APP_START.0,
            flags: 0,
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.launch_flags.bits());
        dst.write_u32(self.timeout);

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + Self::body_size()
    }
}
