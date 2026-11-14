Support for earlier formats is best effort: the current policy format, the current request encoding and their code paths stay clean, and nothing in them exists only to keep an earlier format working.
Each earlier format is read in its own code path, apart from the current reader and evaluator, so that removing it deletes that code without changing them.

<a id="format-1"></a>
## Policies in format 1.x

The broker converts a 1.x policy in memory when it loads it, refuses any policy it cannot convert exactly, and reports the refusal.
Refusing is acceptable even though, after an upgrade, a refused policy leaves the broker refusing every request on that machine until the policy is replaced.
A 1.x policy that uses a pre-release setting is refused, because the current format reads pre-release from the version actually installed rather than from a flag the client sends.
A 1.x priority ordering converts only when exact analysis shows that Deny always winning, with grants adding up, gives every request the same outcome; otherwise the policy is refused.
Editors convert a 1.x file and save it as 2.x.
Reading 1.x policies is deprecated and is removed in a future release; users migrate their policies before then.
The intention is not to author policies for Devolutions Agents that cannot receive pushed policies, because the 2.x format precedes any central authority pushing policies.

<a id="wire-1"></a>
## Earlier request encoding

The broker serves the earlier encoding on its earlier paths and answers with the earlier response, in its own code path, until a future release drops it.
That handling is best effort: it may refuse a request whose conversion is hard, and the code path documents each such choice.
