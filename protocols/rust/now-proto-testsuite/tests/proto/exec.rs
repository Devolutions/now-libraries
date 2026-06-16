use expect_test::expect;
use now_proto_pdu::*;
use now_proto_testsuite::proto::{now_msg_decodes_into, now_msg_roundtrip};

#[test]
fn roundtrip_exec_abort() {
    let msg = NowExecAbortMsg::new(0x12345678, 1);

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[08, 00, 00, 00, 13, 01, 00, 00, 78, 56, 34, 12, 01, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Abort(msg)) => msg,
        _ => panic!("Expected NowExecAbortMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.exit_code(), 1);
}

#[test]
fn roundtrip_exec_cancel_req() {
    let msg = NowExecCancelReqMsg::new(0x12345678);

    let decoded = now_msg_roundtrip(msg, expect!["[04, 00, 00, 00, 13, 02, 00, 00, 78, 56, 34, 12]"]);

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::CancelReq(msg)) => msg,
        _ => panic!("Expected NowExecCancelReqMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
}

#[test]
fn roundtrip_exec_cancel_rsp() {
    let msg = NowExecCancelRspMsg::new_success(0x12345678);

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0E, 00, 00, 00, 13, 03, 00, 00, 78, 56, 34, 12, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::CancelRsp(msg)) => msg,
        _ => panic!("Expected NowExecCancelRspMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert!(actual.to_result().is_ok());
}

#[test]
fn roundtrip_exec_cancel_rsp_error() {
    let msg = NowExecCancelRspMsg::new_error(0x12345678, NowStatusError::new_generic(0xDEADBEEF)).unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0E, 00, 00, 00, 13, 03, 00, 00, 78, 56, 34, 12, 01, 00, 00, 00, EF, BE, AD, DE, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::CancelRsp(msg)) => msg,
        _ => panic!("Expected NowExecCancelRspMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.to_result().unwrap_err(), NowStatusError::new_generic(0xDEADBEEF));
}

#[test]
fn roundtrip_exec_result_success() {
    let msg = NowExecResultMsg::new_success(0x12345678, 42);

    let decoded = now_msg_roundtrip(
        msg,
        expect![
            "[12, 00, 00, 00, 13, 04, 00, 00, 78, 56, 34, 12, 2A, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00]"
        ],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Result(msg)) => msg,
        _ => panic!("Expected NowExecResultMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.to_result().unwrap(), 42);
}

#[test]
fn roundtrip_exec_result_error() {
    let msg = NowExecResultMsg::new_error(
        0x12345678,
        NowStatusError::new_generic(0xDEADBEEF).with_message("ABC").unwrap(),
    )
    .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[15, 00, 00, 00, 13, 04, 00, 00, 78, 56, 34, 12, 00, 00, 00, 00, 03, 00, 00, 00, EF, BE, AD, DE, 03, 41, 42, 43, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Result(msg)) => msg,
        _ => panic!("Expected NowExecResultMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(
        actual.to_result().unwrap_err(),
        NowStatusError::new_generic(0xDEADBEEF).with_message("ABC").unwrap()
    );
}

#[test]
fn roundtrip_exec_data() {
    let msg = NowExecDataMsg::new(0x12345678, NowExecDataStreamKind::Stdout, true, &[0x01, 0x02, 0x03]).unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[08, 00, 00, 00, 13, 05, 05, 00, 78, 56, 34, 12, 03, 01, 02, 03]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Data(msg)) => msg,
        _ => panic!("Expected NowExecDataMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.stream_kind().unwrap(), NowExecDataStreamKind::Stdout);
    assert!(actual.is_last());
}

#[test]
fn roundtrip_exec_data_empty() {
    let msg = NowExecDataMsg::new(0x12345678, NowExecDataStreamKind::Stdin, false, &[]).unwrap();

    let decoded = now_msg_roundtrip(msg, expect!["[05, 00, 00, 00, 13, 05, 02, 00, 78, 56, 34, 12, 00]"]);

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Data(msg)) => msg,
        _ => panic!("Expected NowExecDataMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.stream_kind().unwrap(), NowExecDataStreamKind::Stdin);
    assert!(!actual.is_last());
    assert_eq!(actual.data(), &[]);
}

#[test]
fn roundtrip_exec_started() {
    let msg = NowExecStartedMsg::new(0x12345678);

    let decoded = now_msg_roundtrip(msg, expect!["[04, 00, 00, 00, 13, 06, 00, 00, 78, 56, 34, 12]"]);

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Started(msg)) => msg,
        _ => panic!("Expected NowExecStartedMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
}

#[test]
fn exec_run_v1_0() {
    let msg = NowExecRunMsg::new(0x1234567, "hello").unwrap();

    const ENCODED: &[u8] = &[
        0x0B, 0x00, 0x00, 0x00, 0x13, 0x10, 0x00, 0x00, 0x67, 0x45, 0x23, 0x01, 0x05, 0x68, 0x65, 0x6C, 0x6C, 0x6F,
        0x00,
    ];

    now_msg_decodes_into(msg, ENCODED);
}

#[test]
fn roundtrip_exec_run() {
    let msg = NowExecRunMsg::new(0x1234567, "hello")
        .unwrap()
        .with_directory("hi")
        .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0F, 00, 00, 00, 13, 10, 01, 00, 67, 45, 23, 01, 05, 68, 65, 6C, 6C, 6F, 00, 02, 68, 69, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Run(msg)) => msg,
        _ => panic!("Expected NowExecRunMsg"),
    };

    assert_eq!(actual.session_id(), 0x1234567);
    assert_eq!(actual.command(), "hello");
}

#[test]
fn roundtrip_exec_process() {
    let msg = NowExecProcessMsg::new(0x12345678, "a")
        .unwrap()
        .with_parameters("b")
        .unwrap()
        .with_directory("c")
        .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0D, 00, 00, 00, 13, 11, 03, 00, 78, 56, 34, 12, 01, 61, 00, 01, 62, 00, 01, 63, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Process(msg)) => msg,
        _ => panic!("Expected NowExecProcessMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.filename(), "a");
    assert_eq!(actual.parameters().unwrap(), "b");
    assert_eq!(actual.directory().unwrap(), "c");
}

#[test]
fn roundtrip_exec_process_simple() {
    let msg = NowExecProcessMsg::new(0x12345678, "a").unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0B, 00, 00, 00, 13, 11, 00, 00, 78, 56, 34, 12, 01, 61, 00, 00, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Process(msg)) => msg,
        _ => panic!("Expected NowExecProcessMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.filename(), "a");
    assert!(actual.parameters().is_none());
    assert!(actual.directory().is_none());
}

#[test]
fn roundtrip_exec_shell() {
    let msg = NowExecShellMsg::new(0x12345678, "a")
        .unwrap()
        .with_shell("b")
        .unwrap()
        .with_directory("c")
        .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0D, 00, 00, 00, 13, 12, 03, 00, 78, 56, 34, 12, 01, 61, 00, 01, 62, 00, 01, 63, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Shell(msg)) => msg,
        _ => panic!("Expected NowExecShellMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.command(), "a");
    assert_eq!(actual.shell().unwrap(), "b");
    assert_eq!(actual.directory().unwrap(), "c");
}

#[test]
fn roundtrip_exec_shell_simple() {
    let msg = NowExecShellMsg::new(0x12345678, "a").unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0B, 00, 00, 00, 13, 12, 00, 00, 78, 56, 34, 12, 01, 61, 00, 00, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Shell(msg)) => msg,
        _ => panic!("Expected NowExecShellMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.command(), "a");
    assert!(actual.shell().is_none());
    assert!(actual.directory().is_none());
}

#[test]
fn roundtrip_exec_batch() {
    let msg = NowExecBatchMsg::new(0x12345678, "a")
        .unwrap()
        .with_directory("b")
        .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0A, 00, 00, 00, 13, 13, 01, 00, 78, 56, 34, 12, 01, 61, 00, 01, 62, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Batch(msg)) => msg,
        _ => panic!("Expected NowExecBatchMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.command(), "a");
    assert_eq!(actual.directory().unwrap(), "b");
}

#[test]
fn roundtrip_exec_batch_simple() {
    let msg = NowExecBatchMsg::new(0x12345678, "a").unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[09, 00, 00, 00, 13, 13, 00, 00, 78, 56, 34, 12, 01, 61, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Batch(msg)) => msg,
        _ => panic!("Expected NowExecBatchMsg"),
    };

    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.command(), "a");
    assert!(actual.directory().is_none());
}

#[test]
fn roundtrip_exec_ps() {
    let msg = NowExecWinPsMsg::new(0x12345678, "a")
        .unwrap()
        .with_apartment_state(ComApartmentStateKind::Mta)
        .set_no_profile()
        .set_no_logo()
        .with_directory("d")
        .unwrap()
        .with_execution_policy("b")
        .unwrap()
        .with_configuration_name("c")
        .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[10, 00, 00, 00, 13, 14, D9, 01, 78, 56, 34, 12, 01, 61, 00, 01, 64, 00, 01, 62, 00, 01, 63, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::WinPs(msg)) => msg,
        _ => panic!("Expected NowExecPwshMsg::WinPs"),
    };

    assert!(actual.is_no_profile());
    assert!(actual.is_no_logo());
    assert_eq!(actual.apartment_state().unwrap(), Some(ComApartmentStateKind::Mta));
    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.command(), "a");
    assert_eq!(actual.directory().unwrap(), "d");
    assert_eq!(actual.execution_policy().unwrap(), "b");
    assert_eq!(actual.configuration_name().unwrap(), "c");
}

#[test]
fn roundtrip_exec_ps_simple() {
    let msg = NowExecWinPsMsg::new(0x12345678, "a").unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0D, 00, 00, 00, 13, 14, 00, 00, 78, 56, 34, 12, 01, 61, 00, 00, 00, 00, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::WinPs(msg)) => msg,
        _ => panic!("Expected NowExecPwshMsg::WinPs"),
    };

    assert!(!actual.is_no_profile());
    assert!(!actual.is_no_logo());
    assert!(actual.apartment_state().unwrap().is_none());
    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.command(), "a");
    assert!(actual.directory().is_none());
    assert!(actual.execution_policy().is_none());
    assert!(actual.configuration_name().is_none());
}

#[test]
fn roundtrip_exec_pwsh() {
    let msg = NowExecPwshMsg::new(0x12345678, "a")
        .unwrap()
        .with_apartment_state(ComApartmentStateKind::Mta)
        .set_no_profile()
        .set_no_logo()
        .with_directory("d")
        .unwrap()
        .with_execution_policy("b")
        .unwrap()
        .with_configuration_name("c")
        .unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[10, 00, 00, 00, 13, 15, D9, 01, 78, 56, 34, 12, 01, 61, 00, 01, 64, 00, 01, 62, 00, 01, 63, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Pwsh(msg)) => msg,
        _ => panic!("Expected NowExecPwshMsg::Pwsh"),
    };

    assert!(actual.is_no_profile());
    assert!(actual.is_no_logo());
    assert_eq!(actual.apartment_state().unwrap(), Some(ComApartmentStateKind::Mta));
    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.command(), "a");
    assert_eq!(actual.directory().unwrap(), "d");
    assert_eq!(actual.execution_policy().unwrap(), "b");
    assert_eq!(actual.configuration_name().unwrap(), "c");
}

#[test]
fn roundtrip_exec_pwsh_simple() {
    let msg = NowExecPwshMsg::new(0x12345678, "a").unwrap();

    let decoded = now_msg_roundtrip(
        msg,
        expect!["[0D, 00, 00, 00, 13, 15, 00, 00, 78, 56, 34, 12, 01, 61, 00, 00, 00, 00, 00, 00, 00]"],
    );

    let actual = match decoded {
        NowMessage::Exec(NowExecMessage::Pwsh(msg)) => msg,
        _ => panic!("Expected NowExecPwshMsg::Pwsh"),
    };

    assert!(!actual.is_no_profile());
    assert!(!actual.is_no_logo());
    assert!(actual.apartment_state().unwrap().is_none());
    assert_eq!(actual.session_id(), 0x12345678);
    assert_eq!(actual.command(), "a");
    assert!(actual.directory().is_none());
    assert!(actual.execution_policy().is_none());
    assert!(actual.configuration_name().is_none());
}
