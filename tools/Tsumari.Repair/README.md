# Tsumari.Repair

Standalone, REST-only repair of **existing, database-linked bot messages** carrying
the generated `*(Translation Failed)*` marker. It never posts or deletes Discord
messages, starts a gateway connection, starts the bot, migrates SQLite, or writes
to the bot's database. Its build does not reference/build the bot project: it
compiles selected provider helpers into its own executable.

## Build and test

```powershell
dotnet build tools/Tsumari.Repair/Tsumari.Repair.csproj -c Release --artifacts-path .artifacts/repair-build
dotnet test tests/Tsumari.Repair.Tests/Tsumari.Repair.Tests.csproj -c Release --artifacts-path .artifacts/repair-tests
```

## Configuration and private files

Pass an existing `appsettings.json` explicitly with `--config`. It must contain
`Discord:Token` and `Translation:Provider=Ollama` plus the existing Ollama settings.
`TSUMARI_REPAIR_DISCORD_TOKEN` optionally overrides the Discord token. The utility
does not load the bot's user-secrets or development configuration automatically.
Tokens are never written into plans or command lines.

Keep plans under `.repair-runs/` (gitignored). Plans and append-only journals
contain private message text, original metadata, prepared translations, jump links
and verification snapshots. They are not appropriate for source control.

## Workflow

The examples use repository-relative executable paths; config, database and plan
paths may be absolute. Use the same plan path to resume an interrupted command.
Do not run separate apply processes against overlapping plans.

```powershell
dotnet .artifacts/repair-build/bin/Tsumari.Repair/release/Tsumari.Repair.dll scan --config src/Tsumari.Bot/bin/Release/net10.0/appsettings.json --db src/Tsumari.Bot/bin/Release/net10.0/tsumari.db --plan .repair-runs/production/plan.json --timezone Europe/Athens
dotnet .artifacts/repair-build/bin/Tsumari.Repair/release/Tsumari.Repair.dll prepare --config src/Tsumari.Bot/bin/Release/net10.0/appsettings.json --plan .repair-runs/production/plan.json --limit 5
dotnet .artifacts/repair-build/bin/Tsumari.Repair/release/Tsumari.Repair.dll apply --config src/Tsumari.Bot/bin/Release/net10.0/appsettings.json --plan .repair-runs/production/plan.json --limit 5
dotnet .artifacts/repair-build/bin/Tsumari.Repair/release/Tsumari.Repair.dll verify --config src/Tsumari.Bot/bin/Release/net10.0/appsettings.json --plan .repair-runs/production/plan.json
```

- `scan` is read-only on Discord and SQLite. By default it fixes its time bounds
  at the earliest/latest mirrored message IDs in the database snapshot. Optional
  `--from "2026-09-21 00:00" --until "2026-09-22 00:00"` selects a local-date window.
  Start is inclusive; end is exclusive. Selection uses **mirror creation time**,
  not original-message time, elapsed translation time, or last edit time. Local
  boundaries respect DST; ambiguous/nonexistent local times are rejected.
- Scanning paginates history, checking only stored mirror IDs and bot authorship.
  It requires the exact original text and known generated wrapper; ambiguous
  markers, missing originals, changed sources and unknown attachment notices are
  reported as issues and never automatically repaired. A deleted mirror is never
  recreated. Missing original-channel IDs can be resolved from the Original button.
- `prepare` calls the existing Ollama translation provider, using its normal
  translation prompt without a source-language hint unless a reviewed hint was supplied. It preserves the existing
  author/header, displayed timestamp and attachment warning. It makes no Discord
  edits, rejects empty/oversized output, and pauses at the first provider error.
- `apply` only processes prepared records. It fetches both original and mirror
  again and skips concurrent changes. Each PATCH changes only `content` plus
  `allowed_mentions` (notifications disabled). Existing IDs, timestamps, replies,
  attachments and components are checked with a fresh GET after every edit.
  It writes a journal entry before PATCH and after verification. An uncertain edit
  remains `applying`; rerunning checks whether the exact prepared content is live
  before considering another PATCH. No blind retries of failed PATCH requests.
- `verify` independently re-fetches all applied messages and checks their content
  and preserved metadata; a discrepancy produces a nonzero exit code.
- `prepare` and `apply` use one operation at a time and a 3-second pause by default;
  `--delay-seconds 5` can increase pacing. Discord requests are separately paced
  and respect 429 retry delays. Both services still share capacity with the bot.
- Plan writes are atomic. A file lock prevents simultaneous use of the same plan.
  Ctrl+C stops at a cancellable boundary; completed work is retained. To scan a
  different window use a new plan path.

## Limits while the bot stays running

The live bot can still edit a message between our final check and PATCH; Discord
does not offer compare-and-swap edits. A fixed historical window and fresh checks
reduce this race but cannot eliminate it. Preparation preserves the original
snapshot and application skips changed messages. Provider success is not proof of
linguistic accuracy; inspect initial prepared examples before a bulk apply.

This utility does not manage retry buttons or reaction counters. The bot implements those controls; repair PATCHes leave reactions/components in place and do not reset lifetime retry counts. Repair plans/journals are separate from those counts. Avoid overlapping repair applies and live retries: this utility does not share the bot's in-process mutation lock. SQL-only discovery cannot identify unmarked translation failures.
Unlinked or missing messages remain outside scope; explicitly reviewed unmarked
copies can be handled with the workflow below.

## Reviewed unmarked copies

The bot's English routing/display fallback on failed analysis can cause non-English content to be copied raw. That fallback is withheld as a trusted translation hint; it does not eliminate incorrect routing. Earlier failures can therefore leave non-English
text in the English channel without adding a failure marker. The regular scan
cannot identify these. A successful HTTP edit also does not prove the provider
actually translated the text.

After reviewing specific existing copies and their real source languages, create
a JSON manifest such as:

```json
[
  { "MirrorId": "1551478394439598231", "SourceLanguageCode": "it" }
]
```

Use `review-copies --config PATH --db PATH --plan NEW_PLAN_PATH --review MANIFEST_PATH`.
This is a read-only import: each ID must have a database link, be owned by the bot,
and contain exactly the current original text in a recognized wrapper. The source
language must differ from the stored destination language. Raw master copies are
excluded. The manifest is an explicit reviewed selection, not automatic language
classification of every unchanged message (names, emoji and same-language copies
may legitimately be unchanged).

Then use the normal `prepare`, `apply`, and `verify` commands. Preparation passes
the reviewed source-language hint to the provider. Unchanged output containing
letters now pauses preparation for review unless source and target are confirmed
to match. This check also applies to ordinary marked-failure plans; it prevents
removing a failure marker while silently retaining untranslated source text.

If the provider cannot handle a phrase, a reviewer can set `ReviewedTranslation`
on its still-`discovered` candidate in the local plan and rerun `prepare`. This
uses the reviewed text instead of another provider call, applies the same wrapper,
length and unchanged-text checks, and records `TranslationOrigin: "reviewed"`.
Original snapshots and the live pre-edit checks still apply.

Review the prepared text itself before application. The `verify` command checks
exact delivery and metadata preservation, not semantic translation quality.

The utility reuses the configured Ollama provider helpers, including explicit named profiles when selected. It does not run language analysis before preparing ordinary marked-failure records. A source-required profile such as Salamandra can therefore reject an unhinted request. Keep the evaluated baseline or supply explicitly reviewed hints; do not guess a source to force preparation. See [model profiles](../../docs/models.md) and [runtime configuration](../../docs/configuration.md).
