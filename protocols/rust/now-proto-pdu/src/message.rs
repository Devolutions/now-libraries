use ironrdp_core::{ensure_size, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor};

use crate::{
    NowChannelMessage, NowExecMessage, NowHeader, NowMessageClass, NowRdmMessage, NowSessionMessage, NowSystemMessage,
};

/// Wrapper type for messages transferred over the NOW-PROTO communication channel.
///
/// NOW-PROTO: NOW_*_MSG messages
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum NowMessage<'a> {
    Channel(NowChannelMessage<'a>),
    System(NowSystemMessage<'a>),
    Session(NowSessionMessage<'a>),
    Exec(NowExecMessage<'a>),
    Rdm(NowRdmMessage<'a>),
}

impl_pdu_borrowing!(NowMessage<'_>, OwnedNowMessage);

impl IntoOwned for NowMessage<'_> {
    type Owned = OwnedNowMessage;

    fn into_owned(self) -> Self::Owned {
        match self {
            Self::Channel(msg) => OwnedNowMessage::Channel(msg.into_owned()),
            Self::System(msg) => OwnedNowMessage::System(msg.into_owned()),
            Self::Session(msg) => OwnedNowMessage::Session(msg.into_owned()),
            Self::Exec(msg) => OwnedNowMessage::Exec(msg.into_owned()),
            Self::Rdm(msg) => OwnedNowMessage::Rdm(msg.into_owned()),
        }
    }
}

impl NowMessage<'_> {
    const NAME: &'static str = "NOW_MSG";
}

impl Encode for NowMessage<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        match self {
            Self::Channel(msg) => msg.encode(dst),
            Self::System(msg) => msg.encode(dst),
            Self::Session(msg) => msg.encode(dst),
            Self::Exec(msg) => msg.encode(dst),
            Self::Rdm(msg) => msg.encode(dst),
        }
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        match self {
            Self::Channel(msg) => msg.size(),
            Self::System(msg) => msg.size(),
            Self::Session(msg) => msg.size(),
            Self::Exec(msg) => msg.size(),
            Self::Rdm(msg) => msg.size(),
        }
    }
}

impl<'de> Decode<'de> for NowMessage<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        // Read all message body regardless of the remaining lefover message data.
        // This is required to allow forward compatibility with future now-proto versions,
        // which may add new message fields which are encoded unconditionally.
        ensure_size!(in: src, size: header.size as usize);
        let mut body = ReadCursor::new(src.read_slice(header.size as usize));

        Self::decode_from_body(header, &mut body)
    }
}

impl<'a> NowMessage<'a> {
    pub fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        match NowMessageClass(header.class.0) {
            NowMessageClass::CHANNEL => Ok(Self::Channel(NowChannelMessage::decode_from_body(header, src)?)),
            NowMessageClass::SYSTEM => Ok(Self::System(NowSystemMessage::decode_from_body(header, src)?)),
            NowMessageClass::SESSION => Ok(Self::Session(NowSessionMessage::decode_from_body(header, src)?)),
            NowMessageClass::EXEC => Ok(Self::Exec(NowExecMessage::decode_from_body(header, src)?)),
            NowMessageClass::RDM => Ok(Self::Rdm(NowRdmMessage::decode_from_body(header, src)?)),
            // Handle unknown class; Unknown kind is handled by underlying message type.
            _ => Err(unsupported_message_err!(class: header.class.0, kind: header.kind)),
        }
    }
}
