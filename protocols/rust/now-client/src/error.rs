use now_proto_pdu::{NowProtoVersion, NowStatusError};
use thiserror::Error;

/// Errors produced while connecting to or using a NOW execution channel.
#[derive(Debug, Error)]
pub enum NowClientError {
    /// The underlying transport failed.
    #[error("NOW transport I/O failed: {0}")]
    Io(#[from] std::io::Error),
    /// A NOW PDU could not be encoded.
    #[error("failed to encode NOW PDU: {0}")]
    PduEncode(String),
    /// A NOW PDU could not be decoded.
    #[error("failed to decode NOW PDU: {0}")]
    PduDecode(String),
    /// The peer uses a different NOW protocol major version.
    #[error("incompatible NOW protocol major versions: client {client:?}, peer {peer:?}")]
    IncompatibleVersion {
        /// The version advertised by this client.
        client: NowProtoVersion,
        /// The version advertised by the peer.
        peer: NowProtoVersion,
    },
    /// The handshake did not complete before its configured deadline.
    #[error("NOW capability handshake timed out")]
    HandshakeTimeout,
    /// A message arrived in an invalid protocol state.
    #[error("NOW protocol violation: {0}")]
    Protocol(String),
    /// A PDU body exceeded the configured frame limit.
    #[error("NOW frame body size {declared} exceeds configured maximum {maximum}")]
    FrameTooLarge {
        /// Body size declared by the PDU header.
        declared: usize,
        /// Configured maximum body size.
        maximum: usize,
    },
    /// An incomplete frame exceeded the bounded deframer storage.
    #[error("NOW frame buffer exceeds configured maximum {maximum}")]
    FrameBufferTooLarge {
        /// Maximum retained frame size.
        maximum: usize,
    },
    /// The supplied client configuration is invalid.
    #[error("invalid NOW client configuration: {0}")]
    InvalidConfiguration(String),
    /// The execution request is invalid before it was sent.
    #[error("invalid NOW execution request: {0}")]
    InvalidRequest(String),
    /// The peer did not negotiate a capability required by the request.
    #[error("peer does not support required NOW capability: {0}")]
    UnsupportedCapability(&'static str),
    /// A tracked execution is already active on this client stream.
    #[error("NOW client already has an active tracked execution")]
    OperationInProgress,
    /// All nonzero NOW session IDs have been allocated by this client.
    #[error("NOW session ID space is exhausted")]
    SessionIdExhausted,
    /// A remote NOW result or cancellation response reported an error status.
    #[error("remote NOW operation {session_id} failed: {error}")]
    RemoteStatus {
        /// Session that reported the error.
        session_id: u32,
        /// Error status returned by the peer.
        #[source]
        error: NowStatusError,
    },
    /// The worker stopped before the requested operation completed.
    #[error("NOW worker is closed: {0}")]
    WorkerClosed(String),
    /// A caller did not drain an operation's bounded event queue.
    #[error("NOW operation {session_id} event queue is full")]
    EventQueueFull {
        /// Session whose event queue overflowed.
        session_id: u32,
    },
    /// An operation is already terminal or no longer tracked.
    #[error("NOW operation {session_id} is no longer active")]
    OperationFinished {
        /// Session that is no longer active.
        session_id: u32,
    },
}
