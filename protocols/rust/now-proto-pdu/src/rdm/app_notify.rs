use alloc::borrow::Cow;
use bitflags::bitflags;
use ironrdp_core::{
    cast_length, ensure_fixed_part_size, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor,
};

use crate::{NowHeader, NowMessageClass, NowRdmMsgKind, NowVarStr};

bitflags! {
    /// NOW-PROTO: NOW_RDM_APP_NOTIFY_MSG msgFlags field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    pub(crate) struct NowRdmAppNotifyFlags: u32 {
        // Currently no specific flags are defined in the protocol
        // This is prepared for future extensions
    }
}

/// NOW-PROTO: Application state values for NOW_RDM_APP_NOTIFY_MSG
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NowRdmAppState(u32);

impl NowRdmAppState {
    /// RDM is launched and ready to launch connections
    pub const READY: Self = Self(0x00000001);
    /// RDM has failed to launch
    pub const FAILED: Self = Self(0x00000002);
    /// RDM has been closed or terminated
    pub const CLOSED: Self = Self(0x00000003);
    /// RDM has been minimized
    pub const MINIMIZED: Self = Self(0x00000004);
    /// RDM has been maximized
    pub const MAXIMIZED: Self = Self(0x00000005);
    /// RDM has been restored
    pub const RESTORED: Self = Self(0x00000006);
    /// RDM fullscreen mode has been toggled
    pub const FULLSCREEN: Self = Self(0x00000007);

    pub(crate) fn new(state: u32) -> Self {
        Self(state)
    }

    pub(crate) fn value(&self) -> u32 {
        self.0
    }
}

/// NOW-PROTO: Reason code values for NOW_RDM_APP_NOTIFY_MSG
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NowRdmReason(u32);

impl NowRdmReason {
    /// Unspecified reason (default value)
    pub const NOT_SPECIFIED: Self = Self(0x00000000);
    /// The application state change was user-initiated
    pub const USER_INITIATED: Self = Self(0x00000001);
    /// RDM has failed to launched because it is not installed
    pub const NOT_INSTALLED: Self = Self(0x00000002);
    /// RDM is installed, but something prevented it from starting up properly
    pub const STARTUP_FAILURE: Self = Self(0x00000003);
    /// RDM is installed and could be launched but it wasn't ready before the expected timeout
    pub const LAUNCH_TIMEOUT: Self = Self(0x00000004);

    pub(crate) fn new(reason: u32) -> Self {
        Self(reason)
    }

    pub(crate) fn value(&self) -> u32 {
        self.0
    }
}

/// The NOW_RDM_APP_NOTIFY_MSG is sent by the server to notify the client of an RDM app state change, such as readiness.
///
/// NOW-PROTO: NOW_RDM_APP_NOTIFY_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowRdmAppNotifyMsg<'a> {
    app_state: NowRdmAppState,
    reason_code: NowRdmReason,
    notify_data: NowVarStr<'a>,
}

pub type OwnedNowRdmAppNotifyMsg = NowRdmAppNotifyMsg<'static>;

impl IntoOwned for NowRdmAppNotifyMsg<'_> {
    type Owned = OwnedNowRdmAppNotifyMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowRdmAppNotifyMsg {
            app_state: self.app_state,
            reason_code: self.reason_code,
            notify_data: self.notify_data.into_owned(),
        }
    }
}

impl<'a> NowRdmAppNotifyMsg<'a> {
    const NAME: &'static str = "NOW_RDM_APP_NOTIFY_MSG";
    const FIXED_PART_SIZE: usize = 8; // 4 + 4 bytes

    pub fn new(app_state: NowRdmAppState, reason_code: NowRdmReason) -> Self {
        Self {
            app_state,
            reason_code,
            notify_data: NowVarStr::new("").expect("empty string should always be valid"),
        }
    }

    pub fn with_notify_data(mut self, notify_data: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.notify_data = NowVarStr::new(notify_data)?;
        ensure_now_message_size!(Self::FIXED_PART_SIZE, self.notify_data.size());
        Ok(self)
    }

    pub fn app_state(&self) -> NowRdmAppState {
        self.app_state
    }

    pub fn reason_code(&self) -> NowRdmReason {
        self.reason_code
    }

    pub fn notify_data(&self) -> &str {
        &self.notify_data
    }

    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.notify_data.size()
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let app_state = NowRdmAppState::new(src.read_u32());
        let reason_code = NowRdmReason::new(src.read_u32());
        let notify_data = NowVarStr::decode(src)?;

        Ok(Self {
            app_state,
            reason_code,
            notify_data,
        })
    }
}

impl Encode for NowRdmAppNotifyMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::RDM,
            kind: NowRdmMsgKind::APP_NOTIFY.0,
            flags: 0,
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.app_state.value());
        dst.write_u32(self.reason_code.value());
        self.notify_data.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.body_size()
    }
}
