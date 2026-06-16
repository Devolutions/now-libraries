use alloc::borrow::Cow;
use ironrdp_core::{
    cast_length, ensure_fixed_part_size, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor,
};

use crate::core::NowGuid;
use crate::{NowHeader, NowMessageClass, NowRdmMsgKind, NowVarStr};

/// The NOW_RDM_SESSION_START_MSG message is used to start a new RDM session.
///
/// NOW-PROTO: NOW_RDM_SESSION_START_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowRdmSessionStartMsg<'a> {
    session_id: NowGuid,
    connection_id: NowGuid,
    connection_data: NowVarStr<'a>,
}

pub type OwnedNowRdmSessionStartMsg = NowRdmSessionStartMsg<'static>;

impl IntoOwned for NowRdmSessionStartMsg<'_> {
    type Owned = OwnedNowRdmSessionStartMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowRdmSessionStartMsg {
            session_id: self.session_id,
            connection_id: self.connection_id,
            connection_data: self.connection_data.into_owned(),
        }
    }
}

impl<'a> NowRdmSessionStartMsg<'a> {
    const NAME: &'static str = "NOW_RDM_SESSION_START_MSG";
    const FIXED_PART_SIZE: usize = 0; // All fields are variable size (2 GUIDs + 1 string)

    pub fn new(
        session_id: uuid::Uuid,
        connection_id: uuid::Uuid,
        connection_data: impl Into<Cow<'a, str>>,
    ) -> EncodeResult<Self> {
        let connection_data = NowVarStr::new(connection_data)?;

        let msg = Self {
            session_id: NowGuid::new(session_id),
            connection_id: NowGuid::new(connection_id),
            connection_data,
        };

        ensure_now_message_size!(
            Self::FIXED_PART_SIZE,
            msg.session_id.size(),
            msg.connection_id.size(),
            msg.connection_data.size()
        );

        Ok(msg)
    }

    pub fn session_id(&self) -> uuid::Uuid {
        self.session_id.as_uuid()
    }

    pub fn connection_id(&self) -> uuid::Uuid {
        self.connection_id.as_uuid()
    }

    pub fn connection_data(&self) -> &str {
        &self.connection_data
    }

    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.session_id.size() + self.connection_id.size() + self.connection_data.size()
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let session_id = NowGuid::decode(src)?;
        let connection_id = NowGuid::decode(src)?;
        let connection_data = NowVarStr::decode(src)?;

        Ok(Self {
            session_id,
            connection_id,
            connection_data,
        })
    }
}

impl Encode for NowRdmSessionStartMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::RDM,
            kind: NowRdmMsgKind::SESSION_START.0,
            flags: 0,
        };

        header.encode(dst)?;
        self.session_id.encode(dst)?;
        self.connection_id.encode(dst)?;
        self.connection_data.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.body_size()
    }
}
