use core::time::Duration;

use now_proto_pdu::{NowChannelCapsetMsg, NowExecCapsetFlags};

use crate::NowClientError;

/// Limits and advertised capabilities used to establish a NOW client connection.
#[derive(Clone, Debug)]
pub struct NowClientConfig {
    /// Capabilities sent to the peer during the initial handshake.
    pub client_capset: NowChannelCapsetMsg,
    /// Largest accepted PDU body, excluding the eight-byte NOW header.
    pub max_frame_body_size: usize,
    /// Maximum number of bytes requested from the transport in one read.
    pub read_buffer_size: usize,
    /// Bound for requests waiting to be written by the worker.
    pub command_queue_capacity: usize,
    /// Bound for unread events per tracked execution.
    pub event_queue_capacity: usize,
    /// Maximum number of recent Run submissions whose post-submission frames are discarded.
    pub run_discard_capacity: usize,
    /// Deadline for receiving the peer capability set.
    pub connect_timeout: Duration,
}

impl Default for NowClientConfig {
    fn default() -> Self {
        let exec_capset = NowExecCapsetFlags::STYLE_RUN
            | NowExecCapsetFlags::STYLE_PROCESS
            | NowExecCapsetFlags::STYLE_BATCH
            | NowExecCapsetFlags::STYLE_WINPS
            | NowExecCapsetFlags::STYLE_PWSH
            | NowExecCapsetFlags::IO_REDIRECTION
            | NowExecCapsetFlags::UNICODE_CONSOLE;
        let capset = NowChannelCapsetMsg::default().with_exec_capset(exec_capset);
        let client_capset = match capset.with_heartbeat_interval(Duration::from_secs(60)) {
            Ok(capset) => capset,
            Err(_) => unreachable!("the fixed default heartbeat interval is valid"),
        };

        Self {
            client_capset,
            max_frame_body_size: 16 * 1024 * 1024,
            read_buffer_size: 16 * 1024,
            command_queue_capacity: 64,
            event_queue_capacity: 64,
            run_discard_capacity: 64,
            connect_timeout: Duration::from_secs(15),
        }
    }
}

impl NowClientConfig {
    pub(crate) fn validate(&self) -> Result<(), NowClientError> {
        if self.max_frame_body_size == 0 {
            return Err(NowClientError::InvalidConfiguration(
                "max_frame_body_size must be greater than zero".to_owned(),
            ));
        }
        if self.read_buffer_size == 0 {
            return Err(NowClientError::InvalidConfiguration(
                "read_buffer_size must be greater than zero".to_owned(),
            ));
        }
        if self.command_queue_capacity == 0 {
            return Err(NowClientError::InvalidConfiguration(
                "command_queue_capacity must be greater than zero".to_owned(),
            ));
        }
        if self.event_queue_capacity == 0 {
            return Err(NowClientError::InvalidConfiguration(
                "event_queue_capacity must be greater than zero".to_owned(),
            ));
        }
        if self.run_discard_capacity == 0 {
            return Err(NowClientError::InvalidConfiguration(
                "run_discard_capacity must be greater than zero".to_owned(),
            ));
        }
        if self.connect_timeout.is_zero() {
            return Err(NowClientError::InvalidConfiguration(
                "connect_timeout must be greater than zero".to_owned(),
            ));
        }

        Ok(())
    }
}
