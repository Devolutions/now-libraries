A policy decides which package installs, updates and uninstalls the broker runs for users, often with administrator rights.
It lets administrators hand routine software management to users without letting them install arbitrary software, such as malware from the internet.

<a id="vocabulary"></a>
## Vocabulary

<a id="opt-in"></a>
### Opt-in

An opt-in is something a request asks for beyond a plain request: interactive, skip hash check, custom install location, custom parameters, installer arguments, pre/post commands, kill before operation, uninstall previous, or no upgrade.
WinGet `--override` and `--custom` are their own opt-in, installer arguments, so a custom-parameter pattern never grants them.
*Avoid:* option, modifier, characteristic, flag, extra.

<a id="plain-request"></a>
### Plain request

A plain request uses no opt-ins.

<a id="selection"></a>
### Selection

A rule picks the requests it applies to with `Select`, narrowed by `Except`.
Editors label the selection "Applies to".

<a id="grants"></a>
### Grants and Forbids

An Allow rule `Grants` opt-ins, and a Deny rule `Forbids` them.
Every grant is an object, even an on/off one, so later fields fit.

<a id="elevation"></a>
### Elevation

The elevation values are `NonElevated` and `Elevated`, and editors show them as "Runs as administrator: Yes/No".
Elevation is a targeting choice, not an opt-in, and the format is neutral about it.

<a id="source"></a>
### Source

A source is where a package manager gets packages from, identified by the manager and the source's name, such as WinGet `winget`, PowerShell `PSGallery` or a Scoop bucket.
Which sources a policy accepts is set by its trusted sources.
*Avoid:* repository, feed, registry, bucket, except when quoting a specific manager.

<a id="ownership"></a>
## Ownership of the meaning

The policy specification kept here owns the meaning of a policy: the broker implements it, and editors rely on it.
It is implemented twice: a Rust crate that the broker depends on, and a C# library that editors use.
The C# implementation exists because policies are also authored away from any Devolutions Agent, for a central authority to push, and it gives RDM and UniGetUI the same evaluation and warnings.
A second implementation, rather than the Rust crate shipped to .NET, avoids shipping native binaries for every operating system and architecture, and keeping two implementations in step is practical when language models write much of the code.
For every policy and request, the Rust and C# implementations reach the same decision and name the same rules and opt-ins.
The evaluator reads raw requests, including what each package manager does with them.
A request is read the way the broker will execute it, so a change to how the broker builds commands is a change to the specification.
An option the client sends is the exception: it always counts as asked for, even when the broker drops it.
A decision depends only on the policy and the request, never on what a given broker version does with the options, so that decisions stay predictable and stable.

<a id="rules"></a>
## Rules

An Allow rule permits only a plain request, and every other opt-in is permitted only when granted by name, so that a policy is secure by default.
The default decision is always Deny; a deny-list policy is written as an explicit catch-all Allow rule with its grants.
Deny always wins, rule order is irrelevant, and an exception is written on the rule it narrows.
Numeric priority is rejected because grants adding up across rules leave no one-sentence rule for it, and first match is rejected because a policy's meaning would depend on rule order.
A broad Allow rule can therefore be overridden by a narrower Deny rule.
An exception only narrows the rule it is written in, whether on the rule's selection or inside a grant; a ban is a Deny rule and always wins.
When a property of the request that an exception tests is unknown, the exception keeps the rule fail-closed: on an Allow rule the request counts as excepted, and on a Deny rule as not excepted.
Grants from all applicable Allow rules add up, so a broad rule can grant some opt-ins to many packages while a narrower rule grants more to a few.
An Allow rule cannot require an opt-in.
The format must not preclude policy-applied opt-ins, where the broker applies a value on the policy's behalf, which may be added later.
Pre-release is part of choosing the version: one setting, checked against the version actually installed rather than against a flag the client sends.
A version is a pre-release only when its manager's version scheme marks it as one, and WinGet's scheme has none.

<a id="trusted-sources"></a>
## Trusted sources

A policy lists its trusted sources.
An install or update from a source the list does not name is denied, whatever the rules say.
For installs and updates, a rule can only narrow which trusted sources it applies to.
Without the list, a rule that names no source applies to every source the manager can reach, including a Scoop manifest URL, and a forgotten source must block requests rather than let them through.
The list is required.
A policy that does not restrict a manager's sources says so with an explicit entry trusting any source of that manager, so that trusting everything is a visible choice rather than an omission, and a 1.x policy converts exactly.
A manager missing from the list therefore cannot install or update anything, so the list also says which managers the policy uses.
The list governs installs and updates only, because uninstalls download nothing; rules alone govern uninstalls.
A rule may therefore name a source the list does not trust, to govern uninstalls of packages from that source.
Audit mode can let an untrusted source through, so that a new list can be tried out without blocking users; unlike the custom install location check, the list is ordinary policy content.
A rule names its sources as manager-and-source pairs, so one rule can span managers.
Rules that may name a source only when they select exactly one manager are rejected, because trust and ban lists would need one rule per manager.
Named source lists are rejected, because they add indirection that the trusted-source list makes unnecessary.

<a id="decisions"></a>
## Decisions

Every decision names what produced it.
A denial names the opt-in that no applicable rule grants.
Adding an Allow rule never denies a request that was allowed, and adding a Deny rule never allows a request that was denied.

<a id="fail-closed"></a>
## Fail-closed checks

Outside the validity dates, every request is denied, with no fallback.
A custom install location that is not one plain local path is refused before any rule runs, so audit mode cannot let it through.
Install and update requests carry a concrete version, so that the broker has the information rule matching needs.
A request without one is rejected before any rule runs, except through package managers for which the broker cannot pin a version.
For those managers the version is unknown: a Deny rule's version condition matches, and an Allow rule's does not.
When scope or architecture is unknown, a Deny rule's selection matches and an Allow rule's does not.
Uninstalls carry no version a policy can check, so a selection that tests the version applies to installs and updates only, and says so by listing its operations.
Rules that silently never match uninstalls, and versioned Allow rules that implicitly allow uninstalls, are rejected.

<a id="broker-limits"></a>
## Broker limits

The broker never runs pre/post commands for a request that runs elevated, whatever the policy grants and even in audit mode, because such a command is an arbitrary command run as administrator.
An allowed request that asks for one runs nothing, and that refusal is not a decision.

<a id="warnings"></a>
## Warnings

Risk levels are non-normative guidance in the specification, implemented in the shared C# library so that RDM and UniGetUI warn alike.
