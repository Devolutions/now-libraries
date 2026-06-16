use expect_test::Expect;
use now_proto_pdu::ironrdp_core::{Decode, IntoOwned, ReadCursor};
use now_proto_pdu::NowMessage;

pub fn now_msg_roundtrip(msg: impl Into<NowMessage<'static>>, expected_bytes: Expect) -> NowMessage<'static> {
    let msg = msg.into();

    let buf = now_proto_pdu::ironrdp_core::encode_vec(&msg).expect("failed to encode message");

    expected_bytes.assert_eq(&format!("{:02X?}", buf));

    let mut cursor = ReadCursor::new(&buf);
    let decoded = NowMessage::decode(&mut cursor).expect("failed to decode message");

    assert_eq!(msg, decoded);

    decoded.into_owned()
}

pub fn now_msg_decodes_into(msg: impl Into<NowMessage<'static>>, expected_bytes: &[u8]) -> NowMessage<'static> {
    let msg = msg.into();

    let mut cursor = ReadCursor::new(expected_bytes);
    let decoded = NowMessage::decode(&mut cursor).expect("failed to decode message");

    assert_eq!(msg, decoded);

    decoded.into_owned()
}
