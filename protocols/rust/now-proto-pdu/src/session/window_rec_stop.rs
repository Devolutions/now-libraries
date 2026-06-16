use ironrdp_core::{Decode, DecodeResult, Encode, EncodeResult, ReadCursor, WriteCursor};

use crate::{NowHeader, NowMessage, NowMessageClass, NowSessionMessage, NowSessionMessageKind};

/// The NOW_SESSION_WINDOW_REC_STOP_MSG message is used to stop window recording.
///
/// NOW_PROTO: NOW_SESSION_WINDOW_REC_STOP_MSG
#[derive(Debug, Clone, PartialEq, Eq, Default)]
#[non_exhaustive]
pub struct NowSessionWindowRecStopMsg;

impl NowSessionWindowRecStopMsg {
    const NAME: &'static str = "NOW_SESSION_WINDOW_REC_STOP_MSG";
}

impl Encode for NowSessionWindowRecStopMsg {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: 0,
            class: NowMessageClass::SESSION,
            kind: NowSessionMessageKind::WINDOW_REC_STOP.0,
            flags: 0,
        };

        header.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE
    }
}

impl Decode<'_> for NowSessionWindowRecStopMsg {
    fn decode(src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowSessionMessageKind(header.kind)) {
            (NowMessageClass::SESSION, NowSessionMessageKind::WINDOW_REC_STOP) => Ok(Self::default()),
            _ => Err(unsupported_message_err!(class: header.class.0, kind: header.kind)),
        }
    }
}

impl From<NowSessionWindowRecStopMsg> for NowMessage<'_> {
    fn from(value: NowSessionWindowRecStopMsg) -> Self {
        Self::Session(NowSessionMessage::WindowRecStop(value))
    }
}
