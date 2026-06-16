use expect_test::expect;
use now_proto_pdu::*;
use now_proto_testsuite::proto::now_msg_roundtrip;

#[test]
fn roundtrip_session_lock() {
    now_msg_roundtrip(
        NowSessionLockMsg::default(),
        expect!["[00, 00, 00, 00, 12, 01, 00, 00]"],
    );
}

#[test]
fn roundtrip_session_logoff() {
    now_msg_roundtrip(
        NowSessionLogoffMsg::default(),
        expect!["[00, 00, 00, 00, 12, 02, 00, 00]"],
    );
}

#[test]
fn roundtrip_session_msgbox_req() {
    let msg = NowSessionMsgBoxReqMsg::new(0x76543210, "hello")
        .unwrap()
        .with_response()
        .with_style(NowMessageBoxStyle::ABORT_RETRY_IGNORE)
        .with_title("world")
        .unwrap()
        .with_timeout(core::time::Duration::from_secs(3))
        .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[1A, 00, 00, 00, 12, 03, 0F, 00, 10, 32, 54, 76, 02, 00, 00, 00, 03, 00, 00, 00, 05, 77, 6F, 72, 6C, 64, 00, 05, 68, 65, 6C, 6C, 6F, 00]"]
    );

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::MsgBoxReq(msg)) => msg,
        _ => panic!("Expected NowSessionMsgBoxReqMsg"),
    };

    assert_eq!(actual.request_id(), 0x76543210);
    assert_eq!(actual.message(), "hello");
    assert!(actual.is_response_expected());
    assert_eq!(actual.style(), NowMessageBoxStyle::ABORT_RETRY_IGNORE);
    assert_eq!(actual.title().unwrap(), "world");
    assert_eq!(actual.timeout().unwrap(), core::time::Duration::from_secs(3));
}

#[test]
fn roundtrip_session_msgbox_req_simple() {
    let msg = NowSessionMsgBoxReqMsg::new(0x76543210, "hello").unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[15, 00, 00, 00, 12, 03, 00, 00, 10, 32, 54, 76, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 05, 68, 65, 6C, 6C, 6F, 00]"]
    );

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::MsgBoxReq(msg)) => msg,
        _ => panic!("Expected NowSessionMsgBoxReqMsg"),
    };

    assert_eq!(actual.request_id(), 0x76543210);
    assert_eq!(actual.message(), "hello");
    assert!(!actual.is_response_expected());
    assert_eq!(actual.style(), NowMessageBoxStyle::OK);
    assert!(actual.title().is_none());
    assert!(actual.timeout().is_none());
}

#[test]
fn roundtrip_session_msgbox_rsp() {
    let msg = NowSessionMsgBoxRspMsg::new_success(0x01234567, NowMsgBoxResponse::RETRY);

    let decoded = now_msg_roundtrip(
        msg,
        expect![
            "[12, 00, 00, 00, 12, 04, 00, 00, 67, 45, 23, 01, 04, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00]"
        ],
    );

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::MsgBoxRsp(msg)) => msg,
        _ => panic!("Expected NowSessionMsgBoxRspMsg"),
    };

    assert_eq!(actual.request_id(), 0x01234567);
    assert_eq!(actual.to_result().unwrap(), NowMsgBoxResponse::RETRY);
}

#[test]
fn roundtrip_session_msgbox_rsp_error() {
    let msg = NowSessionMsgBoxRspMsg::new_error(
        0x01234567,
        NowStatusError::from(NowStatusErrorKind::Now(NowProtoError::NotImplemented)),
    )
    .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect![
            "[12, 00, 00, 00, 12, 04, 00, 00, 67, 45, 23, 01, 00, 00, 00, 00, 01, 00, 01, 00, 07, 00, 00, 00, 00, 00]"
        ],
    );

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::MsgBoxRsp(msg)) => msg,
        _ => panic!("Expected NowSessionMsgBoxRspMsg"),
    };

    assert_eq!(actual.request_id(), 0x01234567);
    assert_eq!(
        actual.to_result().unwrap_err(),
        NowStatusError::from(NowStatusErrorKind::Now(NowProtoError::NotImplemented))
    );
}

#[test]
fn roundtrip_session_set_kbd_layout_specific() {
    let msg = NowSessionSetKbdLayoutMsg::new_specific("00000409").unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0A, 00, 00, 00, 12, 05, 00, 00, 08, 30, 30, 30, 30, 30, 34, 30, 39, 00]"],
    );

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::SetKbdLayout(msg)) => msg,
        _ => panic!("Expected NowSessionSetKbdLayoutMsg"),
    };

    assert_eq!(actual.layout(), SetKbdLayoutOption::Specific("00000409"));
}

#[test]
fn roundtrip_session_set_kbd_layout_next() {
    let msg = NowSessionSetKbdLayoutMsg::new_next();

    let decoded = now_msg_roundtrip(msg, expect!["[02, 00, 00, 00, 12, 05, 01, 00, 00, 00]"]);

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::SetKbdLayout(msg)) => msg,
        _ => panic!("Expected NowSessionSetKbdLayoutMsg"),
    };

    assert_eq!(actual.layout(), SetKbdLayoutOption::Next);
}

#[test]
fn roundtrip_session_set_kbd_layout_prev() {
    let msg = NowSessionSetKbdLayoutMsg::new_prev();

    let decoded = now_msg_roundtrip(msg, expect!["[02, 00, 00, 00, 12, 05, 02, 00, 00, 00]"]);

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::SetKbdLayout(msg)) => msg,
        _ => panic!("Expected NowSessionSetKbdLayoutMsg"),
    };

    assert_eq!(actual.layout(), SetKbdLayoutOption::Prev);
}

#[test]
fn roundtrip_session_window_rec_start_simple() {
    let msg = NowSessionWindowRecStartMsg::new(1000, WindowRecStartFlags::empty());

    let decoded = now_msg_roundtrip(msg, expect!["[04, 00, 00, 00, 12, 06, 00, 00, E8, 03, 00, 00]"]);

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::WindowRecStart(msg)) => msg,
        _ => panic!("Expected NowSessionWindowRecStartMsg"),
    };

    assert_eq!(actual.poll_interval, 1000);
    assert!(!actual.flags.contains(WindowRecStartFlags::TRACK_TITLE_CHANGE));
}

#[test]
fn roundtrip_session_window_rec_start_with_flags() {
    let msg = NowSessionWindowRecStartMsg::new(2000, WindowRecStartFlags::TRACK_TITLE_CHANGE);

    let decoded = now_msg_roundtrip(msg, expect!["[04, 00, 00, 00, 12, 06, 01, 00, D0, 07, 00, 00]"]);

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::WindowRecStart(msg)) => msg,
        _ => panic!("Expected NowSessionWindowRecStartMsg"),
    };

    assert_eq!(actual.poll_interval, 2000);
    assert!(actual.flags.contains(WindowRecStartFlags::TRACK_TITLE_CHANGE));
}

#[test]
fn roundtrip_session_window_rec_stop() {
    now_msg_roundtrip(
        NowSessionWindowRecStopMsg::default(),
        expect!["[00, 00, 00, 00, 12, 07, 00, 00]"],
    );
}

#[test]
fn roundtrip_session_window_rec_event_active_window() {
    let msg = NowSessionWindowRecEventMsg::active_window(
        1732550400, // Unix timestamp: 2024-11-25 12:00:00 UTC
        1234,
        "Notepad",
        "C:\\Windows\\System32\\notepad.exe",
    )
    .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[36, 00, 00, 00, 12, 08, 01, 00, 00, 9F, 44, 67, 00, 00, 00, 00, D2, 04, 00, 00, 07, 4E, 6F, 74, 65, 70, 61, 64, 00, 1F, 43, 3A, 5C, 57, 69, 6E, 64, 6F, 77, 73, 5C, 53, 79, 73, 74, 65, 6D, 33, 32, 5C, 6E, 6F, 74, 65, 70, 61, 64, 2E, 65, 78, 65, 00]"],
    );

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::WindowRecEvent(msg)) => msg,
        _ => panic!("Expected NowSessionWindowRecEventMsg"),
    };

    assert_eq!(actual.timestamp(), 1732550400);

    if let WindowRecEventKind::ActiveWindow(data) = actual.kind() {
        assert_eq!(data.process_id(), 1234);
        assert_eq!(data.title(), "Notepad");
        assert_eq!(data.executable_path(), "C:\\Windows\\System32\\notepad.exe");
    } else {
        panic!("Expected ActiveWindow event kind");
    }
}

#[test]
fn roundtrip_session_window_rec_event_title_changed() {
    let msg = NowSessionWindowRecEventMsg::title_changed(
        1732550460, // Unix timestamp: 2024-11-25 12:01:00 UTC
        "Notepad - Document.txt",
    )
    .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[26, 00, 00, 00, 12, 08, 02, 00, 3C, 9F, 44, 67, 00, 00, 00, 00, 00, 00, 00, 00, 16, 4E, 6F, 74, 65, 70, 61, 64, 20, 2D, 20, 44, 6F, 63, 75, 6D, 65, 6E, 74, 2E, 74, 78, 74, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::WindowRecEvent(msg)) => msg,
        _ => panic!("Expected NowSessionWindowRecEventMsg"),
    };

    assert_eq!(actual.timestamp(), 1732550460);

    if let WindowRecEventKind::TitleChanged(data) = actual.kind() {
        assert_eq!(data.title(), "Notepad - Document.txt");
    } else {
        panic!("Expected TitleChanged event kind");
    }
}

#[test]
fn roundtrip_session_window_rec_event_no_active_window() {
    let msg = NowSessionWindowRecEventMsg::no_active_window(1732550520); // Unix timestamp: 2024-11-25 12:02:00 UTC

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[10, 00, 00, 00, 12, 08, 04, 00, 78, 9F, 44, 67, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::WindowRecEvent(msg)) => msg,
        _ => panic!("Expected NowSessionWindowRecEventMsg"),
    };

    assert_eq!(actual.timestamp(), 1732550520);
    assert!(matches!(actual.kind(), WindowRecEventKind::NoActiveWindow));
}

#[test]
fn roundtrip_session_window_rec_event_empty_strings() {
    let msg = NowSessionWindowRecEventMsg::active_window(1732550400, 5678, "", "").unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[10, 00, 00, 00, 12, 08, 01, 00, 00, 9F, 44, 67, 00, 00, 00, 00, 2E, 16, 00, 00, 00, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Session(NowSessionMessage::WindowRecEvent(msg)) => msg,
        _ => panic!("Expected NowSessionWindowRecEventMsg"),
    };

    assert_eq!(actual.timestamp(), 1732550400);

    if let WindowRecEventKind::ActiveWindow(data) = actual.kind() {
        assert_eq!(data.process_id(), 5678);
        assert_eq!(data.title(), "");
        assert_eq!(data.executable_path(), "");
    } else {
        panic!("Expected ActiveWindow event kind");
    }
}
