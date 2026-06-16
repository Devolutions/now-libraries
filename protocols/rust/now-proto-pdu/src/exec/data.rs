use alloc::borrow::Cow;

use bitflags::bitflags;
use ironrdp_core::{
    cast_length, ensure_fixed_part_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, IntoOwned,
    ReadCursor, WriteCursor,
};

use crate::{NowExecMessage, NowExecMsgKind, NowHeader, NowMessage, NowMessageClass, NowVarBuf};

bitflags! {
    /// NOW-PROTO: NOW_EXEC_DATA_MSG flags field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    struct NowExecDataFlags: u16 {
        /// This is the last data message, the command completed execution.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_DATA_LAST
        const LAST = 0x0001;
        /// The data is from the standard input.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_DATA_STDIN
        const STDIN = 0x0002;
        /// The data is from the standard output.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_DATA_STDOUT
        const STDOUT = 0x0004;
        /// The data is from the standard error.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_DATA_STDERR
        const STDERR = 0x0008;
    }
}

/// Redirected std stream kind for the NOW_EXEC_DATA_MSG message.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NowExecDataStreamKind {
    Stdin,
    Stdout,
    Stderr,
}

impl NowExecDataStreamKind {
    fn to_flags(self) -> NowExecDataFlags {
        match self {
            NowExecDataStreamKind::Stdin => NowExecDataFlags::STDIN,
            NowExecDataStreamKind::Stdout => NowExecDataFlags::STDOUT,
            NowExecDataStreamKind::Stderr => NowExecDataFlags::STDERR,
        }
    }

    fn from_flags(flags: NowExecDataFlags) -> Option<Self> {
        let flags = flags & (NowExecDataFlags::STDIN | NowExecDataFlags::STDOUT | NowExecDataFlags::STDERR);

        // Exactly one stream kind flag should be set
        if flags.iter().count() != 1 {
            return None;
        }

        match flags {
            NowExecDataFlags::STDIN => Some(NowExecDataStreamKind::Stdin),
            NowExecDataFlags::STDOUT => Some(NowExecDataStreamKind::Stdout),
            NowExecDataFlags::STDERR => Some(NowExecDataStreamKind::Stderr),
            _ => unreachable!("validated by code above"),
        }
    }
}

/// The NOW_EXEC_DATA_MSG message is used to send input/output data as part of a remote execution.
///
/// NOW-PROTO: NOW_EXEC_DATA_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowExecDataMsg<'a> {
    flags: NowExecDataFlags,
    session_id: u32,
    data: NowVarBuf<'a>,
}

impl_pdu_borrowing!(NowExecDataMsg<'_>, OwnedNowExecDataMsg);

impl IntoOwned for NowExecDataMsg<'_> {
    type Owned = OwnedNowExecDataMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowExecDataMsg {
            flags: self.flags,
            session_id: self.session_id,
            data: self.data.into_owned(),
        }
    }
}

impl<'a> NowExecDataMsg<'a> {
    const NAME: &'static str = "NOW_EXEC_DATA_MSG";
    const FIXED_PART_SIZE: usize = 4;

    pub fn new(
        session_id: u32,
        stream: NowExecDataStreamKind,
        last: bool,
        data: impl Into<Cow<'a, [u8]>>,
    ) -> EncodeResult<Self> {
        let mut flags = stream.to_flags();
        if last {
            flags |= NowExecDataFlags::LAST;
        }

        let msg = Self {
            flags,
            session_id,
            data: NowVarBuf::new(data)?,
        };

        ensure_now_message_size!(Self::FIXED_PART_SIZE, msg.data.size());

        Ok(msg)
    }

    pub fn stream_kind(&self) -> DecodeResult<NowExecDataStreamKind> {
        NowExecDataStreamKind::from_flags(self.flags).ok_or_else(|| invalid_field_err!("flags", "invalid stream kind"))
    }

    pub fn is_last(&self) -> bool {
        self.flags.contains(NowExecDataFlags::LAST)
    }

    pub fn session_id(&self) -> u32 {
        self.session_id
    }

    pub fn data(&self) -> &[u8] {
        &self.data
    }

    // LINTS: Overall message size always fits into usize; VarBuf size always a few powers of 2 less
    // than u32::MAX, therefore it fits into usize
    #[allow(clippy::arithmetic_side_effects)]
    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.data.size()
    }

    pub(super) fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let flags = NowExecDataFlags::from_bits_retain(header.flags);
        let session_id = src.read_u32();
        let data = NowVarBuf::decode(src)?;

        Ok(Self {
            flags,
            session_id,
            data,
        })
    }
}

impl Encode for NowExecDataMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::EXEC,
            kind: NowExecMsgKind::DATA.0,
            flags: self.flags.bits(),
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.session_id);
        self.data.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    // LINTS: See body_size()
    #[allow(clippy::arithmetic_side_effects)]
    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.body_size()
    }
}

impl<'de> Decode<'de> for NowExecDataMsg<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowExecMsgKind(header.kind)) {
            (NowMessageClass::EXEC, NowExecMsgKind::DATA) => Self::decode_from_body(header, src),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl<'de> From<NowExecDataMsg<'de>> for NowMessage<'de> {
    fn from(msg: NowExecDataMsg<'de>) -> Self {
        NowMessage::Exec(NowExecMessage::Data(msg))
    }
}
