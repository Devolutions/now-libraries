use core::time;

use bitflags::bitflags;
use ironrdp_core::{
    ensure_fixed_part_size, invalid_field_err, Decode, DecodeResult, Encode, EncodeResult, ReadCursor, WriteCursor,
};

use crate::{NowChannelMessage, NowChannelMsgKind, NowHeader, NowMessage, NowMessageClass};

bitflags! {
    /// NOW-PROTO: NOW_CHANNEL_CAPSET_MSG flags field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    pub struct NowChannelCapsetFlags: u16 {
        /// Set if heartbeat specify channel heartbeat interval.
        ///
        /// NOW-PROTO: NOW_CHANNEL_SET_HEARTBEAT
        const SET_HEARTBEAT = 0x0001;
    }
}

bitflags! {
    /// NOW-PROTO: NOW_CHANNEL_CAPSET_MSG systemCapset field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    pub struct NowSystemCapsetFlags: u16 {
        /// System shutdown command support.
        ///
        /// NOW-PROTO: NOW_CAP_SYSTEM_SHUTDOWN
        const SHUTDOWN = 0x0001;
    }
}

bitflags! {
    /// NOW-PROTO: NOW_CHANNEL_CAPSET_MSG sessionCapset field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    pub struct NowSessionCapsetFlags: u16 {
        /// Session lock command support.
        ///
        /// NOW-PROTO: NOW_CAP_SESSION_LOCK
        const LOCK = 0x0001;
        /// Session logoff command support.
        ///
        /// NOW-PROTO: NOW_CAP_SESSION_LOGOFF
        const LOGOFF = 0x0002;
        /// Message box command support.
        ///
        /// NOW-PROTO: NOW_CAP_SESSION_MSGBOX
        const MSGBOX = 0x0004;
        /// Set keyboard layout command support.
        ///
        /// NOW-PROTO: NOW_CAP_SESSION_SET_KBD_LAYOUT
        const SET_KBD_LAYOUT = 0x0008;
        /// Window recording command support.
        ///
        /// NOW-PROTO: NOW_CAP_SESSION_WINDOW_RECORDING
        const WINDOW_RECORDING = 0x0010;
    }
}

bitflags! {
    /// NOW-PROTO: NOW_CHANNEL_CAPSET_MSG execCapset field.
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    pub struct NowExecCapsetFlags: u16 {
        /// Generic "Run" execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_RUN
        const STYLE_RUN = 0x0001;
        /// CreateProcess() execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_PROCESS
        const STYLE_PROCESS = 0x0002;
        /// System shell (.sh) execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_SHELL
        const STYLE_SHELL = 0x0004;
        /// Windows batch file (.bat) execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_BATCH
        const STYLE_BATCH = 0x00008;
        /// Windows PowerShell (.ps1) execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_WINPS
        const STYLE_WINPS = 0x0010;
        /// PowerShell 7 (.ps1) execution style.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_STYLE_PWSH
        const STYLE_PWSH = 0x0020;
        /// Set if host implements exec session IO redirection.
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_IO_REDIRECTION
        const IO_REDIRECTION = 0x1000;
        /// Set if host supports encoding control flags for exec commands
        /// (RAW_ENCODING, UNICODE_CONSOLE, and ENCODING_UTF8 flags).
        ///
        /// NOW-PROTO: NOW_CAP_EXEC_UNICODE_CONSOLE
        const UNICODE_CONSOLE = 0x0040;
    }
}

/// NOW-PROTO version representation.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct NowProtoVersion {
    // IMPORTANT: Field ordering is important for `PartialOrd` and `Ord` derived implementations.
    pub major: u16,
    pub minor: u16,
}

impl NowProtoVersion {
    /// Represents the current version of the NOW protocol implemented by the library.
    pub const CURRENT: Self = Self { major: 1, minor: 6 };

    /// Returns `true` if this version supports the encoding control exec flags
    /// (`NOW_EXEC_FLAG_*_RAW_ENCODING`, `NOW_EXEC_FLAG_*_UNICODE_CONSOLE`, and `NOW_EXEC_FLAG_PROCESS_ENCODING_UTF8`).
    pub fn supports_exec_unicode_console(self) -> bool {
        self >= Self { major: 1, minor: 6 }
    }
}

/// This message is first set by the client side, to advertise capabilities.
/// Received client message should be downgraded by the server (remove non-intersecting
/// capabilities) and sent back to the client at the start of DVC channel communications.
/// DVC channel should be closed if protocol versions are not compatible.
///
/// `Default` implementation returns capabilities with empty capability sets and no heartbeat
/// interval set. Proto version is set to [`NowProtoVersion::CURRENT`] by default.
///
/// NOW-PROTO: NOW_CHANNEL_CAPSET_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowChannelCapsetMsg {
    version: NowProtoVersion,
    system_capset: NowSystemCapsetFlags,
    session_capset: NowSessionCapsetFlags,
    exec_capset: NowExecCapsetFlags,
    heartbeat_interval: Option<u32>,
}

impl Default for NowChannelCapsetMsg {
    fn default() -> Self {
        Self {
            version: NowProtoVersion::CURRENT,
            system_capset: NowSystemCapsetFlags::empty(),
            session_capset: NowSessionCapsetFlags::empty(),
            exec_capset: NowExecCapsetFlags::empty(),
            heartbeat_interval: None,
        }
    }
}

impl NowChannelCapsetMsg {
    const NAME: &'static str = "NOW_CHANNEL_CAPSET_MSG";
    const FIXED_PART_SIZE: usize = 14; // NowProtoVersion(4) + u16(2) + u16(2) + u16(2) + u32(4)

    #[must_use]
    pub fn with_system_capset(mut self, system_capset: NowSystemCapsetFlags) -> Self {
        self.system_capset = system_capset;
        self
    }

    #[must_use]
    pub fn with_session_capset(mut self, session_capset: NowSessionCapsetFlags) -> Self {
        self.session_capset = session_capset;
        self
    }

    #[must_use]
    pub fn with_exec_capset(mut self, exec_capset: NowExecCapsetFlags) -> Self {
        self.exec_capset = exec_capset;
        self
    }

    pub fn with_heartbeat_interval(mut self, interval: time::Duration) -> EncodeResult<Self> {
        // Sanity check: Limit min heartbeat interval to 5 seconds.
        const MIN_HEARTBEAT_INTERVAL: time::Duration = time::Duration::from_secs(5);

        // Sanity check: Limit max heartbeat interval to 24 hours.
        const MAX_HEARTBEAT_INTERVAL: time::Duration = time::Duration::from_secs(60 * 60 * 24);

        if interval < MIN_HEARTBEAT_INTERVAL || interval > MAX_HEARTBEAT_INTERVAL {
            return Err(invalid_field_err!("heartbeat_timeout", "too big heartbeat interval"));
        }

        let interval = u32::try_from(interval.as_secs()).expect("heartbeat interval fits into u32");

        self.heartbeat_interval = Some(interval);
        Ok(self)
    }

    pub fn system_capset(&self) -> NowSystemCapsetFlags {
        self.system_capset
    }

    pub fn session_capset(&self) -> NowSessionCapsetFlags {
        self.session_capset
    }

    pub fn exec_capset(&self) -> NowExecCapsetFlags {
        self.exec_capset
    }

    pub fn heartbeat_interval(&self) -> Option<time::Duration> {
        self.heartbeat_interval
            .map(|interval| time::Duration::from_secs(u64::from(interval)))
    }

    pub fn version(&self) -> NowProtoVersion {
        self.version
    }

    /// Downgrade capabilities to the minimum common capabilities between two peers.
    ///
    /// - Version is chosen as minimum between two peers.
    /// - Capabilities are chosen as intersection between two peers.
    /// - Heartbeat interval is chosen as minimum specified value between two peers.
    #[must_use]
    pub fn downgrade(&self, other: &Self) -> Self {
        // Choose minimum version between two peers.
        let version = self.version.min(other.version);

        let system_capset = self.system_capset & other.system_capset;
        let session_capset = self.session_capset & other.session_capset;
        let exec_capset = self.exec_capset & other.exec_capset;

        // Choose minimum specified heartbeat interval between two peers.
        let heartbeat_interval = match (self.heartbeat_interval, other.heartbeat_interval) {
            (Some(lhs), Some(rhs)) => Some(lhs.min(rhs)),
            (Some(lhs), None) => Some(lhs),
            (None, Some(rhs)) => Some(rhs),
            (None, None) => None,
        };

        Self {
            version,
            system_capset,
            session_capset,
            exec_capset,
            heartbeat_interval,
        }
    }

    pub(super) fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        ensure_fixed_part_size!(in: src);

        let flags = NowChannelCapsetFlags::from_bits_retain(header.flags);

        let major_version = src.read_u16();
        let minor_version = src.read_u16();

        let version = NowProtoVersion {
            major: major_version,
            minor: minor_version,
        };

        let system_capset = NowSystemCapsetFlags::from_bits_retain(src.read_u16());
        let session_capset = NowSessionCapsetFlags::from_bits_retain(src.read_u16());
        let exec_capset = NowExecCapsetFlags::from_bits_retain(src.read_u16());
        // Read heartbeat interval unconditionally even if `SET_HEARTBEAT` flags is not set.
        let heartbeat_interval_value = src.read_u32();

        let heartbeat_interval = flags
            .contains(NowChannelCapsetFlags::SET_HEARTBEAT)
            .then_some(heartbeat_interval_value);

        Ok(Self {
            version,
            system_capset,
            session_capset,
            exec_capset,
            heartbeat_interval,
        })
    }
}

impl Encode for NowChannelCapsetMsg {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let flags = if self.heartbeat_interval.is_some() {
            NowChannelCapsetFlags::SET_HEARTBEAT
        } else {
            NowChannelCapsetFlags::empty()
        };

        let header = NowHeader {
            size: u32::try_from(Self::FIXED_PART_SIZE).expect("Capabilities have fixed size which fits into u32"),
            class: NowMessageClass::CHANNEL,
            kind: NowChannelMsgKind::CAPSET.0,
            flags: flags.bits(),
        };

        header.encode(dst)?;

        ensure_fixed_part_size!(in: dst);

        dst.write_u16(self.version.major);
        dst.write_u16(self.version.minor);
        dst.write_u16(self.system_capset.bits());
        dst.write_u16(self.session_capset.bits());
        dst.write_u16(self.exec_capset.bits());
        dst.write_u32(self.heartbeat_interval.unwrap_or_default());

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + Self::FIXED_PART_SIZE
    }
}

impl Decode<'_> for NowChannelCapsetMsg {
    fn decode(src: &mut ReadCursor<'_>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowChannelMsgKind(header.kind)) {
            (NowMessageClass::CHANNEL, NowChannelMsgKind::CAPSET) => Self::decode_from_body(header, src),
            _ => Err(invalid_field_err!("type", "invalid message type")),
        }
    }
}

impl From<NowChannelCapsetMsg> for NowMessage<'_> {
    fn from(msg: NowChannelCapsetMsg) -> Self {
        NowMessage::Channel(NowChannelMessage::Capset(msg))
    }
}
