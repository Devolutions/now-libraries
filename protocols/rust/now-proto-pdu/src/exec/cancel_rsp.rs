use ironrdp_core::{
    cast_length, ensure_fixed_part_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, IntoOwned,
    ReadCursor, WriteCursor,
};

use crate::{NowExecMessage, NowExecMsgKind, NowHeader, NowMessage, NowMessageClass, NowStatus, NowStatusError};

/// The NOW_EXEC_CANCEL_RSP_MSG message is used to respond to a remote execution cancel request.
///
/// NOW_PROTO: NOW_EXEC_CANCEL_RSP_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowExecCancelRspMsg<'a> {
    session_id: u32,
    status: NowStatus<'a>,
}

impl_pdu_borrowing!(NowExecCancelRspMsg<'_>, OwnedNowExecCancelRspMsg);

impl IntoOwned for NowExecCancelRspMsg<'_> {
    type Owned = OwnedNowExecCancelRspMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowExecCancelRspMsg {
            session_id: self.session_id,
            status: self.status.into_owned(),
        }
    }
}

impl<'a> NowExecCancelRspMsg<'a> {
    const NAME: &'static str = "NOW_EXEC_CANCEL_RSP_MSG";
    const FIXED_PART_SIZE: usize = 4;

    pub fn new_success(session_id: u32) -> Self {
        let msg = Self {
            session_id,
            status: NowStatus::new_success(),
        };

        msg
    }

    pub fn new_error(session_id: u32, error: impl Into<NowStatusError>) -> EncodeResult<Self> {
        let msg = Self {
            session_id,
            status: NowStatus::new_error(error),
        };

        ensure_now_message_size!(Self::FIXED_PART_SIZE, msg.status.size());

        Ok(msg)
    }

    pub fn session_id(&self) -> u32 {
        self.session_id
    }

    pub fn to_result(&self) -> Result<(), NowStatusError> {
        self.status.to_result()
    }

    // LINTS: Overall message size always fits into usize
    #[allow(clippy::arithmetic_side_effects)]
    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.status.size()
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);
        let session_id = src.read_u32();

        let status = NowStatus::decode(src)?;

        Ok(Self { session_id, status })
    }
}

impl Encode for NowExecCancelRspMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::EXEC,
            kind: NowExecMsgKind::CANCEL_RSP.0,
            flags: 0,
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.session_id);
        self.status.encode(dst)?;

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

impl<'de> Decode<'de> for NowExecCancelRspMsg<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowExecMsgKind(header.kind)) {
            (NowMessageClass::EXEC, NowExecMsgKind::CANCEL_RSP) => Self::decode_from_body(header, src),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl<'a> From<NowExecCancelRspMsg<'a>> for NowMessage<'a> {
    fn from(msg: NowExecCancelRspMsg<'a>) -> Self {
        NowMessage::Exec(NowExecMessage::CancelRsp(msg))
    }
}
