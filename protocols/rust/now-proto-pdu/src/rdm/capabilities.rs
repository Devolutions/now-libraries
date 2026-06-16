use alloc::borrow::Cow;
use bitflags::bitflags;
use ironrdp_core::{
    cast_length, ensure_fixed_part_size, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor,
};

use crate::{NowHeader, NowMessageClass, NowRdmMsgKind, NowVarStr};

bitflags! {
    /// NOW-PROTO: NOW_RDM_CAPABILITIES_MSG sync_flags field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    struct NowRdmSyncFlags: u32 {
        /// RDM application is available. Only sent by the server to the client.
        ///
        /// NOW-PROTO: NOW_RDM_SYNC_FLAG_APP_AVAILABLE
        const APP_AVAILABLE = 0x00000001;
    }
}

/// The NOW_RDM_CAPABILITIES_MSG message is used to exchange RDM capabilities between the client
/// and the server during the capability negotiation phase.
///
/// NOW-PROTO: NOW_RDM_CAPABILITIES_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowRdmCapabilitiesMsg<'a> {
    timestamp: u64,
    sync_flags: NowRdmSyncFlags,
    rdm_version: NowVarStr<'a>,
    version_extra: NowVarStr<'a>,
}

pub type OwnedNowRdmCapabilitiesMsg = NowRdmCapabilitiesMsg<'static>;

impl IntoOwned for NowRdmCapabilitiesMsg<'_> {
    type Owned = OwnedNowRdmCapabilitiesMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowRdmCapabilitiesMsg {
            timestamp: self.timestamp,
            sync_flags: self.sync_flags,
            rdm_version: self.rdm_version.into_owned(),
            version_extra: self.version_extra.into_owned(),
        }
    }
}

impl<'a> NowRdmCapabilitiesMsg<'a> {
    const NAME: &'static str = "NOW_RDM_CAPABILITIES_MSG";
    const FIXED_PART_SIZE: usize = 12; // 8 + 4 bytes

    pub fn new(timestamp: u64, rdm_version: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        let rdm_version = NowVarStr::new(rdm_version)?;

        let msg = Self {
            timestamp,
            sync_flags: NowRdmSyncFlags::empty(),
            rdm_version,
            version_extra: NowVarStr::default(),
        };

        ensure_now_message_size!(Self::FIXED_PART_SIZE, msg.rdm_version.size(), msg.version_extra.size());

        Ok(msg)
    }

    pub fn with_version_extra(mut self, version_extra: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.version_extra = NowVarStr::new(version_extra)?;

        ensure_now_message_size!(
            Self::FIXED_PART_SIZE,
            self.rdm_version.size(),
            self.version_extra.size()
        );

        Ok(self)
    }

    #[must_use]
    pub fn with_app_available(mut self) -> Self {
        self.sync_flags |= NowRdmSyncFlags::APP_AVAILABLE;
        self
    }

    pub fn timestamp(&self) -> u64 {
        self.timestamp
    }

    pub fn is_app_available(&self) -> bool {
        self.sync_flags.contains(NowRdmSyncFlags::APP_AVAILABLE)
    }

    pub fn rdm_version(&self) -> &str {
        &self.rdm_version
    }

    pub fn version_extra(&self) -> &str {
        &self.version_extra
    }

    pub(super) fn decode_from_body(_header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let timestamp = src.read_u64();
        let sync_flags = NowRdmSyncFlags::from_bits_retain(src.read_u32());
        let rdm_version = NowVarStr::decode(src)?;
        let version_extra = NowVarStr::decode(src)?;

        Ok(Self {
            timestamp,
            sync_flags,
            rdm_version,
            version_extra,
        })
    }

    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.rdm_version.size() + self.version_extra.size()
    }
}

impl Encode for NowRdmCapabilitiesMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::RDM,
            kind: NowRdmMsgKind::CAPABILITIES.0,
            flags: 0,
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u64(self.timestamp);
        dst.write_u32(self.sync_flags.bits());
        self.rdm_version.encode(dst)?;
        self.version_extra.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.body_size()
    }
}
