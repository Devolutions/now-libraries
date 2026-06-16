use ironrdp_core::{
    cast_length, ensure_fixed_part_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, ReadCursor,
    WriteCursor,
};

use crate::{NowExecMessage, NowExecMsgKind, NowHeader, NowMessage, NowMessageClass};

/// The NOW_EXEC_STARTED_MSG message is sent by the server after the execution session has been
/// successfully started.
///
/// NOW-PROTO: NOW_EXEC_STARTED_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowExecStartedMsg {
    session_id: u32,
}

impl NowExecStartedMsg {
    const NAME: &'static str = "NOW_EXEC_STARTED_MSG";
    const FIXED_PART_SIZE: usize = 4;

    pub fn new(session_id: u32) -> Self {
        Self { session_id }
    }

    pub fn session_id(&self) -> u32 {
        self.session_id
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let session_id = src.read_u32();

        Ok(Self { session_id })
    }
}

impl Encode for NowExecStartedMsg {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", Self::FIXED_PART_SIZE)?,
            class: NowMessageClass::EXEC,
            kind: NowExecMsgKind::STARTED.0,
            flags: 0,
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.session_id);

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    // LINTS: Overall message size always fits into usize
    #[allow(clippy::arithmetic_side_effects)]
    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + Self::FIXED_PART_SIZE
    }
}

impl Decode<'_> for NowExecStartedMsg {
    fn decode(src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowExecMsgKind(header.kind)) {
            (NowMessageClass::EXEC, NowExecMsgKind::STARTED) => Self::decode_from_body(header, src),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl From<NowExecStartedMsg> for NowMessage<'_> {
    fn from(msg: NowExecStartedMsg) -> Self {
        NowMessage::Exec(NowExecMessage::Started(msg))
    }
}
