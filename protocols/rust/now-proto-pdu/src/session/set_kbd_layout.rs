use alloc::borrow::Cow;

use bitflags::bitflags;
use ironrdp_core::{
    cast_length, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor,
};

use crate::{NowHeader, NowMessage, NowMessageClass, NowSessionMessage, NowSessionMessageKind, NowVarStr};

bitflags! {
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    pub struct NowSessionSetKbdLayoutFlags: u16 {
        /// Switches to next keyboard layout. kbdLayoutId field should contain empty string.
        /// Conflicts with NOW_SET_KBD_LAYOUT_FLAG_PREV.
        ///
        /// NOW_PROTO: NOW_SET_KBD_LAYOUT_FLAG_NEXT
        const NEXT_LAYOUT = 0x0001;

        /// Switches to previous keyboard layout. kbdLayoutId field should contain empty string.
        /// Conflicts with NOW_SET_KBD_LAYOUT_FLAG_NEXT.
        ///
        /// NOW_PROTO: NOW_SET_KBD_LAYOUT_FLAG_PREV
        const PREV_LAYOUT = 0x0002;
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SetKbdLayoutOption<'a> {
    Next,
    Prev,
    Specific(&'a str),
}

/// The NOW_SESSION_SET_KBD_LAYOUT_MSG message is used to set the keyboard layout for the active
/// foreground window. The request is fire-and-forget, invalid layout identifiers are ignored.
///
/// NOW_PROTO: NOW_SESSION_SET_KBD_LAYOUT_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
#[non_exhaustive]
pub struct NowSessionSetKbdLayoutMsg<'a> {
    flags: NowSessionSetKbdLayoutFlags,
    layout: NowVarStr<'a>,
}

impl_pdu_borrowing!(NowSessionSetKbdLayoutMsg<'_>, OwnedNowSessionSetKbdLayoutMsg);

impl IntoOwned for NowSessionSetKbdLayoutMsg<'_> {
    type Owned = NowSessionSetKbdLayoutMsg<'static>;

    fn into_owned(self) -> Self::Owned {
        NowSessionSetKbdLayoutMsg {
            flags: self.flags,
            layout: self.layout.into_owned(),
        }
    }
}

impl<'a> NowSessionSetKbdLayoutMsg<'a> {
    const NAME: &'static str = "NOW_SESSION_SET_KBD_LAYOUT_MSG";

    pub fn new_next() -> Self {
        Self {
            flags: NowSessionSetKbdLayoutFlags::NEXT_LAYOUT,
            layout: NowVarStr::default(),
        }
    }

    pub fn new_prev() -> Self {
        Self {
            flags: NowSessionSetKbdLayoutFlags::PREV_LAYOUT,
            layout: NowVarStr::default(),
        }
    }

    pub fn new_specific(layout: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        let layout = NowVarStr::new(layout.into())?;

        ensure_now_message_size!(layout.size());

        Ok(Self {
            flags: NowSessionSetKbdLayoutFlags::empty(),
            layout,
        })
    }

    pub fn layout(&'a self) -> SetKbdLayoutOption<'a> {
        if self.flags.contains(NowSessionSetKbdLayoutFlags::NEXT_LAYOUT) {
            return SetKbdLayoutOption::Next;
        }

        if self.flags.contains(NowSessionSetKbdLayoutFlags::PREV_LAYOUT) {
            return SetKbdLayoutOption::Prev;
        }

        SetKbdLayoutOption::Specific(&self.layout)
    }

    fn body_size(&self) -> usize {
        self.layout.size()
    }

    pub(super) fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        let flags = NowSessionSetKbdLayoutFlags::from_bits_retain(header.flags);
        let layout = NowVarStr::decode(src)?;

        if flags.contains(NowSessionSetKbdLayoutFlags::NEXT_LAYOUT)
            && flags.contains(NowSessionSetKbdLayoutFlags::PREV_LAYOUT)
        {
            return Err(invalid_field_err!("flags", "both NEXT and PREV flags are set"));
        }

        Ok(Self { flags, layout })
    }
}

impl Encode for NowSessionSetKbdLayoutMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::SESSION,
            kind: NowSessionMessageKind::SET_KBD_LAYOUT.0,
            flags: self.flags.bits(),
        };

        header.encode(dst)?;
        self.layout.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.body_size()
    }
}

impl<'de> Decode<'de> for NowSessionSetKbdLayoutMsg<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowSessionMessageKind(header.kind)) {
            (NowMessageClass::SESSION, NowSessionMessageKind::SET_KBD_LAYOUT) => Self::decode_from_body(header, src),
            _ => Err(unsupported_message_err!(class: header.class.0, kind: header.kind)),
        }
    }
}

impl<'a> From<NowSessionSetKbdLayoutMsg<'a>> for NowMessage<'a> {
    fn from(msg: NowSessionSetKbdLayoutMsg<'a>) -> Self {
        NowMessage::Session(NowSessionMessage::SetKbdLayout(msg))
    }
}
