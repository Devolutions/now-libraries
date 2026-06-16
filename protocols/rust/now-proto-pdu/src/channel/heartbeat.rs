use ironrdp_core::{invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, ReadCursor, WriteCursor};

use crate::{NowChannelMessage, NowChannelMsgKind, NowHeader, NowMessage, NowMessageClass};

/// Periodic heartbeat message sent by the server. If the client does not receive this message
/// within the specified interval, it should consider the connection as lost.
///
/// NOW-PROTO: NOW_CHANNEL_HEARTBEAT_MSG
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct NowChannelHeartbeatMsg {}

impl NowChannelHeartbeatMsg {
    const NAME: &'static str = "NOW_CHANNEL_HEARTBEAT_MSG";
}

impl Encode for NowChannelHeartbeatMsg {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: 0,
            class: NowMessageClass::CHANNEL,
            kind: NowChannelMsgKind::HEARTBEAT.0,
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

impl Decode<'_> for NowChannelHeartbeatMsg {
    fn decode(src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowChannelMsgKind(header.kind)) {
            (NowMessageClass::CHANNEL, NowChannelMsgKind::HEARTBEAT) => Ok(NowChannelHeartbeatMsg::default()),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl From<NowChannelHeartbeatMsg> for NowMessage<'_> {
    fn from(msg: NowChannelHeartbeatMsg) -> Self {
        NowMessage::Channel(NowChannelMessage::Heartbeat(msg))
    }
}
