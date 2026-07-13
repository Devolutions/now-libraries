#![doc = include_str!("../README.md")]

//! Tokio-based high-level client support for the NOW execution protocol.

mod capabilities;
mod client;
mod config;
mod error;
mod exec;
mod frame;

pub use capabilities::NegotiatedCapabilities;
pub use client::{
    DetachedExecution, Execution, ExecutionEvent, ExecutionStatus, ExecutionSubmission, NowClient, NowClientHandle,
};
pub use config::NowClientConfig;
pub use error::NowClientError;
pub use exec::{BatchRequest, PowerShellRequest, ProcessRequest, PwshRequest, RunRequest, WinPsRequest};
