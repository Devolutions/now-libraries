use alloc::borrow::Cow;

use bitflags::bitflags;
use ironrdp_core::{
    cast_length, other_err, Decode, DecodeResult, Encode, EncodeResult, IntoOwned, ReadCursor, WriteCursor,
};

use crate::{NowHeader, NowMessage, NowMessageClass, NowSessionMessage, NowSessionMessageKind, NowVarStr};

bitflags! {
    /// Event kind flags for window recording events (internal).
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    struct WindowRecEventFlags: u16 {
        /// NOW-PROTO: NOW_WINDOW_REC_EVENT_ACTIVE_WINDOW
        const ACTIVE_WINDOW = 0x0001;
        /// NOW-PROTO: NOW_WINDOW_REC_EVENT_TITLE_CHANGED
        const TITLE_CHANGED = 0x0002;
        /// NOW-PROTO: NOW_WINDOW_REC_EVENT_NO_ACTIVE_WINDOW
        const NO_ACTIVE_WINDOW = 0x0004;
    }
}

/// Active window event data.
///
/// NOW-PROTO: NOW_WINDOW_REC_EVENT_ACTIVE_WINDOW
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ActiveWindowEventData<'a> {
    process_id: u32,
    title: NowVarStr<'a>,
    executable_path: NowVarStr<'a>,
}

impl ActiveWindowEventData<'_> {
    /// Get the process ID of the active window.
    pub fn process_id(&self) -> u32 {
        self.process_id
    }

    /// Get the window title.
    pub fn title(&self) -> &str {
        self.title.as_ref()
    }

    /// Get the executable path.
    pub fn executable_path(&self) -> &str {
        self.executable_path.as_ref()
    }
}

/// Title changed event data.
///
/// NOW-PROTO: NOW_WINDOW_REC_EVENT_TITLE_CHANGED
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TitleChangedEventData<'a> {
    title: NowVarStr<'a>,
}

impl TitleChangedEventData<'_> {
    /// Get the new window title.
    pub fn title(&self) -> &str {
        self.title.as_ref()
    }
}

/// Window recording event kind.
///
/// NOW_PROTO: NOW_SESSION_WINDOW_REC_EVENT_MSG msgFlags
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum WindowRecEventKind<'a> {
    /// Active window changed.
    ///
    /// NOW-PROTO: NOW_WINDOW_REC_EVENT_ACTIVE_WINDOW
    ActiveWindow(ActiveWindowEventData<'a>),
    /// Window title changed for the current active window.
    ///
    /// NOW-PROTO: NOW_WINDOW_REC_EVENT_TITLE_CHANGED
    TitleChanged(TitleChangedEventData<'a>),
    /// No active window.
    ///
    /// NOW-PROTO: NOW_WINDOW_REC_EVENT_NO_ACTIVE_WINDOW
    NoActiveWindow,
}

pub type OwnedActiveWindowEventData = ActiveWindowEventData<'static>;

impl IntoOwned for ActiveWindowEventData<'_> {
    type Owned = OwnedActiveWindowEventData;

    fn into_owned(self) -> Self::Owned {
        OwnedActiveWindowEventData {
            process_id: self.process_id,
            title: self.title.into_owned(),
            executable_path: self.executable_path.into_owned(),
        }
    }
}

pub type OwnedTitleChangedEventData = TitleChangedEventData<'static>;

impl IntoOwned for TitleChangedEventData<'_> {
    type Owned = OwnedTitleChangedEventData;

    fn into_owned(self) -> Self::Owned {
        OwnedTitleChangedEventData {
            title: self.title.into_owned(),
        }
    }
}

pub type OwnedWindowRecEventKind = WindowRecEventKind<'static>;

impl IntoOwned for WindowRecEventKind<'_> {
    type Owned = OwnedWindowRecEventKind;

    fn into_owned(self) -> Self::Owned {
        match self {
            Self::ActiveWindow(data) => OwnedWindowRecEventKind::ActiveWindow(data.into_owned()),
            Self::TitleChanged(data) => OwnedWindowRecEventKind::TitleChanged(data.into_owned()),
            Self::NoActiveWindow => OwnedWindowRecEventKind::NoActiveWindow,
        }
    }
}

/// The NOW_SESSION_WINDOW_REC_EVENT_MSG message is sent by the server to notify of window recording
/// events such as active window changes, title changes, or when no window is active.
///
/// NOW_PROTO: NOW_SESSION_WINDOW_REC_EVENT_MSG
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NowSessionWindowRecEventMsg<'a> {
    timestamp: u64,
    kind: WindowRecEventKind<'a>,
}

impl_pdu_borrowing!(NowSessionWindowRecEventMsg<'_>, OwnedNowSessionWindowRecEventMsg);

impl IntoOwned for NowSessionWindowRecEventMsg<'_> {
    type Owned = OwnedNowSessionWindowRecEventMsg;

    fn into_owned(self) -> Self::Owned {
        OwnedNowSessionWindowRecEventMsg {
            timestamp: self.timestamp,
            kind: self.kind.into_owned(),
        }
    }
}

impl<'a> NowSessionWindowRecEventMsg<'a> {
    const NAME: &'static str = "NOW_SESSION_WINDOW_REC_EVENT_MSG";
    const FIXED_PART_SIZE: usize = 8 + 4; // timestamp (8) + process_id (4)

    pub fn active_window(
        timestamp: u64,
        process_id: u32,
        title: impl Into<Cow<'a, str>>,
        executable_path: impl Into<Cow<'a, str>>,
    ) -> EncodeResult<Self> {
        Ok(Self {
            timestamp,
            kind: WindowRecEventKind::ActiveWindow(ActiveWindowEventData {
                process_id,
                title: NowVarStr::new(title)?,
                executable_path: NowVarStr::new(executable_path)?,
            }),
        })
    }

    pub fn title_changed(timestamp: u64, title: impl Into<Cow<'a, str>>) -> EncodeResult<Self> {
        Ok(Self {
            timestamp,
            kind: WindowRecEventKind::TitleChanged(TitleChangedEventData {
                title: NowVarStr::new(title)?,
            }),
        })
    }

    pub fn no_active_window(timestamp: u64) -> Self {
        Self {
            timestamp,
            kind: WindowRecEventKind::NoActiveWindow,
        }
    }

    pub fn timestamp(&self) -> u64 {
        self.timestamp
    }

    pub fn kind(&self) -> &WindowRecEventKind<'a> {
        &self.kind
    }
}

impl Encode for NowSessionWindowRecEventMsg<'_> {
    fn encode(&self, dst: &mut WriteCursor<'_>) -> EncodeResult<()> {
        let mut title = NowVarStr::default();
        let mut executable_path = NowVarStr::default();

        let (flags, process_id) = match &self.kind {
            WindowRecEventKind::ActiveWindow(data) => {
                title = data.title.clone();
                executable_path = data.executable_path.clone();
                (WindowRecEventFlags::ACTIVE_WINDOW, data.process_id)
            }
            WindowRecEventKind::TitleChanged(data) => {
                title = data.title.clone();
                (WindowRecEventFlags::TITLE_CHANGED, 0)
            }
            WindowRecEventKind::NoActiveWindow => (WindowRecEventFlags::NO_ACTIVE_WINDOW, 0),
        };

        let header = NowHeader {
            size: cast_length!("size", self.body_size())?,
            class: NowMessageClass::SESSION,
            kind: NowSessionMessageKind::WINDOW_REC_EVENT.0,
            flags: flags.bits(),
        };

        header.encode(dst)?;
        dst.write_u64(self.timestamp);
        dst.write_u32(process_id);
        title.encode(dst)?;
        executable_path.encode(dst)?;

        Ok(())
    }

    fn name(&self) -> &'static str {
        Self::NAME
    }

    fn size(&self) -> usize {
        NowHeader::FIXED_PART_SIZE + self.body_size()
    }
}

impl NowSessionWindowRecEventMsg<'_> {
    fn body_size(&self) -> usize {
        // Empty string size (VarU32 length encoding + null terminator, minimum 2 bytes)
        let empty_str_size = NowVarStr::new("").expect("empty string is valid").size();

        let (title_size, exec_path_size) = match &self.kind {
            WindowRecEventKind::ActiveWindow(data) => (data.title.size(), data.executable_path.size()),
            WindowRecEventKind::TitleChanged(data) => (data.title.size(), empty_str_size),
            WindowRecEventKind::NoActiveWindow => (empty_str_size, empty_str_size),
        };

        Self::FIXED_PART_SIZE + title_size + exec_path_size
    }
}

impl<'de> Decode<'de> for NowSessionWindowRecEventMsg<'de> {
    fn decode(src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let header = NowHeader::decode(src)?;

        match (header.class, NowSessionMessageKind(header.kind)) {
            (NowMessageClass::SESSION, NowSessionMessageKind::WINDOW_REC_EVENT) => Self::decode_from_body(header, src),
            _ => Err(unsupported_message_err!(class: header.class.0, kind: header.kind)),
        }
    }
}

impl<'de> NowSessionWindowRecEventMsg<'de> {
    pub(crate) fn decode_from_body(header: NowHeader, src: &mut ReadCursor<'de>) -> DecodeResult<Self> {
        let flags = WindowRecEventFlags::from_bits_retain(header.flags);
        let timestamp = src.read_u64();
        let process_id = src.read_u32();
        let title = NowVarStr::decode(src)?;
        let executable_path = NowVarStr::decode(src)?;

        let kind = if flags.contains(WindowRecEventFlags::ACTIVE_WINDOW) {
            WindowRecEventKind::ActiveWindow(ActiveWindowEventData {
                process_id,
                title,
                executable_path,
            })
        } else if flags.contains(WindowRecEventFlags::TITLE_CHANGED) {
            WindowRecEventKind::TitleChanged(TitleChangedEventData { title })
        } else if flags.contains(WindowRecEventFlags::NO_ACTIVE_WINDOW) {
            WindowRecEventKind::NoActiveWindow
        } else {
            return Err(other_err!(
                "invalid window recording event flags",
                "unsupported flags combination"
            ));
        };

        Ok(Self { timestamp, kind })
    }
}

impl<'a> From<NowSessionWindowRecEventMsg<'a>> for NowMessage<'a> {
    fn from(value: NowSessionWindowRecEventMsg<'a>) -> Self {
        Self::Session(NowSessionMessage::WindowRecEvent(value))
    }
}
