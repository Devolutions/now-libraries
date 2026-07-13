use core::fmt::Display;
use core::time::Duration;

use now_proto_pdu::{
    NowExecBatchMsg, NowExecDataMsg, NowExecDataStreamKind, NowExecProcessMsg, NowExecPwshMsg, NowExecRunMsg,
    NowExecWinPsMsg, NowMessage,
};

use crate::{NegotiatedCapabilities, NowClientError};

#[derive(Clone, Debug, PartialEq, Eq)]
struct CommonRequest {
    command: String,
    directory: Option<String>,
    detached: bool,
    stdin: Option<Vec<u8>>,
    timeout: Option<Duration>,
}

impl CommonRequest {
    fn new(command: impl Into<String>) -> Self {
        Self {
            command: command.into(),
            directory: None,
            detached: false,
            stdin: None,
            timeout: None,
        }
    }
}

/// Request for the generic fire-and-forget Run style.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct RunRequest {
    command: String,
    directory: Option<String>,
}

impl RunRequest {
    /// Creates a Run request.
    pub fn new(command: impl Into<String>) -> Self {
        Self {
            command: command.into(),
            directory: None,
        }
    }

    /// Sets the working directory.
    #[must_use]
    pub fn with_directory(mut self, directory: impl Into<String>) -> Self {
        self.directory = Some(directory.into());
        self
    }
}

/// Request for Windows CreateProcess execution.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ProcessRequest {
    filename: String,
    parameters: Option<String>,
    directory: Option<String>,
    detached: bool,
    stdin: Option<Vec<u8>>,
    timeout: Option<Duration>,
}

impl ProcessRequest {
    /// Creates a CreateProcess request.
    pub fn new(filename: impl Into<String>) -> Self {
        Self {
            filename: filename.into(),
            parameters: None,
            directory: None,
            detached: false,
            stdin: None,
            timeout: None,
        }
    }

    /// Sets command-line parameters.
    #[must_use]
    pub fn with_parameters(mut self, parameters: impl Into<String>) -> Self {
        self.parameters = Some(parameters.into());
        self
    }

    /// Sets the working directory.
    #[must_use]
    pub fn with_directory(mut self, directory: impl Into<String>) -> Self {
        self.directory = Some(directory.into());
        self
    }

    /// Requests detached execution.
    #[must_use]
    pub fn detached(mut self) -> Self {
        self.detached = true;
        self
    }

    /// Supplies the first and final stdin chunk.
    #[must_use]
    pub fn with_stdin(mut self, data: impl Into<Vec<u8>>) -> Self {
        self.stdin = Some(data.into());
        self
    }

    /// Sets the tracked execution timeout.
    #[must_use]
    pub fn with_timeout(mut self, timeout: Duration) -> Self {
        self.timeout = Some(timeout);
        self
    }
}

/// Request for a Windows Batch command.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BatchRequest {
    common: CommonRequest,
}

impl BatchRequest {
    /// Creates a Batch request.
    pub fn new(command: impl Into<String>) -> Self {
        Self {
            common: CommonRequest::new(command),
        }
    }

    /// Sets the working directory.
    #[must_use]
    pub fn with_directory(mut self, directory: impl Into<String>) -> Self {
        self.common.directory = Some(directory.into());
        self
    }

    /// Requests detached execution.
    #[must_use]
    pub fn detached(mut self) -> Self {
        self.common.detached = true;
        self
    }

    /// Supplies the first and final stdin chunk.
    #[must_use]
    pub fn with_stdin(mut self, data: impl Into<Vec<u8>>) -> Self {
        self.common.stdin = Some(data.into());
        self
    }

    /// Sets the tracked execution timeout.
    #[must_use]
    pub fn with_timeout(mut self, timeout: Duration) -> Self {
        self.common.timeout = Some(timeout);
        self
    }
}

/// Request shared by Windows PowerShell and PowerShell 7.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PowerShellRequest {
    common: CommonRequest,
    no_profile: bool,
    non_interactive: bool,
}

impl PowerShellRequest {
    /// Creates a PowerShell request.
    pub fn new(command: impl Into<String>) -> Self {
        Self {
            common: CommonRequest::new(command),
            no_profile: false,
            non_interactive: false,
        }
    }

    /// Sets the working directory.
    #[must_use]
    pub fn with_directory(mut self, directory: impl Into<String>) -> Self {
        self.common.directory = Some(directory.into());
        self
    }

    /// Requests detached execution.
    #[must_use]
    pub fn detached(mut self) -> Self {
        self.common.detached = true;
        self
    }

    /// Supplies the first and final stdin chunk.
    #[must_use]
    pub fn with_stdin(mut self, data: impl Into<Vec<u8>>) -> Self {
        self.common.stdin = Some(data.into());
        self
    }

    /// Sets the tracked execution timeout.
    #[must_use]
    pub fn with_timeout(mut self, timeout: Duration) -> Self {
        self.common.timeout = Some(timeout);
        self
    }

    /// Requests PowerShell's `-NoProfile` option.
    #[must_use]
    pub fn with_no_profile(mut self) -> Self {
        self.no_profile = true;
        self
    }

    /// Requests PowerShell's `-NonInteractive` option.
    #[must_use]
    pub fn with_non_interactive(mut self) -> Self {
        self.non_interactive = true;
        self
    }
}

/// Request for Windows PowerShell (`powershell.exe`).
pub type WinPsRequest = PowerShellRequest;
/// Request for PowerShell 7 (`pwsh`).
pub type PwshRequest = PowerShellRequest;

pub(crate) enum RequestSpec {
    Run(RunRequest),
    Process(ProcessRequest),
    Batch(BatchRequest),
    WinPs(WinPsRequest),
    Pwsh(PwshRequest),
}

pub(crate) struct EncodedRequest {
    pub(crate) message: NowMessage<'static>,
    pub(crate) non_interactive: bool,
}

impl RequestSpec {
    pub(crate) fn is_run(&self) -> bool {
        matches!(self, Self::Run(_))
    }

    pub(crate) fn is_tracked(&self) -> bool {
        match self {
            Self::Run(_) => false,
            Self::Process(request) => !request.detached,
            Self::Batch(request) => !request.common.detached,
            Self::WinPs(request) | Self::Pwsh(request) => !request.common.detached,
        }
    }

    pub(crate) fn initial_stdin(&self) -> Option<&[u8]> {
        match self {
            Self::Run(_) => None,
            Self::Process(request) => request.stdin.as_deref(),
            Self::Batch(request) => request.common.stdin.as_deref(),
            Self::WinPs(request) | Self::Pwsh(request) => request.common.stdin.as_deref(),
        }
    }

    pub(crate) fn timeout(&self) -> Option<Duration> {
        match self {
            Self::Run(_) => None,
            Self::Process(request) => request.timeout,
            Self::Batch(request) => request.common.timeout,
            Self::WinPs(request) | Self::Pwsh(request) => request.common.timeout,
        }
    }

    pub(crate) fn build(
        &self,
        session_id: u32,
        capabilities: &NegotiatedCapabilities,
    ) -> Result<EncodedRequest, NowClientError> {
        self.validate(capabilities)?;

        match self {
            Self::Run(request) => {
                let mut message = pdu(NowExecRunMsg::new(session_id, request.command.clone()))?;
                if let Some(directory) = &request.directory {
                    message = pdu(message.with_directory(directory.clone()))?;
                }
                Ok(EncodedRequest {
                    message: message.into(),
                    non_interactive: false,
                })
            }
            Self::Process(request) => {
                let mut message = pdu(NowExecProcessMsg::new(session_id, request.filename.clone()))?;
                if let Some(parameters) = &request.parameters {
                    message = pdu(message.with_parameters(parameters.clone()))?;
                }
                if let Some(directory) = &request.directory {
                    message = pdu(message.with_directory(directory.clone()))?;
                }
                if capabilities.supports_unicode_console() {
                    message = message.with_encoding_utf8();
                }
                if request.detached {
                    message = message.with_detached();
                } else {
                    message = message.with_io_redirection();
                }
                Ok(EncodedRequest {
                    message: message.into(),
                    non_interactive: false,
                })
            }
            Self::Batch(request) => {
                let common = &request.common;
                let mut message = pdu(NowExecBatchMsg::new(session_id, common.command.clone()))?;
                if let Some(directory) = &common.directory {
                    message = pdu(message.with_directory(directory.clone()))?;
                }
                if capabilities.supports_unicode_console() {
                    message = message.with_raw_encoding().with_unicode_console();
                }
                if common.detached {
                    message = message.with_detached();
                } else {
                    message = message.with_io_redirection();
                }
                Ok(EncodedRequest {
                    message: message.into(),
                    non_interactive: false,
                })
            }
            Self::WinPs(request) => build_power_shell(session_id, request, false, capabilities),
            Self::Pwsh(request) => build_power_shell(session_id, request, true, capabilities),
        }
    }

    fn validate(&self, capabilities: &NegotiatedCapabilities) -> Result<(), NowClientError> {
        match self {
            Self::Run(request) => {
                require(capabilities.supports_run(), "Run")?;
                validate_command(&request.command)?;
                validate_directory(request.directory.as_deref())?;
            }
            Self::Process(request) => {
                require(capabilities.supports_process(), "Process")?;
                validate_filename(&request.filename)?;
                validate_optional(&request.parameters, "parameters")?;
                validate_common(request.detached, request.stdin.as_deref(), request.timeout)?;
                validate_tracking(capabilities, request.detached)?;
            }
            Self::Batch(request) => {
                require(capabilities.supports_batch(), "Batch")?;
                validate_common_request(&request.common, capabilities)?;
            }
            Self::WinPs(request) => {
                require(capabilities.supports_win_ps(), "WinPs")?;
                validate_common_request(&request.common, capabilities)?;
            }
            Self::Pwsh(request) => {
                require(capabilities.supports_pwsh(), "Pwsh")?;
                validate_common_request(&request.common, capabilities)?;
            }
        }
        Ok(())
    }
}

macro_rules! apply_power_shell_options {
    ($message:ident, $common:ident, $request:ident, $capabilities:ident) => {
        if let Some(directory) = &$common.directory {
            $message = pdu($message.with_directory(directory.clone()))?;
        }
        if $request.no_profile {
            $message = $message.set_no_profile();
        }
        if $capabilities.supports_unicode_console() {
            $message = $message.with_raw_encoding().with_unicode_console();
        }
        if $common.detached {
            $message = $message.with_detached();
        } else {
            $message = $message.with_io_redirection();
        }
    };
}

fn build_power_shell(
    session_id: u32,
    request: &PowerShellRequest,
    pwsh: bool,
    capabilities: &NegotiatedCapabilities,
) -> Result<EncodedRequest, NowClientError> {
    let common = &request.common;
    if pwsh {
        let mut message = pdu(NowExecPwshMsg::new(session_id, common.command.clone()))?;
        apply_power_shell_options!(message, common, request, capabilities);
        Ok(EncodedRequest {
            message: message.into(),
            non_interactive: request.non_interactive,
        })
    } else {
        let mut message = pdu(NowExecWinPsMsg::new(session_id, common.command.clone()))?;
        apply_power_shell_options!(message, common, request, capabilities);
        Ok(EncodedRequest {
            message: message.into(),
            non_interactive: request.non_interactive,
        })
    }
}

fn validate_common_request(
    request: &CommonRequest,
    capabilities: &NegotiatedCapabilities,
) -> Result<(), NowClientError> {
    validate_command(&request.command)?;
    validate_directory(request.directory.as_deref())?;
    validate_common(request.detached, request.stdin.as_deref(), request.timeout)?;
    validate_tracking(capabilities, request.detached)
}

fn validate_common(detached: bool, stdin: Option<&[u8]>, timeout: Option<Duration>) -> Result<(), NowClientError> {
    if detached && stdin.is_some() {
        return Err(NowClientError::InvalidRequest(
            "detached execution cannot include inline stdin".to_owned(),
        ));
    }
    if detached && timeout.is_some() {
        return Err(NowClientError::InvalidRequest(
            "detached execution cannot include a timeout".to_owned(),
        ));
    }
    if timeout.is_some_and(|timeout| timeout.is_zero()) {
        return Err(NowClientError::InvalidRequest(
            "execution timeout must not be zero".to_owned(),
        ));
    }
    Ok(())
}

fn validate_tracking(capabilities: &NegotiatedCapabilities, detached: bool) -> Result<(), NowClientError> {
    if detached {
        require(capabilities.supports_detached(), "detached execution")
    } else {
        require(capabilities.supports_io_redirection(), "I/O redirection")
    }
}

fn validate_command(command: &str) -> Result<(), NowClientError> {
    if command.trim().is_empty() {
        return Err(NowClientError::InvalidRequest("command must not be empty".to_owned()));
    }
    Ok(())
}

fn validate_filename(filename: &str) -> Result<(), NowClientError> {
    if filename.trim().is_empty() {
        return Err(NowClientError::InvalidRequest("filename must not be empty".to_owned()));
    }
    Ok(())
}

fn validate_directory(directory: Option<&str>) -> Result<(), NowClientError> {
    if directory.is_some_and(str::is_empty) {
        return Err(NowClientError::InvalidRequest(
            "directory must not be empty when specified".to_owned(),
        ));
    }
    Ok(())
}

fn validate_optional(value: &Option<String>, name: &str) -> Result<(), NowClientError> {
    if value.as_deref().is_some_and(str::is_empty) {
        return Err(NowClientError::InvalidRequest(format!(
            "{name} must not be empty when specified"
        )));
    }
    Ok(())
}

fn require(supported: bool, capability: &'static str) -> Result<(), NowClientError> {
    supported
        .then_some(())
        .ok_or(NowClientError::UnsupportedCapability(capability))
}

fn pdu<T, E: Display>(result: Result<T, E>) -> Result<T, NowClientError> {
    result.map_err(|error| NowClientError::PduEncode(error.to_string()))
}

pub(crate) fn stdin_message(session_id: u32, data: Vec<u8>, last: bool) -> Result<NowMessage<'static>, NowClientError> {
    pdu(NowExecDataMsg::new(
        session_id,
        NowExecDataStreamKind::Stdin,
        last,
        data,
    ))
    .map(Into::into)
}
