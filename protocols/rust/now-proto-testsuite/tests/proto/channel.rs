use core::time;

use expect_test::expect;
use now_proto_pdu::*;
use now_proto_testsuite::proto::now_msg_roundtrip;

#[test]
fn roundtrip_channel_capset() {
    let msg = NowChannelCapsetMsg::default()
        .with_exec_capset(NowExecCapsetFlags::STYLE_RUN | NowExecCapsetFlags::STYLE_SHELL)
        .with_system_capset(NowSystemCapsetFlags::SHUTDOWN)
        .with_session_capset(NowSessionCapsetFlags::MSGBOX)
        .with_heartbeat_interval(time::Duration::from_secs(300))
        .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0E, 00, 00, 00, 10, 01, 01, 00, 01, 00, 06, 00, 01, 00, 04, 00, 05, 00, 2C, 01, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Channel(NowChannelMessage::Capset(msg)) => msg,
        _ => panic!("Expected NowChannelCapsetMsg"),
    };

    assert_eq!(actual.version().major, NowProtoVersion::CURRENT.major);
    assert_eq!(actual.version().minor, NowProtoVersion::CURRENT.minor);
    assert_eq!(actual.system_capset(), NowSystemCapsetFlags::SHUTDOWN);
    assert_eq!(actual.session_capset(), NowSessionCapsetFlags::MSGBOX);
    assert_eq!(
        actual.exec_capset(),
        NowExecCapsetFlags::STYLE_RUN | NowExecCapsetFlags::STYLE_SHELL
    );
    assert_eq!(actual.heartbeat_interval(), Some(time::Duration::from_secs(300)));
}

#[test]
fn roundtrip_channel_capset_simple() {
    let msg = NowChannelCapsetMsg::default();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0E, 00, 00, 00, 10, 01, 00, 00, 01, 00, 06, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Channel(NowChannelMessage::Capset(msg)) => msg,
        _ => panic!("Expected NowChannelCapsetMsg"),
    };

    assert!(actual.system_capset().is_empty());
    assert!(actual.session_capset().is_empty());
    assert!(actual.exec_capset().is_empty());
    assert!(actual.heartbeat_interval().is_none());
}

#[test]
fn roundtrip_channel_capset_too_small_heartbeat_interval() {
    // Sanity check should fail
    NowChannelCapsetMsg::default()
        .with_heartbeat_interval(time::Duration::from_secs(3))
        .unwrap_err();
}

#[test]
fn roundtrip_channel_capset_too_big_heartbeat_interval() {
    // Sanity check should fail
    NowChannelCapsetMsg::default()
        .with_heartbeat_interval(time::Duration::from_secs(60 * 60 * 24 * 2))
        .unwrap_err();
}

#[test]
fn roundtrip_channel_heartbeat() {
    now_msg_roundtrip(
        NowChannelHeartbeatMsg::default(),
        expect!["[00, 00, 00, 00, 10, 02, 00, 00]"],
    );
}

#[test]
fn roundtrip_channel_close_normal() {
    let msg = NowChannelCloseMsg::default();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0A, 00, 00, 00, 10, 03, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Channel(NowChannelMessage::Close(msg)) => msg,
        _ => panic!("Expected NowChannelCloseMsg"),
    };

    assert!(actual.to_result().is_ok());
}

#[test]
fn roundtrip_channel_close_error() {
    let msg = NowChannelCloseMsg::from_error(NowStatusError::from(NowStatusErrorKind::Generic(0))).unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0A, 00, 00, 00, 10, 03, 00, 00, 01, 00, 00, 00, 00, 00, 00, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Channel(NowChannelMessage::Close(msg)) => msg,
        _ => panic!("Expected NowChannelCloseMsg"),
    };

    assert_eq!(
        actual.to_result(),
        Err(NowStatusError::from(NowStatusErrorKind::Generic(0)))
    );
}
