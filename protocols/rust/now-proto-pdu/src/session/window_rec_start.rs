use bitflags::bitflags;
use ironrdp_core::{cast_length, Decode, DecodeResult, Encode, EncodeResult, ReadCursor, WriteCursor};

use crate::{NowHeader, NowMessage, NowMessageClass, NowSessionMessage, NowSessionMessageKind};

bitflags! {
    /// Flags for window recording start message.
    ///
    /// NOW_PROTO: NOW_SESSION_WINDOW_REC_START_MSG msgFlags
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    pub struct WindowRecStartFlags: u16 {
        /// Enable window title change tracking.
        ///
        /// NOW-PROTO: NOW_WINDOW_REC_FLAG_TRACK_TITLE_CHANGE
        const TRACK_TITLE_CHANGE = 0x0001;
    }
}

/// The NOW_SESSION_WINDOW_REC_START_MSG message is used to start window recording, which tracks
/// active window changes and title updates.
///
/// NOW_PROTO: NOW_SESSION_WINDOW_REC_START_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowSessionWindowRecStartMsg {
    /// Flags for window recording options
    pub flags: WindowRecStartFlags,
    /// Interval in milliseconds for polling window changes.
    /// Set to 0 to use the host's default poll interval.
    pub poll_interval: u32,
}

impl NowSessionWindowRecStartMsg {
    const NAME: &'static str = "NOW_SESSION_WINDOW_REC_START_MSG";
    const FIXED_PART_SIZE: usize = 4;

    pub fn new(poll_interval: u32, flags: WindowRecStartFlags) -> Self {
        Self { poll_interval, flags }
    }
}

impl Encode for NowSessionWindowRecStartMsg {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", Self::FIXED_PART_SIZE)?,
            class: NowMessageClass::SESSION,
            kind: NowSessionMessageKind::WINDOW_REC_START.0,
            flags: self.flags.bits(),
        };

        header.encode(dst)?;
        dst.write_u32(self.poll_interval);

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + Self::FIXED_PART_SIZE
    }
}

impl Decode<'_> for NowSessionWindowRecStartMsg {
    fn decode(src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowSessionMessageKind(header.kind)) {
            (NowMessageClass::SESSION, NowSessionMessageKind::WINDOW_REC_START) => Self::decode_from_body(header, src),
            _ => Err(unsupported_message_err!(class: header.class.0, kind: header.kind)),
        }
    }
}

impl NowSessionWindowRecStartMsg {
    pub(crate) fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        let flags = WindowRecStartFlags::from_bits_retain(header.flags);
        let poll_interval = src.read_u32();

        Ok(Self { poll_interval, flags })
    }
}

impl From<NowSessionWindowRecStartMsg> for NowMessage<'_> {
    fn from(value: NowSessionWindowRecStartMsg) -> Self {
        Self::Session(NowSessionMessage::WindowRecStart(value))
    }
}
