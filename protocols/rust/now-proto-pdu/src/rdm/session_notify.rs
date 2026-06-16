use alloc::borrow::Cow;
use ironrdp_core::{
    cast_length, ensure_fixed_part_size, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor,
};

use crate::core::NowGuid;
use crate::{NowHeader, NowMessageClass, NowRdmMsgKind, NowVarStr};

/// NOW-PROTO: Session notify values for NOW_RDM_SESSION_NOTIFY_MSG
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NowRdmSessionNotifyKind(u32);

impl NowRdmSessionNotifyKind {
    /// The session has been closed
    pub const CLOSE: Self = Self(0x00000001);
    /// The session has been focused
    pub const FOCUS: Self = Self(0x00000002);

    pub(crate) fn new(notify: u32) -> Self {
        Self(notify)
    }

    pub(crate) fn value(&self) -> u32 {
        self.0
    }
}

/// The NOW_RDM_SESSION_NOTIFY_MSG is used by the server to notify of a session state change, such as a session closing, or a session focus change.
///
/// NOW-PROTO: NOW_RDM_SESSION_NOTIFY_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowRdmSessionNotifyMsg<'a> {
    kind: NowRdmSessionNotifyKind,
    session_id: NowGuid,
    log_data: NowVarStr<'a>,
}

pub type OwnedNowRdmSessionNotifyMsg = NowRdmSessionNotifyMsg<'static>;

impl IntoOwned for NowRdmSessionNotifyMsg<'_> {
    type Owned = OwnedNowRdmSessionNotifyMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowRdmSessionNotifyMsg {
            kind: self.kind,
            session_id: self.session_id,
            log_data: self.log_data.into_owned(),
        }
    }
}

impl<'a> NowRdmSessionNotifyMsg<'a> {
    const NAME: &'static str = "NOW_RDM_SESSION_NOTIFY_MSG";
    const FIXED_PART_SIZE: usize = 4; // 4 bytes session_notify + variable GUID + variable log_data

    pub fn new(kind: NowRdmSessionNotifyKind, session_id: uuid::Uuid) -> Self {
        Self {
            kind,
            session_id: NowGuid::new(session_id),
            log_data: NowVarStr::new("").expect("empty string should always be valid"),
        }
    }

    pub fn with_log_data(mut self, log_data: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.log_data = NowVarStr::new(log_data)?;
        ensure_now_message_size!(Self::FIXED_PART_SIZE, self.session_id.size() + self.log_data.size());
        Ok(self)
    }

    /// Create a message to notify that a session has been closed
    pub fn new_close(session_id: uuid::Uuid) -> Self {
        Self::new(NowRdmSessionNotifyKind::CLOSE, session_id)
    }

    /// Create a message to notify that a session has been focused
    pub fn new_focus(session_id: uuid::Uuid) -> Self {
        Self::new(NowRdmSessionNotifyKind::FOCUS, session_id)
    }

    pub fn session_notify(&self) -> NowRdmSessionNotifyKind {
        self.kind
    }

    pub fn session_id(&self) -> uuid::Uuid {
        self.session_id.as_uuid()
    }

    pub fn log_data(&self) -> &str {
        &self.log_data
    }

    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.session_id.size() + self.log_data.size()
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let session_notify = NowRdmSessionNotifyKind::new(src.read_u32());
        let session_id = NowGuid::decode(src)?;
        let log_data = NowVarStr::decode(src)?;

        Ok(Self {
            kind: session_notify,
            session_id,
            log_data,
        })
    }
}

impl Encode for NowRdmSessionNotifyMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::RDM,
            kind: NowRdmMsgKind::SESSION_NOTIFY.0,
            flags: 0,
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.kind.value());
        self.session_id.encode(dst)?;
        self.log_data.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.body_size()
    }
}
