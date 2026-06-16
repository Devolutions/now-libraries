use expect_test::expect;
use now_proto_pdu::{
    NowMessage, NowRdmAppAction, NowRdmAppActionMsg, NowRdmAppNotifyMsg, NowRdmAppStartMsg, NowRdmAppState,
    NowRdmCapabilitiesMsg, NowRdmReason, NowRdmSessionAction, NowRdmSessionActionMsg, NowRdmSessionNotifyKind,
    NowRdmSessionNotifyMsg, NowRdmSessionStartMsg, Uuid,
};

use now_proto_testsuite::proto::now_msg_roundtrip;

#[test]
fn rdm_capabilities_msg_roundtrip() {
    let msg = NowRdmCapabilitiesMsg::new(
        1672531200, // Unix timestamp for January 1, 2023 00:00:00 UTC
        "2025.1.2.3",
    )
    .expect("failed to create capabilities message")
    .with_version_extra("ABC")
    .expect("failed to set version extra")
    .with_app_available();

    let decoded = now_msg_roundtrip(
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::Capabilities(msg.clone())),
        expect!["[1D, 00, 00, 00, 14, 01, 00, 00, 00, CD, B0, 63, 00, 00, 00, 00, 01, 00, 00, 00, 0A, 32, 30, 32, 35, 2E, 31, 2E, 32, 2E, 33, 00, 03, 41, 42, 43, 00]"],
    );

    let actual = match decoded {
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::Capabilities(msg)) => msg,
        _ => panic!("Expected RDM Capabilities message"),
    };

    assert_eq!(actual.timestamp(), 1672531200);
    assert!(actual.is_app_available());
    assert_eq!(actual.rdm_version(), "2025.1.2.3");
    assert_eq!(actual.version_extra(), "ABC");
}

#[test]
fn rdm_app_start_msg_roundtrip() {
    let msg = NowRdmAppStartMsg::default()
        .with_timeout(45) // timeout in seconds
        .with_jump_mode()
        .with_maximized();

    let decoded = now_msg_roundtrip(
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::AppStart(msg)),
        expect!["[08, 00, 00, 00, 14, 02, 00, 00, 03, 00, 00, 00, 2D, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::AppStart(msg)) => msg,
        _ => panic!("Expected RDM AppStart message"),
    };

    assert!(actual.is_jump_mode());
    assert!(actual.is_maximized());
    assert!(!actual.is_fullscreen());
    assert_eq!(actual.timeout(), 45);
}

#[test]
fn rdm_app_action_msg_roundtrip() {
    let msg = NowRdmAppActionMsg::new(NowRdmAppAction::CLOSE)
        .with_action_data("ABC") // action data
        .expect("failed to create app action message");

    let decoded = now_msg_roundtrip(
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::AppAction(msg.clone())),
        expect!["[09, 00, 00, 00, 14, 03, 00, 00, 01, 00, 00, 00, 03, 41, 42, 43, 00]"],
    );

    let actual = match decoded {
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::AppAction(msg)) => msg,
        _ => panic!("Expected RDM AppAction message"),
    };

    assert_eq!(actual.app_action(), NowRdmAppAction::CLOSE);
    assert_eq!(actual.action_data(), "ABC");
}

#[test]
fn rdm_app_notify_msg_roundtrip() {
    let msg = NowRdmAppNotifyMsg::new(
        NowRdmAppState::READY,       // app_state
        NowRdmReason::NOT_SPECIFIED, // reason_code
    )
    .with_notify_data("OK") // notify_data
    .expect("failed to create app notify message");

    let decoded = now_msg_roundtrip(
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::AppNotify(msg.clone())),
        expect!["[0C, 00, 00, 00, 14, 04, 00, 00, 01, 00, 00, 00, 00, 00, 00, 00, 02, 4F, 4B, 00]"],
    );

    let actual = match decoded {
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::AppNotify(msg)) => msg,
        _ => panic!("Expected RDM AppNotify message"),
    };

    assert_eq!(actual.app_state(), NowRdmAppState::READY);
    assert_eq!(actual.reason_code(), NowRdmReason::NOT_SPECIFIED);
    assert_eq!(actual.notify_data(), "OK");
}

#[test]
fn rdm_session_start_msg_roundtrip() {
    let session_id = Uuid::from_bytes([
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
    ]);
    let connection_id = Uuid::from_bytes([
        0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0xF6, 0x07, 0x18, 0x29, 0x3A, 0x4B, 0x5C, 0x6D, 0x7E, 0x8F, 0x90,
    ]);

    let msg = NowRdmSessionStartMsg::new(session_id, connection_id, "<a>b</a>")
        .expect("failed to create session start message");

    let decoded = now_msg_roundtrip(
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::SessionStart(msg.clone())),
        expect!["[56, 00, 00, 00, 14, 05, 00, 00, 24, 30, 31, 30, 32, 30, 33, 30, 34, 2D, 30, 35, 30, 36, 2D, 30, 37, 30, 38, 2D, 30, 39, 30, 61, 2D, 30, 62, 30, 63, 30, 64, 30, 65, 30, 66, 31, 30, 00, 24, 61, 31, 62, 32, 63, 33, 64, 34, 2D, 65, 35, 66, 36, 2D, 30, 37, 31, 38, 2D, 32, 39, 33, 61, 2D, 34, 62, 35, 63, 36, 64, 37, 65, 38, 66, 39, 30, 00, 08, 3C, 61, 3E, 62, 3C, 2F, 61, 3E, 00]"],
    );

    let actual = match decoded {
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::SessionStart(msg)) => msg,
        _ => panic!("Expected RDM SessionStart message"),
    };

    assert_eq!(actual.session_id(), session_id);
    assert_eq!(actual.connection_id(), connection_id);
    assert_eq!(actual.connection_data(), "<a>b</a>");
}

#[test]
fn rdm_session_action_msg_roundtrip() {
    let session_id = Uuid::from_bytes([
        0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0xF6, 0x07, 0x18, 0x29, 0x3A, 0x4B, 0x5C, 0x6D, 0x7E, 0x8F, 0x90,
    ]);

    let msg = NowRdmSessionActionMsg::new(NowRdmSessionAction::FOCUS, session_id);

    let decoded = now_msg_roundtrip(
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::SessionAction(msg)),
        expect!["[2A, 00, 00, 00, 14, 06, 00, 00, 02, 00, 00, 00, 24, 61, 31, 62, 32, 63, 33, 64, 34, 2D, 65, 35, 66, 36, 2D, 30, 37, 31, 38, 2D, 32, 39, 33, 61, 2D, 34, 62, 35, 63, 36, 64, 37, 65, 38, 66, 39, 30, 00]"],
    );

    let actual = match decoded {
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::SessionAction(msg)) => msg,
        _ => panic!("Expected RDM SessionAction message"),
    };

    assert_eq!(actual.session_action(), NowRdmSessionAction::FOCUS);
    assert_eq!(actual.session_id(), session_id);
}

#[test]
fn rdm_session_notify_msg_roundtrip() {
    let session_id = Uuid::from_bytes([
        0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0xF6, 0x07, 0x18, 0x29, 0x3A, 0x4B, 0x5C, 0x6D, 0x7E, 0x8F, 0x90,
    ]);

    let msg = NowRdmSessionNotifyMsg::new_close(session_id)
        .with_log_data("Session closed gracefully")
        .expect("failed to create session notify message");

    let decoded = now_msg_roundtrip(
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::SessionNotify(msg)),
        expect!["[45, 00, 00, 00, 14, 07, 00, 00, 01, 00, 00, 00, 24, 61, 31, 62, 32, 63, 33, 64, 34, 2D, 65, 35, 66, 36, 2D, 30, 37, 31, 38, 2D, 32, 39, 33, 61, 2D, 34, 62, 35, 63, 36, 64, 37, 65, 38, 66, 39, 30, 00, 19, 53, 65, 73, 73, 69, 6F, 6E, 20, 63, 6C, 6F, 73, 65, 64, 20, 67, 72, 61, 63, 65, 66, 75, 6C, 6C, 79, 00]"],
    );

    let actual = match decoded {
        NowMessage::Rdm(now_proto_pdu::NowRdmMessage::SessionNotify(msg)) => msg,
        _ => panic!("Expected RDM SessionNotify message"),
    };

    assert_eq!(actual.session_notify(), NowRdmSessionNotifyKind::CLOSE);
    assert_eq!(actual.session_id(), session_id);
    assert_eq!(actual.log_data(), "Session closed gracefully");
}

#[test]
fn test_session_notify_constructors() {
    let session_id = Uuid::from_bytes([
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
    ]);

    // Test close constructor
    let close_msg = NowRdmSessionNotifyMsg::new_close(session_id);
    assert_eq!(close_msg.session_notify(), NowRdmSessionNotifyKind::CLOSE);
    assert_eq!(close_msg.session_id(), session_id);
    assert_eq!(close_msg.log_data(), "");

    // Test focus constructor
    let focus_msg = NowRdmSessionNotifyMsg::new_focus(session_id);
    assert_eq!(focus_msg.session_notify(), NowRdmSessionNotifyKind::FOCUS);
    assert_eq!(focus_msg.session_id(), session_id);
    assert_eq!(focus_msg.log_data(), "");

    // Test generic constructor
    let generic_msg = NowRdmSessionNotifyMsg::new(NowRdmSessionNotifyKind::FOCUS, session_id);
    assert_eq!(generic_msg.session_notify(), NowRdmSessionNotifyKind::FOCUS);
    assert_eq!(generic_msg.session_id(), session_id);
    assert_eq!(generic_msg.log_data(), "");
}
