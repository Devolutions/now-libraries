use ironrdp_core::{
    cast_length, ensure_fixed_part_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, ReadCursor,
    WriteCursor,
};

use crate::{NowExecMessage, NowExecMsgKind, NowHeader, NowMessage, NowMessageClass};

/// The NOW_EXEC_ABORT_MSG message is used to abort a remote execution immediately due to an
/// unrecoverable error. This message can be sent at any time without an explicit response message.
/// The session is considered aborted as soon as this message is sent.
///
/// NOW-PROTO: NOW_EXEC_ABORT_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowExecAbortMsg {
    session_id: u32,
    exit_code: u32,
}

impl NowExecAbortMsg {
    const NAME: &'static str = "NOW_EXEC_ABORT_MSG";
    const FIXED_PART_SIZE: usize = 8;

    pub fn new(session_id: u32, exit_code: u32) -> Self {
        Self { session_id, exit_code }
    }

    pub fn session_id(&self) -> u32 {
        self.session_id
    }

    pub fn exit_code(&self) -> u32 {
        self.exit_code
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let session_id = src.read_u32();
        let exit_code = src.read_u32();

        Ok(Self { session_id, exit_code })
    }
}

impl Encode for NowExecAbortMsg {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", Self::FIXED_PART_SIZE)?,
            class: NowMessageClass::EXEC,
            kind: NowExecMsgKind::ABORT.0,
            flags: 0,
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.session_id);
        dst.write_u32(self.exit_code);

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

impl Decode<'_> for NowExecAbortMsg {
    fn decode(src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowExecMsgKind(header.kind)) {
            (NowMessageClass::EXEC, NowExecMsgKind::ABORT) => Self::decode_from_body(header, src),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl From<NowExecAbortMsg> for NowMessage<'_> {
    fn from(msg: NowExecAbortMsg) -> Self {
        NowMessage::Exec(NowExecMessage::Abort(msg))
    }
}
