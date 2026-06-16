use alloc::borrow::Cow;

use ironrdp_core::{
    cast_length, ensure_fixed_part_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, IntoOwned,
    ReadCursor, WriteCursor,
};

use crate::{
    ComApartmentStateKind, NowExecMessage, NowExecMsgKind, NowExecWinPsFlags, NowHeader, NowMessage, NowMessageClass,
    NowVarStr,
};

/// The NOW_EXEC_PWSH_MSG message is used to execute a remote Windows PowerShell (powershell.exe) command.
///
/// NOW-PROTO: NOW_EXEC_PWSH_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowExecPwshMsg<'a> {
    flags: NowExecWinPsFlags,
    session_id: u32,
    command: NowVarStr<'a>,
    directory: NowVarStr<'a>,
    execution_policy: NowVarStr<'a>,
    configuration_name: NowVarStr<'a>,
}

impl_pdu_borrowing!(NowExecPwshMsg<'_>, OwnedNowExecPwshMsg);

impl IntoOwned for NowExecPwshMsg<'_> {
    type Owned = OwnedNowExecPwshMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowExecPwshMsg {
            flags: self.flags,
            session_id: self.session_id,
            command: self.command.into_owned(),
            directory: self.directory.into_owned(),
            execution_policy: self.execution_policy.into_owned(),
            configuration_name: self.configuration_name.into_owned(),
        }
    }
}

impl<'a> NowExecPwshMsg<'a> {
    const NAME: &'static str = "NOW_EXEC_PWSH_MSG";
    const FIXED_PART_SIZE: usize = 4;

    pub fn new(session_id: u32, command: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        let msg = Self {
            flags: NowExecWinPsFlags::empty(),
            session_id,
            command: NowVarStr::new(command)?,
            directory: NowVarStr::default(),
            execution_policy: NowVarStr::default(),
            configuration_name: NowVarStr::default(),
        };

        msg.ensure_message_size()?;

        Ok(msg)
    }

    pub fn new_server_mode(session_id: u32) -> EncodeResult<Self> {
        let mut msg = Self::new(session_id, "")?;
        msg.flags |= NowExecWinPsFlags::SERVER_MODE;
        Ok(msg)
    }

    pub fn with_directory(mut self, directory: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.flags |= NowExecWinPsFlags::DIRECTORY_SET;
        self.directory = NowVarStr::new(directory)?;

        self.ensure_message_size()?;

        Ok(self)
    }

    pub fn with_execution_policy(mut self, execution_policy: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.flags |= NowExecWinPsFlags::EXECUTION_POLICY;
        self.execution_policy = NowVarStr::new(execution_policy)?;

        self.ensure_message_size()?;

        Ok(self)
    }

    pub fn with_configuration_name(mut self, configuration_name: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        self.flags |= NowExecWinPsFlags::CONFIGURATION_NAME;
        self.configuration_name = NowVarStr::new(configuration_name)?;

        self.ensure_message_size()?;

        Ok(self)
    }

    #[must_use]
    pub fn set_no_logo(mut self) -> Self {
        self.flags |= NowExecWinPsFlags::NO_LOGO;
        self
    }

    #[must_use]
    pub fn set_no_exit(mut self) -> Self {
        self.flags |= NowExecWinPsFlags::NO_EXIT;
        self
    }

    #[must_use]
    pub fn set_no_profile(mut self) -> Self {
        self.flags |= NowExecWinPsFlags::NO_PROFILE;
        self
    }

    #[must_use]
    pub fn with_apartment_state(mut self, apartment_state: ComApartmentStateKind) -> Self {
        self.flags |= apartment_state.to_flags();
        self
    }

    fn ensure_message_size(&self) -> EncodeResult<()> {
        let _message_size = Self::FIXED_PART_SIZE
            .checked_add(self.command.size())
            .and_then(|size| size.checked_add(self.directory.size()))
            .and_then(|size| size.checked_add(self.execution_policy.size()))
            .and_then(|size| size.checked_add(self.configuration_name.size()))
            .ok_or_else(|| invalid_field_err!("size", "message size overflow"))?;

        Ok(())
    }

    pub fn session_id(&self) -> u32 {
        self.session_id
    }

    pub fn command(&self) -> &str {
        &self.command
    }

    pub fn directory(&self) -> Option<&str> {
        if self.flags.contains(NowExecWinPsFlags::DIRECTORY_SET) {
            Some(&self.directory)
        } else {
            None
        }
    }

    pub fn execution_policy(&self) -> Option<&str> {
        if self.flags.contains(NowExecWinPsFlags::EXECUTION_POLICY) {
            Some(&self.execution_policy)
        } else {
            None
        }
    }

    pub fn configuration_name(&self) -> Option<&str> {
        if self.flags.contains(NowExecWinPsFlags::CONFIGURATION_NAME) {
            Some(&self.configuration_name)
        } else {
            None
        }
    }

    #[must_use]
    pub fn with_io_redirection(mut self) -> Self {
        self.flags |= NowExecWinPsFlags::IO_REDIRECTION;
        self
    }

    pub fn is_with_io_redirection(&self) -> bool {
        self.flags.contains(NowExecWinPsFlags::IO_REDIRECTION)
    }

    #[must_use]
    pub fn with_raw_encoding(mut self) -> Self {
        self.flags |= NowExecWinPsFlags::RAW_ENCODING;
        self
    }

    pub fn is_raw_encoding(&self) -> bool {
        self.flags.contains(NowExecWinPsFlags::RAW_ENCODING)
    }

    #[must_use]
    pub fn with_unicode_console(mut self) -> Self {
        self.flags |= NowExecWinPsFlags::UNICODE_CONSOLE;
        self
    }

    pub fn is_unicode_console(&self) -> bool {
        self.flags.contains(NowExecWinPsFlags::UNICODE_CONSOLE)
    }

    #[must_use]
    pub fn with_detached(mut self) -> Self {
        self.flags |= NowExecWinPsFlags::DETACHED;
        self
    }

    pub fn is_detached(&self) -> bool {
        self.flags.contains(NowExecWinPsFlags::DETACHED)
    }

    pub fn is_no_logo(&self) -> bool {
        self.flags.contains(NowExecWinPsFlags::NO_LOGO)
    }

    pub fn is_no_exit(&self) -> bool {
        self.flags.contains(NowExecWinPsFlags::NO_EXIT)
    }

    pub fn is_no_profile(&self) -> bool {
        self.flags.contains(NowExecWinPsFlags::NO_PROFILE)
    }

    pub fn is_non_interactive(&self) -> bool {
        self.flags.contains(NowExecWinPsFlags::NON_INTERACTIVE)
    }

    pub fn is_server_mode(&self) -> bool {
        self.flags.contains(NowExecWinPsFlags::SERVER_MODE)
    }

    pub fn apartment_state(&self) -> DecodeResult<Option<ComApartmentStateKind>> {
        ComApartmentStateKind::from_flags(self.flags)
    }

    // LINTS: Overall message size is validated in the constructor/decode method
    #[allow(clippy::arithmetic_side_effects)]
    fn body_size(&self) -> usize {
        Self::FIXED_PART_SIZE
            + self.command.size()
            + self.directory.size()
            + self.execution_policy.size()
            + self.configuration_name.size()
    }

    pub(super) fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'a>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let flags = NowExecWinPsFlags::from_bits_retain(header.flags);
        let session_id = src.read_u32();
        let command = NowVarStr::decode(src)?;
        let directory = NowVarStr::decode(src)?;
        let execution_policy = NowVarStr::decode(src)?;
        let configuration_name = NowVarStr::decode(src)?;

        let msg = Self {
            flags,
            session_id,
            command,
            directory,
            execution_policy,
            configuration_name,
        };

        Ok(msg)
    }
}

impl Encode for NowExecPwshMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::EXEC,
            kind: NowExecMsgKind::PWSH.0,
            flags: self.flags.bits(),
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);
        dst.write_u32(self.session_id);
        self.command.encode(dst)?;
        self.directory.encode(dst)?;
        self.execution_policy.encode(dst)?;
        self.configuration_name.encode(dst)?;

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

impl<'de> Decode<'de> for NowExecPwshMsg<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowExecMsgKind(header.kind)) {
            (NowMessageClass::EXEC, NowExecMsgKind::PWSH) => Self::decode_from_body(header, src),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl<'a> From<NowExecPwshMsg<'a>> for NowMessage<'a> {
    fn from(msg: NowExecPwshMsg<'a>) -> Self {
        NowMessage::Exec(NowExecMessage::Pwsh(msg))
    }
}
