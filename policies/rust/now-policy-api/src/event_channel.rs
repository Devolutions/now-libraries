//! Per-operation event channel descriptor and `NOW_BROKER` frame protocol.
//!
//! For each executed operation the broker opens a per-operation event channel
//! (currently a local named pipe), when supported, and returns its descriptor
//! in the execution response. The channel is one-way (read-only from the
//! client side) and carries a minimal length-prefixed binary frame protocol
//! used to:
//!
//! - push both stdout and stderr data over a single channel (only when the
//!   execute request opts in via `CaptureOutput`), preserving the sequential
//!   order of the interleaved output;
//! - notify the client that the operation status changed, so status is only
//!   queried over HTTP when something actually happened (no periodic polling);
//! - signal operation completion, after which the channel can be closed.
//!
//! # Wire format
//!
//! All integers are little-endian. Each frame is:
//!
//! ```text
//! | u32 frame_size | u16 frame_kind | [frame_size; u8] frame_body |
//! ```
//!
//! `frame_size` is the body length in bytes and excludes the 6-byte header.
//! Stdout/stderr frame bodies are strictly UTF-8; the broker never splits a
//! UTF-8 character across frames. Decoders must skip frames with unknown
//! kinds so new frame kinds can be added without breaking older clients.

use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

use super::enums::EventChannelKind;

/// Descriptor of a per-operation event channel returned in the execution response.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "EventChannel")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct EventChannel {
    /// Transport kind of the channel.
    pub kind: EventChannelKind,

    /// Transport-specific path. For the `LocalPipe` kind this is the pipe
    /// name/path the client should connect to.
    #[schemars(length(min = 1, max = 1024))]
    pub path: String,
}

/// Event channel protocol major version advertised in the `Hello` frame.
pub const EVENT_CHANNEL_VERSION_MAJOR: u16 = 1;
/// Event channel protocol minor version advertised in the `Hello` frame.
pub const EVENT_CHANNEL_VERSION_MINOR: u16 = 0;

/// Size in bytes of the fixed frame header (`u32 frame_size` + `u16 frame_kind`).
pub const EVENT_FRAME_HEADER_SIZE: usize = 6;

/// Maximum allowed frame body size in bytes.
///
/// Protects decoders from unbounded buffering; encoders must split larger
/// output into multiple frames.
pub const MAX_EVENT_FRAME_BODY_BYTES: usize = 65536;

/// `NOW_BROKER` event frame kind discriminators.
pub mod frame_kind {
    /// `NOW_BROKER_HELLO`
    pub const HELLO: u16 = 0x0000;
    /// `NOW_BROKER_STATUS_UPDATED`
    pub const STATUS_UPDATED: u16 = 0x0001;
    /// `NOW_BROKER_FINISH`
    pub const FINISH: u16 = 0x0002;
    /// `NOW_BROKER_STDOUT`
    pub const STDOUT: u16 = 0x0003;
    /// `NOW_BROKER_STDERR`
    pub const STDERR: u16 = 0x0004;
    /// `NOW_BROKER_STDOUT_OVERFLOW`
    pub const STDOUT_OVERFLOW: u16 = 0x0005;
    /// `NOW_BROKER_STDERR_OVERFLOW`
    pub const STDERR_OVERFLOW: u16 = 0x0006;
}

/// A decoded `NOW_BROKER` event frame.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum EventFrame {
    /// First frame on the channel; acknowledges that the transport is ready
    /// and advertises the protocol version.
    Hello { version_major: u16, version_minor: u16 },
    /// The operation status changed and awaits a `StatusRequest` HTTP query.
    /// Deliberately carries no payload: complex data is queried over HTTP.
    StatusUpdated,
    /// New stdout data. Strictly UTF-8; character boundaries are guaranteed
    /// by the broker.
    Stdout(String),
    /// New stderr data. Strictly UTF-8; character boundaries are guaranteed
    /// by the broker.
    Stderr(String),
    /// The operation finished; query `StatusRequest` for details. The channel
    /// can be gracefully closed by the client after this frame.
    Finish,
    /// The client was too slow to read stdout and `bytes_skipped` bytes were
    /// truncated.
    StdoutOverflow { bytes_skipped: u32 },
    /// The client was too slow to read stderr and `bytes_skipped` bytes were
    /// truncated.
    StderrOverflow { bytes_skipped: u32 },
    /// A frame with an unknown kind. Must be ignored by consumers to allow
    /// forward-compatible protocol extension.
    Unknown { kind: u16, body: Vec<u8> },
}

/// Error produced while encoding or decoding event frames.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
pub enum EventFrameError {
    #[error("frame body size {size} exceeds the maximum of {MAX_EVENT_FRAME_BODY_BYTES} bytes")]
    BodyTooLarge { size: usize },
    #[error("frame kind {kind:#06x} expects a body of {expected} bytes, got {actual}")]
    InvalidBodyLength { kind: u16, expected: usize, actual: usize },
    #[error("stdout/stderr frame body is not valid UTF-8")]
    InvalidUtf8,
}

impl EventFrame {
    /// Frame kind discriminator for this frame.
    pub fn kind(&self) -> u16 {
        match self {
            EventFrame::Hello { .. } => frame_kind::HELLO,
            EventFrame::StatusUpdated => frame_kind::STATUS_UPDATED,
            EventFrame::Stdout(_) => frame_kind::STDOUT,
            EventFrame::Stderr(_) => frame_kind::STDERR,
            EventFrame::Finish => frame_kind::FINISH,
            EventFrame::StdoutOverflow { .. } => frame_kind::STDOUT_OVERFLOW,
            EventFrame::StderrOverflow { .. } => frame_kind::STDERR_OVERFLOW,
            EventFrame::Unknown { kind, .. } => *kind,
        }
    }

    /// Encode the frame (header + body) into a byte vector.
    pub fn encode(&self) -> Result<Vec<u8>, EventFrameError> {
        let body: Vec<u8> = match self {
            EventFrame::Hello {
                version_major,
                version_minor,
            } => {
                let mut body = Vec::with_capacity(4);
                body.extend_from_slice(&version_major.to_le_bytes());
                body.extend_from_slice(&version_minor.to_le_bytes());
                body
            }
            EventFrame::StatusUpdated | EventFrame::Finish => Vec::new(),
            EventFrame::Stdout(data) | EventFrame::Stderr(data) => data.as_bytes().to_vec(),
            EventFrame::StdoutOverflow { bytes_skipped } | EventFrame::StderrOverflow { bytes_skipped } => {
                bytes_skipped.to_le_bytes().to_vec()
            }
            EventFrame::Unknown { body, .. } => body.clone(),
        };

        if body.len() > MAX_EVENT_FRAME_BODY_BYTES {
            return Err(EventFrameError::BodyTooLarge { size: body.len() });
        }

        let mut frame = Vec::with_capacity(EVENT_FRAME_HEADER_SIZE + body.len());
        frame.extend_from_slice(&u32::try_from(body.len()).expect("bounded above").to_le_bytes());
        frame.extend_from_slice(&self.kind().to_le_bytes());
        frame.extend_from_slice(&body);
        Ok(frame)
    }

    /// Decode a frame from a kind discriminator and body bytes.
    pub fn decode_body(kind: u16, body: &[u8]) -> Result<EventFrame, EventFrameError> {
        fn expect_len(kind: u16, body: &[u8], expected: usize) -> Result<(), EventFrameError> {
            if body.len() != expected {
                return Err(EventFrameError::InvalidBodyLength {
                    kind,
                    expected,
                    actual: body.len(),
                });
            }
            Ok(())
        }

        match kind {
            frame_kind::HELLO => {
                expect_len(kind, body, 4)?;
                Ok(EventFrame::Hello {
                    version_major: u16::from_le_bytes([body[0], body[1]]),
                    version_minor: u16::from_le_bytes([body[2], body[3]]),
                })
            }
            frame_kind::STATUS_UPDATED => {
                expect_len(kind, body, 0)?;
                Ok(EventFrame::StatusUpdated)
            }
            frame_kind::STDOUT => {
                let data = str::from_utf8(body).map_err(|_| EventFrameError::InvalidUtf8)?;
                Ok(EventFrame::Stdout(data.to_owned()))
            }
            frame_kind::STDERR => {
                let data = str::from_utf8(body).map_err(|_| EventFrameError::InvalidUtf8)?;
                Ok(EventFrame::Stderr(data.to_owned()))
            }
            frame_kind::FINISH => {
                expect_len(kind, body, 0)?;
                Ok(EventFrame::Finish)
            }
            frame_kind::STDOUT_OVERFLOW => {
                expect_len(kind, body, 4)?;
                Ok(EventFrame::StdoutOverflow {
                    bytes_skipped: u32::from_le_bytes([body[0], body[1], body[2], body[3]]),
                })
            }
            frame_kind::STDERR_OVERFLOW => {
                expect_len(kind, body, 4)?;
                Ok(EventFrame::StderrOverflow {
                    bytes_skipped: u32::from_le_bytes([body[0], body[1], body[2], body[3]]),
                })
            }
            _ => Ok(EventFrame::Unknown {
                kind,
                body: body.to_vec(),
            }),
        }
    }
}

/// Incremental decoder for a `NOW_BROKER` event frame stream.
///
/// Feed raw bytes read from the channel with [`EventFrameDecoder::extend`],
/// then drain complete frames with [`EventFrameDecoder::next_frame`].
#[derive(Debug, Default)]
pub struct EventFrameDecoder {
    buffer: Vec<u8>,
}

impl EventFrameDecoder {
    pub fn new() -> Self {
        Self::default()
    }

    /// Append raw bytes received from the transport.
    pub fn extend(&mut self, bytes: &[u8]) {
        self.buffer.extend_from_slice(bytes);
    }

    /// Returns `true` when the decoder holds buffered bytes that do not yet
    /// form a complete frame. If the transport reaches end-of-stream while
    /// this is `true`, the frame stream was truncated mid-frame.
    pub fn has_buffered_data(&self) -> bool {
        !self.buffer.is_empty()
    }

    /// Try to decode the next complete frame.
    ///
    /// Returns `Ok(None)` when more bytes are needed. Frames with unknown
    /// kinds are returned as [`EventFrame::Unknown`] and should be ignored by
    /// the consumer. Errors are not recoverable: the channel is corrupt and
    /// should be closed.
    pub fn next_frame(&mut self) -> Result<Option<EventFrame>, EventFrameError> {
        if self.buffer.len() < EVENT_FRAME_HEADER_SIZE {
            return Ok(None);
        }

        let size = u32::from_le_bytes([self.buffer[0], self.buffer[1], self.buffer[2], self.buffer[3]]) as usize;
        if size > MAX_EVENT_FRAME_BODY_BYTES {
            return Err(EventFrameError::BodyTooLarge { size });
        }

        let kind = u16::from_le_bytes([self.buffer[4], self.buffer[5]]);
        let total = EVENT_FRAME_HEADER_SIZE + size;
        if self.buffer.len() < total {
            return Ok(None);
        }

        let frame = EventFrame::decode_body(kind, &self.buffer[EVENT_FRAME_HEADER_SIZE..total])?;
        self.buffer.drain(..total);
        Ok(Some(frame))
    }
}

#[cfg(test)]
mod tests {
    #![allow(clippy::unwrap_used)]

    use super::*;

    fn round_trip(frame: EventFrame) {
        let bytes = frame.encode().unwrap();
        let mut decoder = EventFrameDecoder::new();
        decoder.extend(&bytes);
        assert_eq!(decoder.next_frame().unwrap(), Some(frame));
        assert_eq!(decoder.next_frame().unwrap(), None);
    }

    #[test]
    fn frames_round_trip() {
        round_trip(EventFrame::Hello {
            version_major: EVENT_CHANNEL_VERSION_MAJOR,
            version_minor: EVENT_CHANNEL_VERSION_MINOR,
        });
        round_trip(EventFrame::StatusUpdated);
        round_trip(EventFrame::Stdout("hello π\n".to_owned()));
        round_trip(EventFrame::Stderr("warning: π\n".to_owned()));
        round_trip(EventFrame::Finish);
        round_trip(EventFrame::StdoutOverflow { bytes_skipped: 4096 });
        round_trip(EventFrame::StderrOverflow { bytes_skipped: 1 });
    }

    #[test]
    fn hello_frame_has_documented_layout() {
        let bytes = EventFrame::Hello {
            version_major: 1,
            version_minor: 0,
        }
        .encode()
        .unwrap();
        assert_eq!(bytes, [4, 0, 0, 0, 0x00, 0x00, 1, 0, 0, 0]);
    }

    #[test]
    fn decoder_handles_partial_and_concatenated_input() {
        let mut stream = Vec::new();
        stream.extend_from_slice(
            &EventFrame::Hello {
                version_major: 1,
                version_minor: 0,
            }
            .encode()
            .unwrap(),
        );
        stream.extend_from_slice(&EventFrame::Stdout("chunk".to_owned()).encode().unwrap());
        stream.extend_from_slice(&EventFrame::Finish.encode().unwrap());

        let mut decoder = EventFrameDecoder::new();
        let mut frames = Vec::new();
        for byte in stream {
            decoder.extend(&[byte]);
            while let Some(frame) = decoder.next_frame().unwrap() {
                frames.push(frame);
            }
        }

        assert_eq!(
            frames,
            [
                EventFrame::Hello {
                    version_major: 1,
                    version_minor: 0
                },
                EventFrame::Stdout("chunk".to_owned()),
                EventFrame::Finish,
            ]
        );
    }

    #[test]
    fn decoder_skips_unknown_frame_kinds() {
        let unknown = EventFrame::Unknown {
            kind: 0x7fff,
            body: vec![1, 2, 3],
        };
        let mut decoder = EventFrameDecoder::new();
        decoder.extend(&unknown.encode().unwrap());
        decoder.extend(&EventFrame::Finish.encode().unwrap());

        assert_eq!(decoder.next_frame().unwrap(), Some(unknown));
        assert_eq!(decoder.next_frame().unwrap(), Some(EventFrame::Finish));
    }

    #[test]
    fn decoder_rejects_invalid_utf8_and_oversized_frames() {
        let mut decoder = EventFrameDecoder::new();
        decoder.extend(&[2, 0, 0, 0, 0x03, 0x00, 0xff, 0xfe]);
        assert_eq!(decoder.next_frame(), Err(EventFrameError::InvalidUtf8));

        let mut decoder = EventFrameDecoder::new();
        let oversized = (u32::try_from(MAX_EVENT_FRAME_BODY_BYTES).unwrap() + 1).to_le_bytes();
        decoder.extend(&[oversized[0], oversized[1], oversized[2], oversized[3], 0x03, 0x00]);
        assert!(matches!(
            decoder.next_frame(),
            Err(EventFrameError::BodyTooLarge { .. })
        ));
    }

    #[test]
    fn decoder_reports_buffered_data_for_truncated_frames() {
        let mut decoder = EventFrameDecoder::new();
        assert!(!decoder.has_buffered_data());

        let bytes = EventFrame::Stdout("interrupted".to_owned()).encode().unwrap();
        decoder.extend(&bytes[..bytes.len() - 4]);
        assert_eq!(decoder.next_frame().unwrap(), None);
        // EOF here would mean the stream was truncated mid-frame.
        assert!(decoder.has_buffered_data());

        decoder.extend(&bytes[bytes.len() - 4..]);
        assert_eq!(
            decoder.next_frame().unwrap(),
            Some(EventFrame::Stdout("interrupted".to_owned()))
        );
        assert!(!decoder.has_buffered_data());
    }

    #[test]
    fn fixed_body_frames_reject_wrong_length() {
        assert_eq!(
            EventFrame::decode_body(frame_kind::HELLO, &[1, 0]),
            Err(EventFrameError::InvalidBodyLength {
                kind: frame_kind::HELLO,
                expected: 4,
                actual: 2
            })
        );
        assert_eq!(
            EventFrame::decode_body(frame_kind::FINISH, &[0]),
            Err(EventFrameError::InvalidBodyLength {
                kind: frame_kind::FINISH,
                expected: 0,
                actual: 1
            })
        );
    }
}
