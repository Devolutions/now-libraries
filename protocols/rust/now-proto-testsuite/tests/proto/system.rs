use expect_test::expect;
use now_proto_pdu::*;
use now_proto_testsuite::proto::now_msg_roundtrip;

#[test]
fn roundtrip_system_shutdown() {
    let msg = NowSystemShutdownMsg::new(core::time::Duration::from_secs(123), "hello")
        .unwrap()
        .with_force_shutdown();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0B, 00, 00, 00, 11, 03, 01, 00, 7B, 00, 00, 00, 05, 68, 65, 6C, 6C, 6F, 00]"],
    );

    let actual = match decoded {
        NowMessage::System(NowSystemMessage::Shutdown(msg)) => msg,
        _ => panic!("Expected NowSystemShutdownMsg"),
    };

    assert_eq!(actual.timeout(), core::time::Duration::from_secs(123));
}
