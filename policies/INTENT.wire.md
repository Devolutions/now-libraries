Requests and responses on the named pipe use the vocabulary of `policies/INTENT.md`, so that a request reads like the policy that decides it.

<a id="opt-ins"></a>
## Opt-ins

A request lists the opt-ins it asks for under `OptIns`, keyed exactly like a policy's `Grants` and `Forbids`, and a plain request has none.
An opt-in is asked for when its key is present, so a request never spells out the opt-ins it does not use.
`OptIns` is the only way to ask for an opt-in: installer arguments have their own key, and a custom parameter that would carry another opt-in makes the request unreadable, so that every opt-in a request uses is visible in `OptIns`.
A denial names an opt-in by the key the request used.

<a id="targeting"></a>
## Scope and elevation

Scope and elevation are request fields side by side, outside the opt-ins, because elevation is a targeting choice.
The elevation values are `NonElevated` and `Elevated`, and a user-scope request may run elevated.
A request whose `Scope` is `Machine` and that asks to run non-elevated contradicts itself and is refused as a bad request rather than silently elevated.
A request that installs, updates or uninstalls machine-wide without saying so in `Scope`, through its manager's default or a custom parameter, and asks to run non-elevated is unreadable, so that the wire needs no knowledge of managers.

<a id="fields"></a>
## Fields

A request carries no field without an effect on what the broker runs, so that it stays close to what will actually happen: no pre-release flag, because pre-release comes from the version; no channel; and no source URL, because a source is identified by its name.

<a id="replacement"></a>
## Policy replacement

A policy that replaces the active policy with the same `Id` has a higher `Revision`, so that a stale push cannot silently undo a newer policy.
A policy with another `Id` may replace it.

<a id="encodings"></a>
## Encodings

Every endpoint has a 2.0 path under `/v2/`, and the path alone selects the encoding, so the routes of the earlier encoding stay a separate block the broker can drop.
A 2.0 request or response carries no version or kind field, because the path already says both.
A response uses the encoding of the request it answers, so a client that speaks 2.0 holds no code for the earlier encoding.
A 2.0 client does not work with a broker that knows only the earlier encoding, and that is accepted for this migration.
