use alloc::borrow::Cow;

use bitflags::bitflags;
use ironrdp_core::{
    cast_length, ensure_fixed_part_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, IntoOwned,
    ReadCursor, WriteCursor,
};

use crate::{NowExecMessage, NowExecMsgKind, NowHeader, NowMessage, NowMessageClass, NowVarStr};

bitflags! {
    /// NOW-PROTO: NOW_EXEC_PROCESS_MSG msgFlags field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    struct NowExecProcessFlags: u16 {
        /// Set if parameters field contains non-default value.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PROCESS_PARAMETERS_SET
        const PARAMETERS_SET = 0x0001;

        /// Set if directory field contains non-default value.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PROCESS_DIRECTORY_SET
        const DIRECTORY_SET = 0x0002;

        /// Enables OEM-to-UTF-8 transcoding for stdin, stdout, and stderr.
        /// Without this flag, data streams are passed through as raw bytes.
        /// Does NOT guarantee UTF-8 character boundaries are preserved in data messages;
        /// the client must manage incomplete multi-byte sequences across message boundaries.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PROCESS_ENCODING_UTF8
        const ENCODING_UTF8 = 0x0004;

        /// Enable stdio (stdout, stderr, stdin) redirection.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PROCESS_IO_REDIRECTION
        const IO_REDIRECTION = 0x1000;

        /// Detached mode: the process is started without tracking execution or sending back output.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PROCESS_DETACHED
        const DETACHED = 0x8000;
    }
}

/// The NOW_EXEC_PROCESS_MSG message is used to send a Windows CreateProcess() request.
///
/// NOW-PROTO: NOW_EXEC_PROCESS_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowExecProcessMsg<'a> {
    flags: NowExecProcessFlags,
    session_id: u32,
    filename: NowVarStr<'a>,
    parameters: NowVarStr<'a>,
    directory: NowVarStr<'a>,
}

impl_pdu_borrowing!(NowExecProcessMsg<'_>, OwnedNowExecProcessMsg);

impl IntoOwned for NowExecProcessMsg<'_> {
    type Owned = OwnedNowExecProcessMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowExecProcessMsg {
            flags: self.flags,
            session_id: self.session_id,
            filename: self.filename.into_owned(),
            parameters: self.parameters.into_owned(),
            directory: self.directory.into_owned(),
        }
    }
}

impl<'a> NowExecProcessMsg<'a> {
    const NAME: &'static str = "NOW_EXEC_PROCESS_MSG";
    const FIXED_PART_SIZE: usize = 4;

    pub fn new(session_id: u32, filename: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        let msg = Self {
            flags: NowExecProcessFlags::empty(),
            session_id,
            filename: NowVarStr::new(filename)?,
            parameters: NowVarStr::default(),
            directory: NowVarStr::default(),
        };

        msg.ensure_message_size()?;

        Ok(msg)
    }

    pub fn with_parameters(mut self, parameters: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.flags |= NowExecProcessFlags::PARAMETERS_SET;
        self.parameters = NowVarStr::new(parameters)?;

        self.ensure_message_size()?;

        Ok(self)
    }

    pub fn with_directory(mut self, directory: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.flags |= NowExecProcessFlags::DIRECTORY_SET;
        self.directory = NowVarStr::new(directory)?;

        self.ensure_message_size()?;

        Ok(self)
    }

    #[must_use]
    pub fn with_io_redirection(mut self) -> Self {
        self.flags |= NowExecProcessFlags::IO_REDIRECTION;
        self
    }

    pub fn is_with_io_redirection(&self) -> bool {
        self.flags.contains(NowExecProcessFlags::IO_REDIRECTION)
    }

    #[must_use]
    pub fn with_encoding_utf8(mut self) -> Self {
        self.flags |= NowExecProcessFlags::ENCODING_UTF8;
        self
    }

    pub fn is_encoding_utf8(&self) -> bool {
        self.flags.contains(NowExecProcessFlags::ENCODING_UTF8)
    }

    #[must_use]
    pub fn with_detached(mut self) -> Self {
        self.flags |= NowExecProcessFlags::DETACHED;
        self
    }

    pub fn is_detached(&self) -> bool {
        self.flags.contains(NowExecProcessFlags::DETACHED)
    }

    fn ensure_message_size(&self) -> EncodeResult<()> {
        ensure_now_message_size!(
            Self::FIXED_PART_SIZE,
            self.filename.size(),
            self.parameters.size(),
            self.directory.size()
        );

        Ok(())
    }

    pub fn session_id(&self) -> u32 {
        self.session_id
    }

    pub fn filename(&self) -> &str {
        &self.filename
    }

    pub fn parameters(&self) -> Option<&str> {
        if self.flags.contains(NowExecProcessFlags::PARAMETERS_SET) {
            Some(&self.parameters)
        } else {
            None
        }
    }

    pub fn directory(&self) -> Option<&str> {
        if self.flags.contains(NowExecProcessFlags::DIRECTORY_SET) {
            Some(&self.directory)
        } else {
            None
        }
    }

    // LINTS: Overall message size is validated in the constructor/decode method
    #[allow(clippy::arithmetic_side_effects)]
    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.filename.size() + self.parameters.size() + self.directory.size()
    }

    pub(super) fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let flags = NowExecProcessFlags::from_bits_retain(header.flags);
        let session_id = src.read_u32();
        let filename = NowVarStr::decode(src)?;
        let parameters = NowVarStr::decode(src)?;
        let directory = NowVarStr::decode(src)?;

        let msg = Self {
            flags,
            session_id,
            filename,
            parameters,
            directory,
        };

        Ok(msg)
    }
}

impl Encode for NowExecProcessMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::EXEC,
            kind: NowExecMsgKind::PROCESS.0,
            flags: self.flags.bits(),
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.session_id);
        self.filename.encode(dst)?;
        self.parameters.encode(dst)?;
        self.directory.encode(dst)?;

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

impl<'de> Decode<'de> for NowExecProcessMsg<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowExecMsgKind(header.kind)) {
            (NowMessageClass::EXEC, NowExecMsgKind::PROCESS) => Self::decode_from_body(header, src),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl<'a> From<NowExecProcessMsg<'a>> for NowMessage<'a> {
    fn from(msg: NowExecProcessMsg<'a>) -> Self {
        NowMessage::Exec(NowExecMessage::Process(msg))
    }
}
