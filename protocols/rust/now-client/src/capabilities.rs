use core::time::Duration;

use now_proto_pdu::{NowChannelCapsetMsg, NowExecCapsetFlags, NowProtoVersion, NowSessionCapsetFlags};

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

    /// Returns whether both sides negotiated `NOW_CAP_SESSION_LOCK`, which
    /// [`NowClientHandle::session_lock`](crate::NowClientHandle::session_lock) requires.
    pub fn supports_session_lock(&self) -> bool {
        self.capset.session_capset().contains(NowSessionCapsetFlags::LOCK)
    }

    /// Returns whether both sides negotiated `NOW_CAP_SESSION_LOGOFF`, which
    /// [`NowClientHandle::session_logoff`](crate::NowClientHandle::session_logoff) requires.
    pub fn supports_session_logoff(&self) -> bool {
        self.capset.session_capset().contains(NowSessionCapsetFlags::LOGOFF)
    }

    /// Returns whether elevated execution is available.
    ///
    /// Elevation is gated on the capability rather than the version alone, because a 1.7 peer
    /// that cannot elevate (for example, one where elevation is disabled or unsupported) must
    /// answer `false`.
    /// Under this capability the host may prompt the interactive user for consent, and stdio
    /// redirection is best-effort depending on the host's elevation mechanism.
    pub fn supports_elevation(&self) -> bool {
        self.has(NowExecCapsetFlags::ELEVATION) && self.at_least(1, 7)
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

#[cfg(test)]
mod tests {
    use now_proto_pdu::{NowChannelCapsetMsg, NowSessionCapsetFlags};

    use super::NowCapabilities;

    fn negotiate(client: NowSessionCapsetFlags, peer: NowSessionCapsetFlags) -> NowCapabilities {
        let client = NowChannelCapsetMsg::default().with_session_capset(client);
        let peer = NowChannelCapsetMsg::default().with_session_capset(peer);
        match NowCapabilities::negotiate(&client, &peer) {
            Ok(capabilities) => capabilities,
            Err(error) => panic!("matching major versions must negotiate: {error}"),
        }
    }

    #[test]
    fn session_capabilities_require_both_sides() {
        let both = NowSessionCapsetFlags::LOCK | NowSessionCapsetFlags::LOGOFF;

        let negotiated = negotiate(both, both);
        assert!(negotiated.supports_session_lock());
        assert!(negotiated.supports_session_logoff());

        let peer_lacks_logoff = negotiate(both, NowSessionCapsetFlags::LOCK);
        assert!(peer_lacks_logoff.supports_session_lock());
        assert!(!peer_lacks_logoff.supports_session_logoff());

        let client_lacks_lock = negotiate(NowSessionCapsetFlags::LOGOFF, both);
        assert!(!client_lacks_lock.supports_session_lock());
        assert!(client_lacks_lock.supports_session_logoff());

        let unrelated = negotiate(NowSessionCapsetFlags::empty(), NowSessionCapsetFlags::all());
        assert!(!unrelated.supports_session_lock());
        assert!(!unrelated.supports_session_logoff());
    }
}
