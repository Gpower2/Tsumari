# Translation retries

Localized bot copies have a gray `↻` button beside the jump links. It retries only that copy, using the current original. `/tsumari retry message:<Discord jump link>` uses the same service and works on old copies without buttons. Original/master copies and missing messages are not recreated. Only the original author, moderators with Manage Messages in the destination channel, administrators, and the optional retry role can retry; source and destination history must be visible to the caller.

At Discord's 25-button limit, a localized copy omits its redundant jump link to itself to make room for Retry. Original and links to all other copies remain available.

The reply is private. On success the existing message is edited, preserving its ID, order, displayed timestamp, attachments, replies, and buttons. Detection or validation failures leave its content unchanged. An uncertain PATCH is reported as uncertain, never blindly repeated. Verification checks delivery, not semantic accuracy.

## Configuration

`Translation:Retry` contains `Temperatures` (default `[0.15,0.25,0.35,0.45,0.55,0.60,0.60,0.60,0.60,0.60]`), `TopP` (0.8), `CooldownSeconds` (30), `QueueCapacity` (8 including the active job), and `RoleId` (0 disables role-based access). The temperature schedule must be nondecreasing, at most ten entries, and bounded by 0.60. The number of entries is the retry limit. Normal translations and language detection retain their existing deterministic settings. Currently Ollama supports the retry profiles; unsupported providers report that capability before consuming an attempt.

One retry runs at a time, with a bounded queue and at most 60 seconds waiting for the worker. Concurrent clicks on the same copy are rejected. Source-edit synchronization and retry commits share a lock; retry re-fetches source/copy via REST before committing and skips concurrent changes. Discord has no conditional PATCH, so external edits in the final check-to-edit interval cannot be made atomic.

## Counters

The bot's own `1️⃣` through `🔟` reactions persist attempts on each copy. Human reaction totals do not count. The highest bot-owned number wins if old-counter removal failed. All ten symbols are reserved from mirroring throughout linked families (including clear/remove reconciliation); this also prevents a human number reaction being mirrored by the bot and mistaken for a retry.

Provider language analysis and local validation happen before recording the attempt. The next reaction is added immediately before generation, then old bot-owned counters are removed. Failed generations still count; automatic transport retries retain the same profile and count. Clearing reactions loses the count. A crash between recording and generation can count an attempt that never ran. This is deliberately best-effort state stored in Discord, with no new DB columns or retry rows. Counters are lifetime attempts for a copy and do not reset when its source is edited.

Exact source echoes with a different detected language are rejected for review; same-language echoes and non-letter content are allowed. Proper-name-only text can still need review if language detection assigns it a different language. No translation-quality guarantee is inferred from a successful HTTP response.

## Deployment and tests

Build/test with an isolated `--artifacts-path` while production is running. This change requires a planned bot restart to activate the new service and register the command. Do not overwrite the production config, token or DB. Historical copies can use the command immediately after deployment; adding buttons to old copies and bulk time-window repair are separate operations.

Unit/component integration tests use temporary SQLite databases and simulated Discord/provider boundaries. HTTP serialization tests exercise the actual Ollama provider and TranslationService concurrently. Tests make no live Discord edits and do not start a bot gateway. A live smoke test belongs in the test channels after deployment.
