use now_proto_pdu::ironrdp_core::encode_vec;
use now_proto_pdu::{
    NowChannelCapsetMsg, NowChannelMessage, NowExecDataStreamKind, NowExecMessage, NowMessage, OwnedNowMessage,
};
use std::collections::{HashMap, HashSet};
use std::sync::{Arc, RwLock};
use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt};
use tokio::sync::{mpsc, oneshot};
use tokio::time::{self, Instant};

use crate::exec::{stdin_message, EncodedRequest, RequestSpec};
use crate::{NegotiatedCapabilities, NowClientConfig, NowClientError};

/// Entry point for connecting a Tokio byte stream to the NOW execution protocol.
pub struct NowClient;

impl NowClient {
    /// Negotiates NOW capabilities over `stream` and starts its single-owner protocol worker.
    pub async fn connect<S>(
        stream: S,
        config: NowClientConfig,
    ) -> Result<(NowClientHandle, NegotiatedCapabilities), NowClientError>
    where
        S: AsyncRead + AsyncWrite + Unpin + Send + 'static,
    {
        config.validate()?;
        let (stream, messages, peer_capset) = handshake(stream, &config).await?;
        let capabilities = NegotiatedCapabilities::negotiate(&config.client_capset, &peer_capset)?;
        let shared_capabilities = Arc::new(RwLock::new(capabilities.clone()));
        let (command_sender, command_receiver) = mpsc::channel(config.command_queue_capacity);
        let worker_sender = command_sender.downgrade();
        let worker = OwnedWorker {
            stream,
            messages,
            requested_capset: config.client_capset.clone(),
            capabilities: Arc::clone(&shared_capabilities),
            command_receiver,
            command_sender: worker_sender,
            operations: HashMap::new(),
            discarded_run_sessions: HashSet::new(),
            run_discard_capacity: config.run_discard_capacity,
            next_session_id: 1,
            read_buffer: vec![0; config.read_buffer_size],
        };

        tokio::spawn(async move {
            worker.run().await;
        });

        Ok((
            NowClientHandle {
                command_sender,
                capabilities: shared_capabilities,
                event_queue_capacity: config.event_queue_capacity,
            },
            capabilities,
        ))
    }
}

/// Cloneable command handle for one connected NOW channel.
#[derive(Clone)]
pub struct NowClientHandle {
    command_sender: mpsc::Sender<WorkerCommand>,
    capabilities: Arc<RwLock<NegotiatedCapabilities>>,
    event_queue_capacity: usize,
}

impl NowClientHandle {
    /// Returns the latest negotiated capabilities, including capset refreshes from the peer.
    pub fn capabilities(&self) -> NegotiatedCapabilities {
        self.capabilities
            .read()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .clone()
    }

    /// Submits an untracked Run request.
    pub async fn run(&self, request: crate::RunRequest) -> Result<DetachedExecution, NowClientError> {
        match self.submit(RequestSpec::Run(request)).await? {
            ExecutionSubmission::Detached(execution) => Ok(execution),
            ExecutionSubmission::Tracked(_) => Err(NowClientError::Protocol(
                "Run request unexpectedly started a tracked operation".to_owned(),
            )),
        }
    }

    /// Submits a Process request.
    pub async fn process(&self, request: crate::ProcessRequest) -> Result<ExecutionSubmission, NowClientError> {
        self.submit(RequestSpec::Process(request)).await
    }

    /// Submits a Batch request.
    pub async fn batch(&self, request: crate::BatchRequest) -> Result<ExecutionSubmission, NowClientError> {
        self.submit(RequestSpec::Batch(request)).await
    }

    /// Submits a Windows PowerShell request.
    pub async fn win_ps(&self, request: crate::WinPsRequest) -> Result<ExecutionSubmission, NowClientError> {
        self.submit(RequestSpec::WinPs(request)).await
    }

    /// Submits a PowerShell 7 request.
    pub async fn pwsh(&self, request: crate::PwshRequest) -> Result<ExecutionSubmission, NowClientError> {
        self.submit(RequestSpec::Pwsh(request)).await
    }

    async fn submit(&self, spec: RequestSpec) -> Result<ExecutionSubmission, NowClientError> {
        let tracked = spec.is_tracked();
        let (start_sender, start_receiver) = oneshot::channel();
        let (registration, events, terminal) = if tracked {
            let (event_sender, event_receiver) = mpsc::channel(self.event_queue_capacity);
            let (terminal_sender, terminal_receiver) = oneshot::channel();
            (
                Some(OperationRegistration {
                    event_sender,
                    terminal_sender,
                }),
                Some(event_receiver),
                Some(terminal_receiver),
            )
        } else {
            (None, None, None)
        };

        self.command_sender
            .send(WorkerCommand::Start {
                spec,
                registration,
                response: start_sender,
            })
            .await
            .map_err(|_| NowClientError::WorkerClosed("command queue is closed".to_owned()))?;
        let session_id = start_receiver
            .await
            .map_err(|_| NowClientError::WorkerClosed("worker stopped while starting operation".to_owned()))??;

        if tracked {
            let events = events.ok_or_else(|| {
                NowClientError::Protocol("tracked operation did not receive an event receiver".to_owned())
            })?;
            let terminal = terminal.ok_or_else(|| {
                NowClientError::Protocol("tracked operation did not receive a terminal receiver".to_owned())
            })?;
            Ok(ExecutionSubmission::Tracked(Execution {
                session_id,
                events,
                terminal,
                command_sender: self.command_sender.clone(),
            }))
        } else {
            Ok(ExecutionSubmission::Detached(DetachedExecution { session_id }))
        }
    }
}

/// Either a tracked execution handle or a detached submission identity.
pub enum ExecutionSubmission {
    /// A tracked operation that emits output and has a terminal result.
    Tracked(Execution),
    /// A detached operation that only reports local submission.
    Detached(DetachedExecution),
}

impl ExecutionSubmission {
    /// Returns the NOW session identity allocated for the submission.
    pub fn id(&self) -> u32 {
        match self {
            Self::Tracked(execution) => execution.id(),
            Self::Detached(execution) => execution.id(),
        }
    }
}

/// Fire-and-forget NOW execution submission.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct DetachedExecution {
    session_id: u32,
}

impl DetachedExecution {
    /// Returns the NOW session identity allocated for this submission.
    pub fn id(&self) -> u32 {
        self.session_id
    }
}

/// A tracked NOW execution.
pub struct Execution {
    session_id: u32,
    events: mpsc::Receiver<ExecutionEvent>,
    terminal: oneshot::Receiver<Result<ExecutionStatus, NowClientError>>,
    command_sender: mpsc::Sender<WorkerCommand>,
}

impl Execution {
    /// Returns the NOW session identity allocated for this execution.
    pub fn id(&self) -> u32 {
        self.session_id
    }

    /// Receives the next execution event, or `None` when the event stream is closed.
    pub async fn next_event(&mut self) -> Option<ExecutionEvent> {
        self.events.recv().await
    }

    /// Forwards raw bytes to the remote standard input stream.
    pub async fn send_stdin(&self, data: Vec<u8>, last: bool) -> Result<(), NowClientError> {
        let (response_sender, response_receiver) = oneshot::channel();
        self.command_sender
            .send(WorkerCommand::SendStdin {
                session_id: self.session_id,
                data,
                last,
                response: response_sender,
            })
            .await
            .map_err(|_| NowClientError::WorkerClosed("command queue is closed".to_owned()))?;
        response_receiver
            .await
            .map_err(|_| NowClientError::WorkerClosed("worker stopped while writing stdin".to_owned()))?
    }

    /// Sends a cancel request and waits until the peer responds to it.
    ///
    /// Call [`Self::wait`] afterwards to observe the matching terminal result.
    pub async fn cancel(&self) -> Result<(), NowClientError> {
        let (response_sender, response_receiver) = oneshot::channel();
        self.command_sender
            .send(WorkerCommand::Cancel {
                session_id: self.session_id,
                response: Some(response_sender),
            })
            .await
            .map_err(|_| NowClientError::WorkerClosed("command queue is closed".to_owned()))?;
        response_receiver
            .await
            .map_err(|_| NowClientError::WorkerClosed("worker stopped while waiting for cancel response".to_owned()))?
    }

    /// Waits for the remote terminal result.
    ///
    /// A nonzero remote exit code is returned as [`ExecutionStatus::Completed`], not an error.
    pub async fn wait(self) -> Result<ExecutionStatus, NowClientError> {
        self.terminal
            .await
            .map_err(|_| NowClientError::WorkerClosed("worker stopped before terminal result".to_owned()))?
    }
}

/// Events emitted by a tracked execution.
#[derive(Debug, PartialEq, Eq)]
pub enum ExecutionEvent {
    /// The peer accepted and started the execution.
    Started,
    /// Raw stdout bytes and their final-stream marker.
    Stdout {
        /// Bytes exactly as received from NOW.
        data: Vec<u8>,
        /// Whether this is the final stdout chunk.
        last: bool,
    },
    /// Raw stderr bytes and their final-stream marker.
    Stderr {
        /// Bytes exactly as received from NOW.
        data: Vec<u8>,
        /// Whether this is the final stderr chunk.
        last: bool,
    },
    /// The peer accepted a previously requested cancellation.
    CancelAccepted,
}

/// Terminal state of a tracked execution.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ExecutionStatus {
    /// The peer completed the command with this exit code.
    Completed {
        /// Process exit code, including nonzero values.
        exit_code: u32,
    },
    /// The peer accepted cancellation and later returned a terminal result.
    Cancelled,
}

struct OperationRegistration {
    event_sender: mpsc::Sender<ExecutionEvent>,
    terminal_sender: oneshot::Sender<Result<ExecutionStatus, NowClientError>>,
}

struct Operation {
    event_sender: mpsc::Sender<ExecutionEvent>,
    terminal_sender: oneshot::Sender<Result<ExecutionStatus, NowClientError>>,
    cancel_response: Option<oneshot::Sender<Result<(), NowClientError>>>,
    cancel_pending: bool,
    cancel_accepted: bool,
    stdin_closed: bool,
}

#[derive(Default)]
struct BufferUpdate {
    capset_updated: bool,
    heartbeat_received: bool,
}

impl BufferUpdate {
    fn merge(&mut self, other: Self) {
        self.capset_updated |= other.capset_updated;
        self.heartbeat_received |= other.heartbeat_received;
    }
}

enum WorkerCommand {
    Start {
        spec: RequestSpec,
        registration: Option<OperationRegistration>,
        response: oneshot::Sender<Result<u32, NowClientError>>,
    },
    SendStdin {
        session_id: u32,
        data: Vec<u8>,
        last: bool,
        response: oneshot::Sender<Result<(), NowClientError>>,
    },
    Cancel {
        session_id: u32,
        response: Option<oneshot::Sender<Result<(), NowClientError>>>,
    },
}

struct OwnedWorker<S> {
    stream: S,
    messages: crate::frame::MessageBuffer,
    requested_capset: NowChannelCapsetMsg,
    capabilities: Arc<RwLock<NegotiatedCapabilities>>,
    command_receiver: mpsc::Receiver<WorkerCommand>,
    command_sender: mpsc::WeakSender<WorkerCommand>,
    operations: HashMap<u32, Operation>,
    discarded_run_sessions: HashSet<u32>,
    run_discard_capacity: usize,
    next_session_id: u32,
    read_buffer: Vec<u8>,
}

impl<S> OwnedWorker<S>
where
    S: AsyncRead + AsyncWrite + Unpin + Send + 'static,
{
    async fn run(mut self) {
        let mut heartbeat_deadline = self.heartbeat_deadline();
        loop {
            match self.process_buffer() {
                Ok(update) => {
                    if update.capset_updated || update.heartbeat_received {
                        heartbeat_deadline = self.heartbeat_deadline();
                    }
                }
                Err(error) => {
                    self.fail_all(&error);
                    return;
                }
            }

            enum Event {
                Command(Option<WorkerCommand>),
                Read(std::io::Result<usize>),
                HeartbeatTimeout,
            }

            let event = {
                let heartbeat = async {
                    if let Some(deadline) = heartbeat_deadline {
                        time::sleep_until(deadline).await;
                    } else {
                        core::future::pending::<()>().await;
                    }
                };
                tokio::pin!(heartbeat);
                tokio::select! {
                    command = self.command_receiver.recv() => Event::Command(command),
                    read = self.stream.read(&mut self.read_buffer) => Event::Read(read),
                    _ = &mut heartbeat => Event::HeartbeatTimeout,
                }
            };

            let result = match event {
                Event::Command(Some(command)) => self.handle_command(command).await,
                Event::Command(None) => {
                    let error = NowClientError::WorkerClosed("all client handles were dropped".to_owned());
                    self.fail_all(&error);
                    return;
                }
                Event::Read(Ok(0)) => Err(NowClientError::WorkerClosed("transport reached EOF".to_owned())),
                Event::Read(Ok(read)) => self.messages.push(&self.read_buffer[..read]),
                Event::Read(Err(error)) => Err(error.into()),
                Event::HeartbeatTimeout => Err(NowClientError::WorkerClosed("peer heartbeat timed out".to_owned())),
            };
            if let Err(error) = result {
                self.fail_all(&error);
                return;
            }
        }
    }

    fn heartbeat_deadline(&self) -> Option<Instant> {
        self.capabilities()
            .heartbeat_interval()
            .filter(|interval| !interval.is_zero())
            .map(|interval| Instant::now() + interval.saturating_mul(2))
    }

    fn capabilities(&self) -> NegotiatedCapabilities {
        self.capabilities
            .read()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .clone()
    }

    fn process_buffer(&mut self) -> Result<BufferUpdate, NowClientError> {
        let messages = self.messages.take_ready();
        let mut update = BufferUpdate::default();
        for message in messages {
            update.merge(self.dispatch_message(message)?);
        }
        Ok(update)
    }

    fn dispatch_message(&mut self, message: OwnedNowMessage) -> Result<BufferUpdate, NowClientError> {
        match message {
            NowMessage::Channel(NowChannelMessage::Capset(capset)) => {
                let capabilities = NegotiatedCapabilities::negotiate(&self.requested_capset, &capset)?;
                *self
                    .capabilities
                    .write()
                    .unwrap_or_else(std::sync::PoisonError::into_inner) = capabilities;
                Ok(BufferUpdate {
                    capset_updated: true,
                    heartbeat_received: false,
                })
            }
            NowMessage::Channel(NowChannelMessage::Close(_)) => {
                Err(NowClientError::WorkerClosed("peer closed the NOW channel".to_owned()))
            }
            NowMessage::Exec(NowExecMessage::Started(message)) => {
                let session_id = message.session_id();
                if !self.discarded_run_sessions.contains(&session_id) {
                    self.emit_event(session_id, ExecutionEvent::Started);
                }
                Ok(BufferUpdate::default())
            }
            NowMessage::Exec(NowExecMessage::Data(message)) => {
                if self.discarded_run_sessions.contains(&message.session_id()) {
                    return Ok(BufferUpdate::default());
                }
                let event = match message
                    .stream_kind()
                    .map_err(|error| NowClientError::PduDecode(error.to_string()))?
                {
                    NowExecDataStreamKind::Stdout => Some(ExecutionEvent::Stdout {
                        data: message.data().to_vec(),
                        last: message.is_last(),
                    }),
                    NowExecDataStreamKind::Stderr => Some(ExecutionEvent::Stderr {
                        data: message.data().to_vec(),
                        last: message.is_last(),
                    }),
                    NowExecDataStreamKind::Stdin => None,
                };
                if let Some(event) = event {
                    self.emit_event(message.session_id(), event);
                }
                Ok(BufferUpdate::default())
            }
            NowMessage::Exec(NowExecMessage::CancelRsp(message)) => {
                let session_id = message.session_id();
                match message.to_result() {
                    Ok(()) => {
                        self.emit_event(session_id, ExecutionEvent::CancelAccepted);
                        if let Some(operation) = self.operations.get_mut(&session_id) {
                            operation.cancel_accepted = true;
                            if let Some(response) = operation.cancel_response.take() {
                                let _ = response.send(Ok(()));
                            }
                        }
                    }
                    Err(error) => {
                        if let Some(operation) = self.operations.get_mut(&session_id) {
                            operation.cancel_pending = false;
                            if let Some(response) = operation.cancel_response.take() {
                                let _ = response.send(Err(NowClientError::RemoteStatus { session_id, error }));
                            }
                        }
                    }
                }
                Ok(BufferUpdate::default())
            }
            NowMessage::Exec(NowExecMessage::Result(message)) => {
                let session_id = message.session_id();
                if self.discarded_run_sessions.remove(&session_id) {
                    return Ok(BufferUpdate::default());
                }
                match message.to_result() {
                    Ok(exit_code) => {
                        let status = if self
                            .operations
                            .get(&session_id)
                            .is_some_and(|operation| operation.cancel_accepted)
                        {
                            ExecutionStatus::Cancelled
                        } else {
                            ExecutionStatus::Completed { exit_code }
                        };
                        self.finish(session_id, Ok(status));
                    }
                    Err(error) => self.finish(session_id, Err(NowClientError::RemoteStatus { session_id, error })),
                }
                Ok(BufferUpdate::default())
            }
            NowMessage::Channel(NowChannelMessage::Heartbeat(_)) => Ok(BufferUpdate {
                capset_updated: false,
                heartbeat_received: true,
            }),
            _ => Ok(BufferUpdate::default()),
        }
    }

    async fn handle_command(&mut self, command: WorkerCommand) -> Result<(), NowClientError> {
        match command {
            WorkerCommand::Start {
                spec,
                registration,
                response,
            } => {
                let tracked = spec.is_tracked();
                let is_run = spec.is_run();
                if tracked != registration.is_some() {
                    let _ = response.send(Err(NowClientError::Protocol(
                        "worker received inconsistent operation registration".to_owned(),
                    )));
                    return Ok(());
                }
                if tracked && !self.operations.is_empty() {
                    let _ = response.send(Err(NowClientError::OperationInProgress));
                    return Ok(());
                }
                if is_run && self.discarded_run_sessions.len() == self.run_discard_capacity {
                    let _ = response.send(Err(NowClientError::RunDiscardQueueFull));
                    return Ok(());
                }
                let session_id = self.allocate_session_id();
                let request = match spec.build(session_id, &self.capabilities()) {
                    Ok(request) => request,
                    Err(error) => {
                        let _ = response.send(Err(error));
                        return Ok(());
                    }
                };
                let initial_stdin = match spec
                    .initial_stdin()
                    .map(|data| stdin_message(session_id, data.to_vec(), true))
                    .transpose()
                {
                    Ok(stdin) => stdin,
                    Err(error) => {
                        let _ = response.send(Err(error));
                        return Ok(());
                    }
                };
                let stdin_closed = initial_stdin.is_some();

                if let Err(error) = self.write_message(request).await {
                    let response_error = NowClientError::WorkerClosed(error.to_string());
                    let _ = response.send(Err(response_error));
                    return Err(error);
                }
                if let Some(stdin) = initial_stdin {
                    if let Err(error) = self
                        .write_message(EncodedRequest {
                            message: stdin,
                            non_interactive: false,
                        })
                        .await
                    {
                        let response_error = NowClientError::WorkerClosed(error.to_string());
                        let _ = response.send(Err(response_error));
                        return Err(error);
                    }
                }

                if let Some(registration) = registration {
                    self.operations.insert(
                        session_id,
                        Operation {
                            event_sender: registration.event_sender,
                            terminal_sender: registration.terminal_sender,
                            cancel_response: None,
                            cancel_pending: false,
                            cancel_accepted: false,
                            stdin_closed,
                        },
                    );
                    if let Some(timeout) = spec.timeout() {
                        if let Some(command_sender) = self.command_sender.upgrade() {
                            tokio::spawn(async move {
                                time::sleep(timeout).await;
                                let _ = command_sender
                                    .send(WorkerCommand::Cancel {
                                        session_id,
                                        response: None,
                                    })
                                    .await;
                            });
                        }
                    }
                }
                if is_run {
                    self.discarded_run_sessions.insert(session_id);
                }
                let _ = response.send(Ok(session_id));
                Ok(())
            }
            WorkerCommand::SendStdin {
                session_id,
                data,
                last,
                response,
            } => {
                let stdin_closed = match self.operations.get(&session_id) {
                    Some(operation) => operation.stdin_closed,
                    None => {
                        let _ = response.send(Err(NowClientError::OperationFinished { session_id }));
                        return Ok(());
                    }
                };
                if stdin_closed {
                    let _ = response.send(Err(NowClientError::InvalidRequest(
                        "standard input is already closed".to_owned(),
                    )));
                    return Ok(());
                }
                match stdin_message(session_id, data, last) {
                    Ok(message) => match self
                        .write_message(EncodedRequest {
                            message,
                            non_interactive: false,
                        })
                        .await
                    {
                        Ok(()) => {
                            if last {
                                if let Some(operation) = self.operations.get_mut(&session_id) {
                                    operation.stdin_closed = true;
                                }
                            }
                            let _ = response.send(Ok(()));
                            Ok(())
                        }
                        Err(error) => {
                            let response_error = NowClientError::WorkerClosed(error.to_string());
                            let _ = response.send(Err(response_error));
                            Err(error)
                        }
                    },
                    Err(error) => {
                        let _ = response.send(Err(error));
                        Ok(())
                    }
                }
            }
            WorkerCommand::Cancel { session_id, response } => {
                let already_pending = match self.operations.get(&session_id) {
                    Some(operation) => operation.cancel_pending,
                    None => {
                        if let Some(response) = response {
                            let _ = response.send(Err(NowClientError::OperationFinished { session_id }));
                        }
                        return Ok(());
                    }
                };
                if already_pending {
                    if let Some(response) = response {
                        let _ = response.send(Err(NowClientError::InvalidRequest(
                            "cancellation is already pending".to_owned(),
                        )));
                    }
                    return Ok(());
                }

                let message = now_proto_pdu::NowExecCancelReqMsg::new(session_id).into();
                match self
                    .write_message(EncodedRequest {
                        message,
                        non_interactive: false,
                    })
                    .await
                {
                    Ok(()) => {
                        if let Some(operation) = self.operations.get_mut(&session_id) {
                            operation.cancel_pending = true;
                            operation.cancel_response = response;
                        }
                        Ok(())
                    }
                    Err(error) => {
                        if let Some(response) = response {
                            let _ = response.send(Err(NowClientError::WorkerClosed(error.to_string())));
                        }
                        Err(error)
                    }
                }
            }
        }
    }

    async fn write_message(&mut self, request: EncodedRequest) -> Result<(), NowClientError> {
        let mut bytes = encode_vec(&request.message).map_err(|error| NowClientError::PduEncode(error.to_string()))?;
        if request.non_interactive {
            // now-proto-pdu 0.4.3 exposes the PowerShell non-interactive flag for decoding but
            // intentionally has no setter. The common NOW header stores flags at bytes 6..8.
            let flags = u16::from_le_bytes([bytes[6], bytes[7]]) | 0x0020;
            bytes[6..8].copy_from_slice(&flags.to_le_bytes());
        }
        self.stream.write_all(&bytes).await?;
        self.stream.flush().await?;
        Ok(())
    }

    fn allocate_session_id(&mut self) -> u32 {
        loop {
            let session_id = self.next_session_id;
            self.next_session_id = self.next_session_id.wrapping_add(1);
            if self.next_session_id == 0 {
                self.next_session_id = 1;
            }
            if session_id != 0
                && !self.operations.contains_key(&session_id)
                && !self.discarded_run_sessions.contains(&session_id)
            {
                return session_id;
            }
        }
    }

    fn emit_event(&mut self, session_id: u32, event: ExecutionEvent) {
        let overflowed = match self.operations.get_mut(&session_id) {
            Some(operation) => match operation.event_sender.try_send(event) {
                Ok(()) => false,
                Err(mpsc::error::TrySendError::Full(_)) => true,
                Err(mpsc::error::TrySendError::Closed(_)) => false,
            },
            None => false,
        };
        if overflowed {
            self.finish(session_id, Err(NowClientError::EventQueueFull { session_id }));
        }
    }

    fn finish(&mut self, session_id: u32, result: Result<ExecutionStatus, NowClientError>) {
        if let Some(mut operation) = self.operations.remove(&session_id) {
            if let Some(cancel_response) = operation.cancel_response.take() {
                let _ = cancel_response.send(Err(NowClientError::OperationFinished { session_id }));
            }
            let _ = operation.terminal_sender.send(result);
        }
    }

    fn fail_all(&mut self, error: &NowClientError) {
        let reason = error.to_string();
        for (session_id, mut operation) in self.operations.drain() {
            if let Some(cancel_response) = operation.cancel_response.take() {
                let _ = cancel_response.send(Err(NowClientError::WorkerClosed(reason.clone())));
            }
            let _ = operation.terminal_sender.send(Err(NowClientError::WorkerClosed(format!(
                "{reason} (session {session_id})"
            ))));
        }
    }
}

async fn handshake<S>(
    mut stream: S,
    config: &NowClientConfig,
) -> Result<(S, crate::frame::MessageBuffer, NowChannelCapsetMsg), NowClientError>
where
    S: AsyncRead + AsyncWrite + Unpin,
{
    let capset: NowMessage<'static> = config.client_capset.clone().into();
    let bytes = encode_vec(&capset).map_err(|error| NowClientError::PduEncode(error.to_string()))?;
    stream.write_all(&bytes).await?;
    stream.flush().await?;

    let receive_capset = async {
        let mut messages = crate::frame::MessageBuffer::new(config.max_frame_body_size);
        let mut read_buffer = vec![0; config.read_buffer_size];
        loop {
            let read = stream.read(&mut read_buffer).await?;
            if read == 0 {
                return Err(NowClientError::WorkerClosed(
                    "transport reached EOF during capability handshake".to_owned(),
                ));
            }
            messages.push(&read_buffer[..read])?;
            let decoded = messages.take_ready();
            let mut decoded = decoded.into_iter();
            while let Some(message) = decoded.next() {
                match message {
                    NowMessage::Channel(NowChannelMessage::Heartbeat(_)) => {}
                    NowMessage::Channel(NowChannelMessage::Capset(capset)) => {
                        messages.restore_ready(decoded);
                        return Ok((messages, capset));
                    }
                    _ => {
                        return Err(NowClientError::Protocol(
                            "expected peer capability set during handshake".to_owned(),
                        ))
                    }
                }
            }
        }
    };

    match time::timeout(config.connect_timeout, receive_capset).await {
        Ok(result) => {
            let (messages, capset) = result?;
            Ok((stream, messages, capset))
        }
        Err(_) => Err(NowClientError::HandshakeTimeout),
    }
}

#[cfg(test)]
mod tests {
    use now_proto_pdu::ironrdp_core::{encode_vec, Decode, IntoOwned, ReadCursor};
    use now_proto_pdu::{
        NowChannelCapsetMsg, NowChannelHeartbeatMsg, NowExecCancelRspMsg, NowExecCapsetFlags, NowExecDataMsg,
        NowExecDataStreamKind, NowExecMessage, NowExecResultMsg, NowExecStartedMsg, NowMessage,
    };
    use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt};

    use super::{ExecutionEvent, ExecutionStatus, ExecutionSubmission, NowClient};
    use crate::{NowClientConfig, NowClientError, ProcessRequest, RunRequest};

    fn encode(message: impl Into<NowMessage<'static>>) -> Vec<u8> {
        match encode_vec(&message.into()) {
            Ok(bytes) => bytes,
            Err(error) => panic!("test PDU must encode: {error}"),
        }
    }

    async fn read_message<S>(stream: &mut S) -> NowMessage<'static>
    where
        S: AsyncRead + Unpin,
    {
        let mut header = [0; 8];
        if let Err(error) = stream.read_exact(&mut header).await {
            panic!("test peer must receive a header: {error}");
        }
        let body_size = u32::from_le_bytes([header[0], header[1], header[2], header[3]]) as usize;
        let mut bytes = header.to_vec();
        bytes.resize(8 + body_size, 0);
        if let Err(error) = stream.read_exact(&mut bytes[8..]).await {
            panic!("test peer must receive a body: {error}");
        }
        let mut cursor = ReadCursor::new(&bytes);
        match NowMessage::decode(&mut cursor) {
            Ok(message) => message.into_owned(),
            Err(error) => panic!("test peer must decode a PDU: {error}"),
        }
    }

    async fn write_message<S>(stream: &mut S, message: impl Into<NowMessage<'static>>)
    where
        S: AsyncWrite + Unpin,
    {
        let bytes = encode(message);
        if let Err(error) = stream.write_all(&bytes).await {
            panic!("test peer must write a PDU: {error}");
        }
    }

    fn capset(flags: NowExecCapsetFlags) -> NowChannelCapsetMsg {
        NowChannelCapsetMsg::default().with_exec_capset(flags)
    }

    #[tokio::test]
    async fn handshake_rejects_an_incompatible_major_version() {
        let (client_stream, mut peer_stream) = tokio::io::duplex(1024);
        let peer = tokio::spawn(async move {
            let _ = read_message(&mut peer_stream).await;
            let mut bytes = encode(capset(NowExecCapsetFlags::STYLE_RUN));
            bytes[8..10].copy_from_slice(&2u16.to_le_bytes());
            if let Err(error) = peer_stream.write_all(&bytes).await {
                panic!("test peer must write an incompatible capset: {error}");
            }
        });

        let result = NowClient::connect(client_stream, NowClientConfig::default()).await;
        assert!(matches!(result, Err(NowClientError::IncompatibleVersion { .. })));
        match peer.await {
            Ok(()) => {}
            Err(error) => panic!("test peer task failed: {error}"),
        }
    }

    #[tokio::test]
    async fn handshake_intersects_peer_capabilities_defensively() {
        let (client_stream, mut peer_stream) = tokio::io::duplex(1024);
        let peer = tokio::spawn(async move {
            let _ = read_message(&mut peer_stream).await;
            write_message(&mut peer_stream, NowChannelHeartbeatMsg::default()).await;
            write_message(
                &mut peer_stream,
                capset(
                    NowExecCapsetFlags::STYLE_RUN
                        | NowExecCapsetFlags::STYLE_PROCESS
                        | NowExecCapsetFlags::IO_REDIRECTION,
                ),
            )
            .await;
        });

        let config = NowClientConfig {
            client_capset: capset(NowExecCapsetFlags::STYLE_RUN),
            ..NowClientConfig::default()
        };
        let (handle, capabilities) = match NowClient::connect(client_stream, config).await {
            Ok(connection) => connection,
            Err(error) => panic!("handshake must succeed: {error}"),
        };
        assert!(capabilities.supports_run());
        assert!(!capabilities.supports_process());
        assert!(!handle.capabilities().supports_process());
        drop(handle);
        match peer.await {
            Ok(()) => {}
            Err(error) => panic!("test peer task failed: {error}"),
        }
    }

    #[tokio::test]
    async fn run_frames_are_discarded_before_the_following_process() {
        let (client_stream, mut peer_stream) = tokio::io::duplex(4096);
        let (release_sender, release_receiver) = tokio::sync::oneshot::channel();
        let peer = tokio::spawn(async move {
            let _ = read_message(&mut peer_stream).await;
            write_message(
                &mut peer_stream,
                capset(
                    NowExecCapsetFlags::STYLE_RUN
                        | NowExecCapsetFlags::STYLE_PROCESS
                        | NowExecCapsetFlags::IO_REDIRECTION,
                ),
            )
            .await;

            let run_session = match read_message(&mut peer_stream).await {
                NowMessage::Exec(NowExecMessage::Run(message)) => message.session_id(),
                message => panic!("expected Run request, got {message:?}"),
            };
            write_message(&mut peer_stream, NowExecStartedMsg::new(run_session)).await;
            let run_data = match NowExecDataMsg::new(run_session, NowExecDataStreamKind::Stdout, true, vec![0xaa, 0xbb])
            {
                Ok(message) => message,
                Err(error) => panic!("test Run data PDU must encode: {error}"),
            };
            write_message(&mut peer_stream, run_data).await;
            write_message(&mut peer_stream, NowExecResultMsg::new_success(run_session, 0)).await;

            let process_session = match read_message(&mut peer_stream).await {
                NowMessage::Exec(NowExecMessage::Process(message)) => message.session_id(),
                message => panic!("expected Process request, got {message:?}"),
            };
            match read_message(&mut peer_stream).await {
                NowMessage::Exec(NowExecMessage::Data(message)) => {
                    assert_eq!(message.session_id(), process_session);
                    assert_eq!(message.data(), [0x01, 0xff]);
                    assert!(message.is_last());
                }
                message => panic!("expected Process stdin, got {message:?}"),
            }
            if release_receiver.await.is_err() {
                panic!("test must release the Process response");
            }
            write_message(&mut peer_stream, NowExecStartedMsg::new(process_session)).await;
            let stdout = match NowExecDataMsg::new(
                process_session,
                NowExecDataStreamKind::Stdout,
                true,
                vec![0xff, 0x00, 0x80],
            ) {
                Ok(message) => message,
                Err(error) => panic!("test Process data PDU must encode: {error}"),
            };
            write_message(&mut peer_stream, stdout).await;
            write_message(&mut peer_stream, NowExecResultMsg::new_success(process_session, 17)).await;
        });

        let (handle, _) = match NowClient::connect(client_stream, NowClientConfig::default()).await {
            Ok(connection) => connection,
            Err(error) => panic!("handshake must succeed: {error}"),
        };
        let run = match handle.run(RunRequest::new("run.exe")).await {
            Ok(submission) => submission,
            Err(error) => panic!("Run request must be accepted: {error}"),
        };
        assert_eq!(run.id(), 1);
        let mut process = match handle
            .process(ProcessRequest::new("process.exe").with_stdin(vec![0x01, 0xff]))
            .await
        {
            Ok(ExecutionSubmission::Tracked(execution)) => execution,
            Ok(ExecutionSubmission::Detached(_)) => panic!("default process request must be tracked"),
            Err(error) => panic!("Process request must start: {error}"),
        };
        match handle.process(ProcessRequest::new("second.exe")).await {
            Err(NowClientError::OperationInProgress) => {}
            Ok(_) => panic!("second tracked execution must be rejected"),
            Err(error) => panic!("unexpected second execution error: {error}"),
        }
        if release_sender.send(()).is_err() {
            panic!("test peer stopped before Process response");
        }

        assert_eq!(process.next_event().await, Some(ExecutionEvent::Started));
        assert_eq!(
            process.next_event().await,
            Some(ExecutionEvent::Stdout {
                data: vec![0xff, 0x00, 0x80],
                last: true,
            })
        );
        match process.wait().await {
            Ok(ExecutionStatus::Completed { exit_code: 17 }) => {}
            Ok(status) => panic!("Process returned unexpected status: {status:?}"),
            Err(error) => panic!("Process must complete: {error}"),
        }
        match peer.await {
            Ok(()) => {}
            Err(error) => panic!("test peer task failed: {error}"),
        }
    }

    #[tokio::test]
    async fn cancellation_waits_for_the_matching_response_and_result() {
        let (client_stream, mut peer_stream) = tokio::io::duplex(4096);
        let peer = tokio::spawn(async move {
            let _ = read_message(&mut peer_stream).await;
            write_message(
                &mut peer_stream,
                capset(NowExecCapsetFlags::STYLE_PROCESS | NowExecCapsetFlags::IO_REDIRECTION),
            )
            .await;

            let session_id = match read_message(&mut peer_stream).await {
                NowMessage::Exec(NowExecMessage::Process(message)) => message.session_id(),
                message => panic!("expected Process request, got {message:?}"),
            };
            write_message(&mut peer_stream, NowExecStartedMsg::new(session_id)).await;
            match read_message(&mut peer_stream).await {
                NowMessage::Exec(NowExecMessage::CancelReq(message)) => assert_eq!(message.session_id(), session_id),
                message => panic!("expected cancel request, got {message:?}"),
            }
            write_message(&mut peer_stream, NowExecCancelRspMsg::new_success(session_id)).await;
            write_message(&mut peer_stream, NowExecResultMsg::new_success(session_id, 0)).await;
        });

        let (handle, _) = match NowClient::connect(client_stream, NowClientConfig::default()).await {
            Ok(connection) => connection,
            Err(error) => panic!("handshake must succeed: {error}"),
        };
        let mut execution = match handle.process(ProcessRequest::new("process.exe")).await {
            Ok(ExecutionSubmission::Tracked(execution)) => execution,
            Ok(ExecutionSubmission::Detached(_)) => panic!("default process request must be tracked"),
            Err(error) => panic!("Process request must start: {error}"),
        };
        assert_eq!(execution.next_event().await, Some(ExecutionEvent::Started));
        match execution.cancel().await {
            Ok(()) => {}
            Err(error) => panic!("cancellation must be accepted: {error}"),
        }
        assert_eq!(execution.next_event().await, Some(ExecutionEvent::CancelAccepted));
        match execution.wait().await {
            Ok(ExecutionStatus::Cancelled) => {}
            Ok(status) => panic!("unexpected terminal status: {status:?}"),
            Err(error) => panic!("cancellation must complete: {error}"),
        }
        match peer.await {
            Ok(()) => {}
            Err(error) => panic!("test peer task failed: {error}"),
        }
    }
}
