use ironrdp_core::{
    cast_length, ensure_fixed_part_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, IntoOwned,
    ReadCursor, WriteCursor,
};

use crate::{NowExecMessage, NowExecMsgKind, NowHeader, NowMessage, NowMessageClass, NowStatus, NowStatusError};

/// The NOW_EXEC_RESULT_MSG message is used to return the result of an execution request.
///
/// NOW_PROTO: NOW_EXEC_RESULT_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowExecResultMsg<'a> {
    session_id: u32,
    exit_code: u32,
    status: NowStatus<'a>,
}

impl_pdu_borrowing!(NowExecResultMsg<'_>, OwnedNowExecResultMsg);

impl IntoOwned for NowExecResultMsg<'_> {
    type Owned = OwnedNowExecResultMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowExecResultMsg {
            session_id: self.session_id,
            exit_code: self.exit_code,
            status: self.status.into_owned(),
        }
    }
}

impl<'a> NowExecResultMsg<'a> {
    const NAME: &'static str = "NOW_EXEC_RESULT_MSG";
    const FIXED_PART_SIZE: usize = 8;

    pub fn new_success(session_id: u32, exit_code: u32) -> Self {
        let msg = Self {
            session_id,
            exit_code,
            status: NowStatus::new_success(),
        };

        msg.ensure_message_size()
            .expect("success message size always fits into payload");

        msg
    }

    pub fn new_error(session_id: u32, error: impl Into<NowStatusError>) -> EncodeResult<Self> {
        let msg = Self {
            session_id,
            exit_code: 0,
            status: NowStatus::new_error(error),
        };

        msg.ensure_message_size()?;

        Ok(msg)
    }

    pub fn session_id(&self) -> u32 {
        self.session_id
    }

    /// Returns the process exit code of the executed command on success.
    pub fn to_result(&self) -> Result<u32, NowStatusError> {
        self.status.to_result().map(|_| self.exit_code)
    }

    // LINTS: Overall message size always fits into usize
    #[allow(clippy::arithmetic_side_effects)]
    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.status.size()
    }

    fn ensure_message_size(&self) -> EncodeResult<()> {
        ensure_now_message_size!(Self::FIXED_PART_SIZE, self.status.size());

        Ok(())
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let session_id = src.read_u32();
        let exit_code = src.read_u32();

        let status = NowStatus::decode(src)?;

        Ok(Self {
            session_id,
            exit_code,
            status,
        })
    }
}

impl Encode for NowExecResultMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::EXEC,
            kind: NowExecMsgKind::RESULT.0,
            flags: 0,
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.session_id);
        dst.write_u32(self.exit_code);

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

impl<'de> Decode<'de> for NowExecResultMsg<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowExecMsgKind(header.kind)) {
            (NowMessageClass::EXEC, NowExecMsgKind::RESULT) => Self::decode_from_body(header, src),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl<'a> From<NowExecResultMsg<'a>> for NowMessage<'a> {
    fn from(msg: NowExecResultMsg<'a>) -> Self {
        NowMessage::Exec(NowExecMessage::Result(msg))
    }
}
