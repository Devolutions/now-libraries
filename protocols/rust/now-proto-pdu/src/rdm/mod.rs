mod app_action;
mod app_notify;
mod app_start;
mod capabilities;
mod session_action;
mod session_notify;
mod session_start;

pub use app_action::{NowRdmAppAction, NowRdmAppActionMsg, OwnedNowRdmAppActionMsg};
pub use app_notify::{NowRdmAppNotifyMsg, NowRdmAppState, NowRdmReason, OwnedNowRdmAppNotifyMsg};
pub use app_start::NowRdmAppStartMsg;
pub use capabilities::{NowRdmCapabilitiesMsg, OwnedNowRdmCapabilitiesMsg};
pub use session_action::{NowRdmSessionAction, NowRdmSessionActionMsg, OwnedNowRdmSessionActionMsg};
pub use session_notify::{NowRdmSessionNotifyKind, NowRdmSessionNotifyMsg, OwnedNowRdmSessionNotifyMsg};
pub use session_start::{NowRdmSessionStartMsg, OwnedNowRdmSessionStartMsg};

use ironrdp_core::{DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor};

use crate::NowHeader;

// Wrapper for the `NOW_RDM_MSG_CLASS_ID` message class.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum NowRdmMessage<'a> {
    Capabilities(NowRdmCapabilitiesMsg<'a>),
    AppStart(NowRdmAppStartMsg),
    AppAction(NowRdmAppActionMsg<'a>),
    AppNotify(NowRdmAppNotifyMsg<'a>),
    SessionStart(NowRdmSessionStartMsg<'a>),
    SessionAction(NowRdmSessionActionMsg),
    SessionNotify(NowRdmSessionNotifyMsg<'a>),
}

pub type OwnedNowRdmMessage = NowRdmMessage<'static>;

impl IntoOwned for NowRdmMessage<'_> {
    type Owned = OwnedNowRdmMessage;

    fn into_owned(self) -> Self::Owned {
        match self {
            Self::Capabilities(msg) => OwnedNowRdmMessage::Capabilities(msg.into_owned()),
            Self::AppStart(msg) => OwnedNowRdmMessage::AppStart(msg),
            Self::AppAction(msg) => OwnedNowRdmMessage::AppAction(msg.into_owned()),
            Self::AppNotify(msg) => OwnedNowRdmMessage::AppNotify(msg.into_owned()),
            Self::SessionStart(msg) => OwnedNowRdmMessage::SessionStart(msg.into_owned()),
            Self::SessionAction(msg) => OwnedNowRdmMessage::SessionAction(msg.into_owned()),
            Self::SessionNotify(msg) => OwnedNowRdmMessage::SessionNotify(msg.into_owned()),
        }
    }
}

impl<'a> NowRdmMessage<'a> {
    const NAME: &'static str = "NOW_RDM_MSG";

    pub fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        match NowRdmMsgKind(header.kind) {
            NowRdmMsgKind::CAPABILITIES => Ok(Self::Capabilities(NowRdmCapabilitiesMsg::decode_from_body(
                header, src,
            )?)),
            NowRdmMsgKind::APP_START => Ok(Self::AppStart(NowRdmAppStartMsg::decode_from_body(header, src)?)),
            NowRdmMsgKind::APP_ACTION => Ok(Self::AppAction(NowRdmAppActionMsg::decode_from_body(header, src)?)),
            NowRdmMsgKind::APP_NOTIFY => Ok(Self::AppNotify(NowRdmAppNotifyMsg::decode_from_body(header, src)?)),
            NowRdmMsgKind::SESSION_START => Ok(Self::SessionStart(NowRdmSessionStartMsg::decode_from_body(
                header, src,
            )?)),
            NowRdmMsgKind::SESSION_ACTION => Ok(Self::SessionAction(NowRdmSessionActionMsg::decode_from_body(
                header, src,
            )?)),
            NowRdmMsgKind::SESSION_NOTIFY => Ok(Self::SessionNotify(NowRdmSessionNotifyMsg::decode_from_body(
                header, src,
            )?)),
            _ => Err(unsupported_message_err!(class: header.class.0, kind: header.kind)),
        }
    }
}

impl Encode for NowRdmMessage<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        match self {
            Self::Capabilities(msg) => msg.encode(dst),
            Self::AppStart(msg) => msg.encode(dst),
            Self::AppAction(msg) => msg.encode(dst),
            Self::AppNotify(msg) => msg.encode(dst),
            Self::SessionStart(msg) => msg.encode(dst),
            Self::SessionAction(msg) => msg.encode(dst),
            Self::SessionNotify(msg) => msg.encode(dst),
        }
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        match self {
            Self::Capabilities(msg) => msg.size(),
            Self::AppStart(msg) => msg.size(),
            Self::AppAction(msg) => msg.size(),
            Self::AppNotify(msg) => msg.size(),
            Self::SessionStart(msg) => msg.size(),
            Self::SessionAction(msg) => msg.size(),
            Self::SessionNotify(msg) => msg.size(),
        }
    }
}

/// NOW-PROTO: NOW_RDM_*_ID
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NowRdmMsgKind(pub u8);

impl NowRdmMsgKind {
    /// NOW-PROTO: NOW_RDM_CAPABILITIES_MSG_ID
    pub const CAPABILITIES: Self = Self(0x01);
    /// NOW-PROTO: NOW_RDM_APP_START_MSG_ID
    pub const APP_START: Self = Self(0x02);
    // NOW-PROTO: NOW_RDM_APP_ACTION_MSG_ID
    pub const APP_ACTION: Self = Self(0x03);
    // NOW-PROTO: NOW_RDM_APP_NOTIFY_MSG_ID
    pub const APP_NOTIFY: Self = Self(0x04);
    // NOW-PROTO: NOW_RDM_SESSION_START_MSG_ID
    pub const SESSION_START: Self = Self(0x05);
    // NOW-PROTO: NOW_RDM_SESSION_ACTION_MSG_ID
    pub const SESSION_ACTION: Self = Self(0x06);
    // NOW-PROTO: NOW_RDM_SESSION_NOTIFY_MSG_ID
    pub const SESSION_NOTIFY: Self = Self(0x07);
}
