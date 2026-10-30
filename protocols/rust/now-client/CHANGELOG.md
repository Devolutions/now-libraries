# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [[0.1.1](https://github.com/Devolutions/now-libraries/compare/now-client-v0.1.0...now-client-v0.1.1)] - 2026-10-30

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

- Add session lock and logoff requests ([#123](https://github.com/Devolutions/now-libraries/issues/123)) ([41ef2574b3](https://github.com/Devolutions/now-libraries/commit/41ef2574b34560aa2115e0f2eea0c40d4f328739)) 

  Adds `NowClientHandle::session_lock` and
  `NowClientHandle::session_logoff`, which send `NOW_SESSION_LOCK_MSG` and
  `NOW_SESSION_LOGOFF_MSG`. These let an RDP client lock or log off the
  interactive user session that hosts the NOW agent. RDP itself can only
  disconnect, so a client-initiated logoff has to go through NOW.
  
  - **Response:** neither message has a response, so both methods complete
  once the request is written and flushed. A successful logoff ends the
  session that carries the channel, and the handle then returns
  `WorkerClosed`.
  - **Capability check:** each request requires the matching negotiated
  session capability (`NowCapabilities::supports_session_lock` /
  `supports_session_logoff`).
  - The worker checks the capability when it writes the request, so a
  capset refresh from the peer is honored.
  - A request that isn't negotiated fails with
  `NowClientError::UnsupportedCapability` and writes nothing. This matches
  how the exec styles and the .NET client behave.
  - **Default config:** the default `NowClientConfig` capset now
  advertises `LOCK | LOGOFF`. Callers that build their own `client_capset`
  must add these flags themselves.
  
  Tests cover:
  - the exact header-only frames on the wire;
  - the default capset advertisement;
  - a peer omitting a capability;
  - a peer capset refresh withdrawing lock;
  - negotiation requiring both sides;
  - the handle closing after the channel ends.
  
  🤖 Generated with [Claude Code](https://claude.com/claude-code)
  
  ---------


