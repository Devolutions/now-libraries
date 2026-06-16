#![doc = include_str!("../README.md")]
#![doc(
    html_logo_url = "https://webdevolutions.blob.core.windows.net/images/projects/devolutions/logos/devolutions-icon-shadow.svg"
)]
#![no_std]

extern crate alloc;

// Re-export ironrdp crates to allow users to use it without additional imports.
pub extern crate ironrdp_core;
pub extern crate ironrdp_error;

pub use uuid::Uuid;

#[macro_use]
mod macros;

// Ensure that we do not compile on platforms with less than 4 bytes per u32. It is pretty safe
// to assume that NOW-PROTO will not ever be used on 8/16-bit MCUs or CPUs.
//
// This is required to safely cast u32 to usize without additional checks.
const_assert!(size_of::<usize>() >= 4);

mod channel;
mod core;
mod exec;
mod message;
mod rdm;
mod session;
mod system;

pub use core::*;

pub use channel::*;
pub use exec::*;
pub use message::*;
pub use rdm::*;
pub use session::*;
pub use system::*;
