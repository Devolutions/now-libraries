use core::time::Duration;

use now_proto_pdu::{NowChannelCapsetMsg, NowExecCapsetFlags, NowProtoVersion};

use crate::NowClientError;

/// Capabilities mutually supported by the client and its connected peer.
///
/// The value is always calculated as an intersection with the local advertised capset,
/// even if the peer echoes unsupported flags.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct NowCapabilities {
    capset: NowChannelCapsetMsg,
}

impl NowCapabilities {
    pub(crate) fn negotiate(
        requested: &NowChannelCapsetMsg,
        peer: &NowChannelCapsetMsg,
    ) -> Result<Self, NowClientError> {
        if requested.version().major != peer.version().major {
            return Err(NowClientError::IncompatibleVersion {
                client: requested.version(),
                peer: peer.version(),
            });
        }

        Ok(Self {
            capset: requested.downgrade(peer),
        })
    }

    /// Returns the negotiated capability-set PDU.
    pub fn capset(&self) -> &NowChannelCapsetMsg {
        &self.capset
    }

    /// Returns the negotiated NOW protocol version.
    pub fn version(&self) -> NowProtoVersion {
        self.capset.version()
    }

    /// Returns the negotiated heartbeat interval, if either peer requested one.
    pub fn heartbeat_interval(&self) -> Option<Duration> {
        self.capset.heartbeat_interval()
    }

    /// Returns whether the generic Run style is available.
    pub fn supports_run(&self) -> bool {
        self.has(NowExecCapsetFlags::STYLE_RUN)
    }

    /// Returns whether CreateProcess execution is available.
    pub fn supports_process(&self) -> bool {
        self.has(NowExecCapsetFlags::STYLE_PROCESS)
    }

    /// Returns whether Batch execution is available.
    pub fn supports_batch(&self) -> bool {
        self.has(NowExecCapsetFlags::STYLE_BATCH)
    }

    /// Returns whether Windows PowerShell execution is available.
    pub fn supports_win_ps(&self) -> bool {
        self.has(NowExecCapsetFlags::STYLE_WINPS)
    }

    /// Returns whether PowerShell 7 execution is available.
    pub fn supports_pwsh(&self) -> bool {
        self.has(NowExecCapsetFlags::STYLE_PWSH)
    }

    /// Returns whether tracked I/O redirection is available.
    pub fn supports_io_redirection(&self) -> bool {
        self.has(NowExecCapsetFlags::IO_REDIRECTION) && self.at_least(1, 3)
    }

    /// Returns whether UTF-8 and Unicode-console encoding controls are available.
    pub fn supports_unicode_console(&self) -> bool {
        self.has(NowExecCapsetFlags::UNICODE_CONSOLE) && self.version().supports_exec_unicode_console()
    }

    pub(crate) fn supports_detached(&self) -> bool {
        self.at_least(1, 4)
    }

    fn has(&self, capability: NowExecCapsetFlags) -> bool {
        self.capset.exec_capset().contains(capability)
    }

    fn at_least(&self, major: u16, minor: u16) -> bool {
        self.version() >= NowProtoVersion { major, minor }
    }
}
