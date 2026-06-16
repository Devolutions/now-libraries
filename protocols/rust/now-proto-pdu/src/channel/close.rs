use ironrdp_core::{invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor};

use crate::{NowChannelMessage, NowChannelMsgKind, NowHeader, NowMessage, NowMessageClass, NowStatus, NowStatusError};

/// Channel close notice, could be sent by either parties at any moment of communication to
/// gracefully close DVC channel.
///
/// NOW-PROTO: NOW_CHANNEL_CLOSE_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowChannelCloseMsg<'a> {
    status: NowStatus<'a>,
}

impl_pdu_borrowing!(NowChannelCloseMsg<'_>, OwnedNowChannelCloseMsg);

impl IntoOwned for NowChannelCloseMsg<'_> {
    type Owned = OwnedNowChannelCloseMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowChannelCloseMsg {
            status: self.status.into_owned(),
        }
    }
}

impl Default for NowChannelCloseMsg<'_> {
    fn default() -> Self {
        let status = NowStatus::new_success();

        Self { status }
    }
}

impl<'a> NowChannelCloseMsg<'a> {
    const NAME: &'static str = "NOW_CHANNEL_CLOSE_MSG";

    pub fn from_error(error: impl Into<NowStatusError>) -> EncodeResult<Self> {
        let status = NowStatus::new_error(error);

        let msg = Self { status };

        ensure_now_message_size!(msg.status.size());

        Ok(msg)
    }

    pub fn to_result(&self) -> Result<(), NowStatusError> {
        self.status.to_result()
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        let status = NowStatus::decode(src)?;

        Ok(Self { status })
    }
}

impl Encode for NowChannelCloseMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: self.status.size().try_into().expect("validated in constructor"),
            class: NowMessageClass::CHANNEL,
            kind: NowChannelMsgKind::CLOSE.0,
            flags: 0,
        };

        header.encode(dst)?;

        self.status.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.status.size()
    }
}

impl<'de> Decode<'de> for NowChannelCloseMsg<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowChannelMsgKind(header.kind)) {
            (NowMessageClass::CHANNEL, NowChannelMsgKind::CLOSE) => Self::decode_from_body(header, src),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl<'a> From<NowChannelCloseMsg<'a>> for NowMessage<'a> {
    fn from(msg: NowChannelCloseMsg<'a>) -> Self {
        NowMessage::Channel(NowChannelMessage::Close(msg))
    }
}
