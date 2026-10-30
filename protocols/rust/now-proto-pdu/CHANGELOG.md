# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [[0.5.0](https://github.com/Devolutions/now-libraries/compare/now-proto-pdu-v0.4.4...now-proto-pdu-v0.5.0)] - 2026-10-30

### <!-- 1 -->Features

- Add elevated execution support to exec messages ([#103](https://github.com/Devolutions/now-libraries/issues/103)) ([4a24d1e50d](https://github.com/Devolutions/now-libraries/commit/4a24d1e50d1b952ff42525c9f34478abbc82746c)) 

  ## Summary
  
  Updates NOW-PROTO to **v1.7**, adding the ability for a client to
  request that an exec session run with elevated privileges.
  
  RDM has had a "Run as administrator" checkbox in *Execute script via
  Devolutions Agent* since the legacy RDM Jump days, where it worked — the
  receiving RDM ran `ShellExecute` with the `runas` verb. The jump
  transport was removed in RDMW-21914 and the NOW exec path has no way to
  carry the intent, so the checkbox has silently done nothing since. This
  PR adds the protocol half and both client libraries.
  
  ## What this adds
  
  - **`NOW_EXEC_FLAG_*_ELEVATED`** on the run, process, shell, batch,
  winps and pwsh exec messages.
  - **`NOW_CAP_EXEC_ELEVATION`** (0x0080) — advertised by servers that can
  run elevated exec sessions. The baseline semantics are the most
  restrictive a platform offers: it may prompt the interactive user for
  consent, and stdio is not redirected.
  - **`NOW_CODE_ELEVATION_DENIED`** (0x0009) and
  **`NOW_CODE_ELEVATION_UNAVAILABLE`** (0x000A) — NOW-level status codes
  so a client never has to sniff Win32 numbers to learn why elevation
  failed.
  - **`NOW_EXEC_FLAG_BATCH_NO_EXIT`** (0x0010) — `cmd /K` rather than
  `/C`. Under elevation there is no stdio, so a console left open is the
  only way the user sees output. winps/pwsh already had `NO_EXIT`; batch
  did not. Must be ignored when the session redirects stdio, where the
  hidden interpreter would never exit.
  - A new normative **"Elevated Execution"** spec section.
  - **Client-side refusal** in both `Devolutions.NowClient` and the Rust
  `now-client`: an elevated request is rejected unless the negotiated
  capset advertises elevation. The .NET client also refuses batch `NoExit`
  to a pre-1.7 peer.
  
  ## Design: intent, not mechanism
  
  The part worth reviewing. Three rules, all aimed at letting a better
  elevation mechanism land later without a breaking change:
  
  1. **The message expresses intent only.** The flag is `ELEVATED`, never
  `RUNAS`. A client cannot select a mechanism, so a server is free to
  change how it elevates without any client change.
  2. **The server advertises what elevation costs** in `execCapset`, the
  same way `UNICODE_CONSOLE` and `IO_REDIRECTION` already do. A client
  learns what to expect *before* sending.
  3. **The capability is named by property, not mechanism.** An earlier
  revision called it `ELEVATE_SHELL`. Clients only ever need to answer two
  questions — *will it prompt?* and *will I get stdio?* — and a
  mechanism-named bit makes them keep a table of mechanisms as more arrive
  (a PEDM token path with no prompt and stdio; macOS `osascript … with
  administrator privileges` with a prompt *and* stdio).
  `NOW_CAP_EXEC_ELEVATION` is the conservative baseline; a future bit
  relaxes exactly one property and says which. That matches the existing
  capset, where every bit names what the host supports rather than how.
  
  Two scoping statements in the spec keep future work unblocked:
  
  - `ELEVATED` raises the privileges of the identity that would otherwise
  run the command. It selects no identity and carries no credentials, so
  "run as another user" has clean ground for a separate field. The spec is
  honest that credential-prompt elevation (a standard user typing an
  admin's password) runs the command as *that* account, because the
  identity sentence is what "run as" will later be built on.
  - An elevation capability means the server implements elevation, **not**
  that every advertised exec style can be elevated. A server that cannot
  elevate a style fails with `ELEVATION_UNAVAILABLE`; a client must handle
  it per style.
  
  Three things the section now pins down because they otherwise become
  support tickets:
  
  - `NOW_EXEC_STARTED_MSG` is sent **after** elevation is granted. The UAC
  consent wait (up to 120 s) happens before it, so clients should start
  execution timers at `STARTED`, not at submission.
  - An elevated session sends **no `EXEC_DATA` at all**, and a server must
  ignore `IO_REDIRECTION` set alongside `ELEVATED`, so no client waits on
  a stream EOF that never comes.
  - Abort and cancel **may fail** (the child runs at a higher integrity
  level) and cannot dismiss a pending prompt.
  
  ## Why the client refuses locally
  
  The spec requires a server to fail rather than execute unelevated — but
  that only binds servers at 1.7 or later. A **pre-1.7 server does not
  know the flag**: it ignores the unknown bit and runs the command with
  normal privileges, with nothing on the wire to distinguish that from
  success. The server-side MUST cannot protect that case, so both clients
  refuse the request when no elevation capability was advertised. Silent
  downgrade is the exact defect this PR exists to remove; it would be
  perverse to reintroduce it in the client.
  
  The Rust `now-client` additionally rejects an elevated request carrying
  inline stdin or a timeout: there is no stdio to write to, and a timeout
  is enforced by cancellation, which may fail.
  
  ## Bit allocation
  
  Each exec message has its own flag namespace; values avoid every bit
  already in use.
  
  | Message | Flag | Value |
  |---|---|---|
  | `NOW_CHANNEL_CAPSET_MSG` (`execCapset`) | `NOW_CAP_EXEC_ELEVATION` |
  `0x0080` |
  | `NOW_EXEC_RUN_MSG` | `NOW_EXEC_FLAG_RUN_ELEVATED` | `0x0002` |
  | `NOW_EXEC_PROCESS_MSG` | `NOW_EXEC_FLAG_PROCESS_ELEVATED` | `0x0008` |
  | `NOW_EXEC_SHELL_MSG` | `NOW_EXEC_FLAG_SHELL_ELEVATED` | `0x0008` |
  | `NOW_EXEC_BATCH_MSG` | `NOW_EXEC_FLAG_BATCH_ELEVATED` | `0x0008` |
  | `NOW_EXEC_BATCH_MSG` | `NOW_EXEC_FLAG_BATCH_NO_EXIT` | `0x0010` |
  | `NOW_EXEC_WINPS_MSG` / `NOW_EXEC_PWSH_MSG` |
  `NOW_EXEC_FLAG_PS_ELEVATED` | `0x0800` |
  | `NOW_STATUS` (`NOW_STATUS_ERROR_KIND_NOW`) |
  `NOW_CODE_ELEVATION_DENIED` | `0x0009` |
  | `NOW_STATUS` (`NOW_STATUS_ERROR_KIND_NOW`) |
  `NOW_CODE_ELEVATION_UNAVAILABLE` | `0x000A` |
  
  ## Included bug fix (separate commit)



## [[0.4.4](https://github.com/Devolutions/now-libraries/compare/now-proto-pdu-v0.4.3...now-proto-pdu-v0.4.4)] - 2026-07-27

### <!-- 6 -->Documentation

- Fix spelling error in `NowMessage` decoding code comment ([#76](https://github.com/Devolutions/now-libraries/issues/76)) ([5d343c5f2e](https://github.com/Devolutions/now-libraries/commit/5d343c5f2e611511762812c61238dfec0699daf2))


## [[0.4.3](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.4.2...now-proto-pdu-v0.4.3)] - 2026-05-15

### <!-- 1 -->Features

- Add support for utf-8 transcoding and unicode console mode for exec commands ([#62](https://github.com/Devolutions/now-proto/issues/62)) ([1e41deee6a](https://github.com/Devolutions/now-proto/commit/1e41deee6aaad4f2b43e3e1ba6ca6c551b43d622)) 

  This PR updates now proto to v1.6, adding the following:
  
  - `now-proto` now requires agent to implement server-side OEM code page
  transcoding to UTF-8.
  - By default redirected IO for cmd, PowerShell5 and PowerShell7 now
  automatically transcoded to UTF-8. Current RDM implementation already
  expects UTF-8, so when new agent version will be installed, "garbaged
  input" issue [DGW-370](https://devolutions.atlassian.net/browse/DGW-370)
  will be automatically fixed.
  - Note that for `process` execution mode, raw (no-transcoding) is still
  default, but transcoding could be enabled by
  `NOW_EXEC_FLAG_PROCESS_ENCODING_UTF8` flag instead, as `process`
  execution is more advanced use case usually needed for bit-bit output,
  so no explicit transcoding is provided.
  - Added `NOW_CAP_EXEC_UNICODE_CONSOLE` capability to signify that new
  flags are supported and the redirected streams are indeed correct utf-8.
  - [testing] Updated CLI test app for encoding testing purposes



## [[0.4.2](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.4.1...now-proto-pdu-v0.4.2)] - 2025-12-02

### <!-- 1 -->Features

- Add window recording support to protocol and libraries ([#52](https://github.com/Devolutions/now-proto/issues/52)) ([e455c4c6e3](https://github.com/Devolutions/now-proto/commit/e455c4c6e3c06e54fc585c8d6f14c315177dd7cf)) 

  Adds new messages for the current active windows tracking.

## [[0.4.1](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.4.0...now-proto-pdu-v0.4.1)] - 2025-11-18

### <!-- 1 -->Features

- Add detached exec mode (ARC-411) ([#48](https://github.com/Devolutions/now-proto/issues/48)) ([a4ce1b2d16](https://github.com/Devolutions/now-proto/commit/a4ce1b2d163b023e4268b6bb6a0afeaf851e23f5)) 

## [[0.4.0](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.3.2...now-proto-pdu-v0.4.0)] - 2025-09-24

### <!-- 1 -->Features

- Implemented NOW-Proto 1.3 features in rust crate ([b99bbeae0c](https://github.com/Devolutions/now-proto/commit/b99bbeae0cda6f6ee20e0f29b6b36ee9abdd34e9)) 

### <!-- 4 -->Bug Fixes

- Update version numbers in libraries ([7296b6d325](https://github.com/Devolutions/now-proto/commit/7296b6d325df4fc08ca18faa1a4e24a322ba2bb7)) 

## [[0.3.2](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.3.1...now-proto-pdu-v0.3.2)] - 2025-09-11

### <!-- 4 -->Bug Fixes

- Add missing NowExecPwshMsg::is_server_mode method ([27fe1341f8](https://github.com/Devolutions/now-proto/commit/27fe1341f8145316f911cd89f83c223a539bc048)) 



## [[0.3.1](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.3.0...now-proto-pdu-v0.3.1)] - 2025-09-11

### <!-- 1 -->Features

- Add PowerShell server mode flag support ([2177c8ece1](https://github.com/Devolutions/now-proto/commit/2177c8ece131a9e82c545caa9a38769cb6b9267b)) 



## [[0.3.0](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.2.0...now-proto-pdu-v0.3.0)] - 2025-08-20

### <!-- 1 -->Features

- Add IO redirection flags to all exec sessions; add missing working directory option to ShellExecute (#29) ([ce0afe06c4](https://github.com/Devolutions/now-proto/commit/ce0afe06c4d1a9f1750eb0055034fd0b896db407)) 

### <!-- 4 -->Bug Fixes

- Add missing forward-compatibility logic to message decoding (#32) ([0adfc78cfa](https://github.com/Devolutions/now-proto/commit/0adfc78cfa350b3086f6444758d7a5da220c23e8)) 

- [**breaking**] Change incorrect `NowExecRunMsg::directory` method (#33) ([edba71a91e](https://github.com/Devolutions/now-proto/commit/edba71a91ec63735c0aeb3ae839fda3b570d0bc6)) 

## [[0.2.0](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.1.0...now-proto-pdu-v0.2.0)] - 2025-03-14

### <!-- 1 -->Features

- Set keyboard layout functionality (#22) ([31e0c79318](https://github.com/Devolutions/now-proto/commit/31e0c793186d558c0369fe188a2525b99911af30)) 

### <!-- 6 -->Documentation

- Update README.md and uniformize wording (#19) ([17719140e7](https://github.com/Devolutions/now-proto/commit/17719140e7b52b209cda9c17d0ef892cf006f723)) 

