use ironrdp_core::{
    cast_length, ensure_fixed_part_size, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor,
};

use crate::core::NowGuid;
use crate::{NowHeader, NowMessageClass, NowRdmMsgKind};

/// The NOW_RDM_SESSION_ACTION_MSG is used by the client to trigger an action on an existing session,
/// such closing or focusing a session.
///
/// NOW-PROTO: NOW_RDM_SESSION_ACTION_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowRdmSessionActionMsg {
    session_action: NowRdmSessionAction,
    session_id: NowGuid,
}

pub type OwnedNowRdmSessionActionMsg = NowRdmSessionActionMsg;

impl IntoOwned for NowRdmSessionActionMsg {
    type Owned = OwnedNowRdmSessionActionMsg;

    fn into_owned(self) -> Self::Owned {
        self
    }
}

/// Session action types for RDM
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NowRdmSessionAction(u32);

impl NowRdmSessionAction {
    /// Close or terminate the session
    pub const CLOSE: Self = Self(0x00000001);
    /// Focus the embedded tab of a specific session
    pub const FOCUS: Self = Self(0x00000002);

    pub(crate) fn new(action: u32) -> Self {
        Self(action)
    }

    pub(crate) fn value(&self) -> u32 {
        self.0
    }
}

impl NowRdmSessionActionMsg {
    const NAME: &'static str = "NOW_RDM_SESSION_ACTION_MSG";
    const FIXED_PART_SIZE: usize = 4; // 4 bytes for session_action field

    pub fn new(session_action: NowRdmSessionAction, session_id: uuid::Uuid) -> Self {
        Self {
            session_action,
            session_id: NowGuid::new(session_id),
        }
    }

    /// Create a message to close or terminate the session
    pub fn new_close(session_id: uuid::Uuid) -> Self {
        Self::new(NowRdmSessionAction::CLOSE, session_id)
    }

    /// Create a message to focus the embedded tab of a specific session
    pub fn new_focus(session_id: uuid::Uuid) -> Self {
        Self::new(NowRdmSessionAction::FOCUS, session_id)
    }

    pub fn session_action(&self) -> NowRdmSessionAction {
        self.session_action
    }

    pub fn session_id(&self) -> uuid::Uuid {
        self.session_id.as_uuid()
    }

    fn body_size(&self) -> usize {
        4 + self.session_id.size() // 4 bytes for session_action + variable GUID size
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        let session_action = NowRdmSessionAction::new(src.read_u32());
        let session_id = NowGuid::decode(src)?;

        Ok(Self {
            session_action,
            session_id,
        })
    }
}

impl Encode for NowRdmSessionActionMsg {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::RDM,
            kind: NowRdmMsgKind::SESSION_ACTION.0,
            flags: 0,
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.session_action.value());
        self.session_id.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.body_size()
    }
}
