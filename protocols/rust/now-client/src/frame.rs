use now_proto_pdu::ironrdp_core::{Decode, IntoOwned, ReadCursor};
use now_proto_pdu::{NowMessage, OwnedNowMessage};

use crate::NowClientError;

/// Incrementally decodes bounded NOW PDU frames from a byte stream.
pub(crate) struct MessageBuffer {
    bytes: Vec<u8>,
    ready: Vec<OwnedNowMessage>,
    max_body_size: usize,
}

impl MessageBuffer {
    const HEADER_SIZE: usize = 8;

    pub(crate) fn new(max_body_size: usize) -> Self {
        Self {
            bytes: Vec::new(),
            ready: Vec::new(),
            max_body_size,
        }
    }

    pub(crate) fn push(&mut self, input: &[u8]) -> Result<(), NowClientError> {
        let maximum = self
            .max_body_size
            .checked_add(Self::HEADER_SIZE)
            .ok_or(NowClientError::FrameBufferTooLarge {
                maximum: self.max_body_size,
            })?;
        let mut input = input;
        loop {
            if self.bytes.len() < Self::HEADER_SIZE {
                if input.is_empty() {
                    break;
                }
                let bytes_to_copy = input.len().min(Self::HEADER_SIZE - self.bytes.len());
                self.bytes.extend_from_slice(&input[..bytes_to_copy]);
                input = &input[bytes_to_copy..];
                continue;
            }

            let body_size = u32::from_le_bytes([self.bytes[0], self.bytes[1], self.bytes[2], self.bytes[3]]) as usize;
            if body_size > self.max_body_size {
                return Err(NowClientError::FrameTooLarge {
                    declared: body_size,
                    maximum: self.max_body_size,
                });
            }

            let frame_size = Self::HEADER_SIZE
                .checked_add(body_size)
                .ok_or(NowClientError::FrameTooLarge {
                    declared: body_size,
                    maximum: self.max_body_size,
                })?;
            if frame_size > maximum {
                return Err(NowClientError::FrameBufferTooLarge { maximum });
            }
            if self.bytes.len() < frame_size {
                if input.is_empty() {
                    break;
                }
                let bytes_to_copy = input.len().min(frame_size - self.bytes.len());
                self.bytes.extend_from_slice(&input[..bytes_to_copy]);
                input = &input[bytes_to_copy..];
                continue;
            }

            let mut cursor = ReadCursor::new(&self.bytes[..frame_size]);
            let message =
                NowMessage::decode(&mut cursor).map_err(|error| NowClientError::PduDecode(error.to_string()))?;
            self.ready.push(message.into_owned());
            self.bytes.drain(..frame_size);
        }

        Ok(())
    }

    pub(crate) fn take_ready(&mut self) -> Vec<OwnedNowMessage> {
        core::mem::take(&mut self.ready)
    }

    pub(crate) fn restore_ready(&mut self, messages: impl IntoIterator<Item = OwnedNowMessage>) {
        self.ready.extend(messages);
    }
}

#[cfg(test)]
mod tests {
    use now_proto_pdu::ironrdp_core::encode_vec;
    use now_proto_pdu::{NowChannelCapsetMsg, NowChannelHeartbeatMsg, NowMessage};

    use super::MessageBuffer;
    use crate::NowClientError;

    fn encode(message: impl Into<NowMessage<'static>>) -> Vec<u8> {
        match encode_vec(&message.into()) {
            Ok(bytes) => bytes,
            Err(error) => panic!("test PDU must encode: {error}"),
        }
    }

    #[test]
    fn fragmented_and_coalesced_frames_are_retained_and_decoded() {
        let frame = encode(NowChannelCapsetMsg::default());
        let heartbeat = encode(NowChannelHeartbeatMsg::default());
        let mut buffer = MessageBuffer::new(64);

        let first = buffer.push(&frame[..10]);
        assert!(first.is_ok());
        assert!(buffer.take_ready().is_empty());

        let mut coalesced = frame[10..].to_vec();
        coalesced.extend_from_slice(&heartbeat);
        match buffer.push(&coalesced) {
            Ok(()) => {}
            Err(error) => panic!("frames must decode: {error}"),
        };
        let messages = buffer.take_ready();

        assert_eq!(messages.len(), 2);
    }

    #[test]
    fn oversized_body_is_rejected_before_buffering_it() {
        let mut buffer = MessageBuffer::new(16);
        let error = match buffer.push(&[17, 0, 0, 0, 0, 0, 0, 0]) {
            Ok(()) => panic!("oversized frame must be rejected"),
            Err(error) => error,
        };

        assert!(matches!(
            error,
            NowClientError::FrameTooLarge {
                declared: 17,
                maximum: 16
            }
        ));
    }
}
