use alloc::borrow::Cow;
use ironrdp_core::{
    cast_length, ensure_fixed_part_size, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor,
};

use crate::{NowHeader, NowMessageClass, NowRdmMsgKind, NowVarStr};

/// The NOW_RDM_APP_ACTION_MSG is sent by the client to trigger an application state change.
///
/// NOW-PROTO: NOW_RDM_APP_ACTION_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowRdmAppActionMsg<'a> {
    app_action: NowRdmAppAction,
    action_data: NowVarStr<'a>,
}

pub type OwnedNowRdmAppActionMsg = NowRdmAppActionMsg<'static>;

impl IntoOwned for NowRdmAppActionMsg<'_> {
    type Owned = OwnedNowRdmAppActionMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowRdmAppActionMsg {
            app_action: self.app_action,
            action_data: self.action_data.into_owned(),
        }
    }
}

/// Application action types for RDM
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NowRdmAppAction(u32);

impl NowRdmAppAction {
    /// Close (terminate) RDM application
    pub const CLOSE: Self = Self(0x00000001);
    /// Minimize RDM application window
    pub const MINIMIZE: Self = Self(0x00000002);
    /// Maximize RDM application window
    pub const MAXIMIZE: Self = Self(0x00000003);
    /// Restore RDM application window
    pub const RESTORE: Self = Self(0x00000004);
    /// Toggle RDM fullscreen mode
    pub const FULLSCREEN: Self = Self(0x00000005);

    pub(crate) fn new(action: u32) -> Self {
        Self(action)
    }

    pub(crate) fn value(&self) -> u32 {
        self.0
    }
}

impl<'a> NowRdmAppActionMsg<'a> {
    const NAME: &'static str = "NOW_RDM_APP_ACTION_MSG";
    const FIXED_PART_SIZE: usize = 4; // 4 bytes

    pub fn new(app_action: NowRdmAppAction) -> Self {
        Self {
            app_action,
            action_data: NowVarStr::default(),
        }
    }

    /// Create a message to close (terminate) the RDM application
    pub fn new_close() -> Self {
        Self::new(NowRdmAppAction::CLOSE)
    }

    /// Create a message to minimize the RDM application window
    pub fn new_minimize() -> Self {
        Self::new(NowRdmAppAction::MINIMIZE)
    }

    /// Create a message to maximize the RDM application window
    pub fn new_maximize() -> Self {
        Self::new(NowRdmAppAction::MAXIMIZE)
    }

    /// Create a message to restore the RDM application window
    pub fn new_restore() -> Self {
        Self::new(NowRdmAppAction::RESTORE)
    }

    /// Create a message to toggle RDM fullscreen mode
    pub fn new_fullscreen() -> Self {
        Self::new(NowRdmAppAction::FULLSCREEN)
    }

    pub fn with_action_data(mut self, action_data: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.action_data = NowVarStr::new(action_data)?;

        ensure_now_message_size!(Self::FIXED_PART_SIZE, self.action_data.size());

        Ok(self)
    }

    pub fn app_action(&self) -> NowRdmAppAction {
        self.app_action
    }

    pub fn action_data(&self) -> &str {
        &self.action_data
    }

    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.action_data.size()
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let app_action = NowRdmAppAction::new(src.read_u32());
        let action_data = NowVarStr::decode(src)?;

        Ok(Self {
            app_action,
            action_data,
        })
    }
}

impl Encode for NowRdmAppActionMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::RDM,
            kind: NowRdmMsgKind::APP_ACTION.0,
            flags: 0,
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.app_action.value());
        self.action_data.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.body_size()
    }
}
