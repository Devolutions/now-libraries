use alloc::borrow::Cow;

use bitflags::bitflags;
use ironrdp_core::{
    cast_length, ensure_fixed_part_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, IntoOwned,
    ReadCursor, WriteCursor,
};

use crate::{NowExecMessage, NowExecMsgKind, NowHeader, NowMessage, NowMessageClass, NowVarStr};

bitflags! {
    /// NOW-PROTO: NOW_EXEC_SHELL_MSG msgFlags field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    struct NowExecShellFlags: u16 {
        /// Set if parameters shell contains non-default value.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_SHELL_SHELL_SET
        const PARAMETERS_SET = 0x0001;

        /// Set if directory field contains non-default value.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_SHELL_DIRECTORY_SET
        const DIRECTORY_SET = 0x0002;

        /// Enable stdio (stdout, stderr, stdin) redirection.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_SHELL_IO_REDIRECTION
        const IO_REDIRECTION = 0x1000;

        /// Detached mode: the shell is started without tracking execution or sending back output.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_SHELL_DETACHED
        const DETACHED = 0x8000;
    }
}

/// The NOW_EXEC_SHELL_MSG message is used to execute a remote shell command.
///
/// NOW-PROTO: NOW_EXEC_SHELL_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowExecShellMsg<'a> {
    flags: NowExecShellFlags,
    session_id: u32,
    command: NowVarStr<'a>,
    shell: NowVarStr<'a>,
    directory: NowVarStr<'a>,
}

impl_pdu_borrowing!(NowExecShellMsg<'_>, OwnedNowExecShellMsg);

impl IntoOwned for NowExecShellMsg<'_> {
    type Owned = OwnedNowExecShellMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowExecShellMsg {
            flags: self.flags,
            session_id: self.session_id,
            command: self.command.into_owned(),
            shell: self.shell.into_owned(),
            directory: self.directory.into_owned(),
        }
    }
}

impl<'a> NowExecShellMsg<'a> {
    const NAME: &'static str = "NOW_EXEC_SHELL_MSG";
    const FIXED_PART_SIZE: usize = 4;

    pub fn new(session_id: u32, command: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        let msg = Self {
            flags: NowExecShellFlags::empty(),
            session_id,
            command: NowVarStr::new(command)?,
            shell: NowVarStr::default(),
            directory: NowVarStr::default(),
        };

        msg.ensure_message_size()?;

        Ok(msg)
    }

    pub fn with_shell(mut self, shell: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.flags |= NowExecShellFlags::PARAMETERS_SET;
        self.shell = NowVarStr::new(shell)?;

        self.ensure_message_size()?;

        Ok(self)
    }

    pub fn with_directory(mut self, directory: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.flags |= NowExecShellFlags::DIRECTORY_SET;
        self.directory = NowVarStr::new(directory)?;

        self.ensure_message_size()?;

        Ok(self)
    }

    fn ensure_message_size(&self) -> EncodeResult<()> {
        ensure_now_message_size!(
            Self::FIXED_PART_SIZE,
            self.command.size(),
            self.shell.size(),
            self.directory.size()
        );

        Ok(())
    }

    pub fn session_id(&self) -> u32 {
        self.session_id
    }

    pub fn command(&self) -> &str {
        &self.command
    }

    pub fn shell(&self) -> Option<&str> {
        if self.flags.contains(NowExecShellFlags::PARAMETERS_SET) {
            Some(&self.shell)
        } else {
            None
        }
    }

    pub fn directory(&self) -> Option<&str> {
        if self.flags.contains(NowExecShellFlags::DIRECTORY_SET) {
            Some(&self.directory)
        } else {
            None
        }
    }

    #[must_use]
    pub fn with_io_redirection(mut self) -> Self {
        self.flags |= NowExecShellFlags::IO_REDIRECTION;
        self
    }

    pub fn is_with_io_redirection(&self) -> bool {
        self.flags.contains(NowExecShellFlags::IO_REDIRECTION)
    }

    #[must_use]
    pub fn with_detached(mut self) -> Self {
        self.flags |= NowExecShellFlags::DETACHED;
        self
    }

    pub fn is_detached(&self) -> bool {
        self.flags.contains(NowExecShellFlags::DETACHED)
    }

    // LINTS: Overall message size is validated in the constructor/decode method
    #[allow(clippy::arithmetic_side_effects)]
    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE + self.command.size() + self.shell.size() + self.directory.size()
    }

    pub(super) fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let flags = NowExecShellFlags::from_bits_retain(header.flags);
        let session_id = src.read_u32();
        let command = NowVarStr::decode(src)?;
        let shell = NowVarStr::decode(src)?;
        let directory = NowVarStr::decode(src)?;

        let msg = Self {
            flags,
            session_id,
            command,
            shell,
            directory,
        };

        Ok(msg)
    }
}

impl Encode for NowExecShellMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::EXEC,
            kind: NowExecMsgKind::SHELL.0,
            flags: self.flags.bits(),
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.session_id);
        self.command.encode(dst)?;
        self.shell.encode(dst)?;
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

impl<'de> Decode<'de> for NowExecShellMsg<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowExecMsgKind(header.kind)) {
            (NowMessageClass::EXEC, NowExecMsgKind::SHELL) => Self::decode_from_body(header, src),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl<'a> From<NowExecShellMsg<'a>> for NowMessage<'a> {
    fn from(msg: NowExecShellMsg<'a>) -> Self {
        NowMessage::Exec(NowExecMessage::Shell(msg))
    }
}
