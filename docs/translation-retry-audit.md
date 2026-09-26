# Retry implementation validation — 26 September 2026

Two independent audit cycles completed; stopped after cycle two reported no new actionable findings (requested maximum: five).

## Cycle one

Independent read-only review found:

1. P1: direct Discord PATCH inherited automatic timeout/502 retries. Fixed by explicitly passing the existing bounded request options, allowing only Discord rate-limit handling. Regression test asserts the request timeout/retry mode and one PATCH invocation on an uncertain response.
2. P2: interface serialization omitted nested button fields from snapshots. Fixed with explicit recursive row/button snapshots. Tests detect changes to a link within the same row and skip commits on changed buttons.
3. P2: adding a retry button to 25 existing buttons exceeded Discord capacity. Fixed by replacing only the copy's redundant self-link at that boundary. Test covers 24 localized copies with Original and all other destination links preserved.

Also added an integration test coordinating the actual source-edit service and retry service through their shared mutation coordinator, ensuring the latest source translation survives.

## Cycle two

A fresh reviewer independently inspected the feature and prior fixes, then ran 42 focused tests in a separate build directory. All passed. No new actionable findings.

## Final validation

- Bot unit/component integration suite: **333 passed, 0 failed** (34 additional cases relative to the 299-case baseline).
- Standalone repair utility suite: **22 passed, 0 failed**.
- Isolated Release build: **succeeded**.
- Whitespace check preserves each existing file's line-ending convention.
- Build output continues to report the pre-existing NU1903 advisory for SQLitePCLRaw.lib.e_sqlite3 2.1.11; dependency remediation is outside this feature change.

Tests use temporary SQLite databases, simulated Discord boundaries, and a simulated HTTP endpoint with the actual Ollama provider. They do not verify live Discord delivery or semantic translation quality. Discord cannot atomically combine a conditional source check and destination PATCH; the remaining final-check race is documented in the feature guide.

Production bot PID 47248 remained running with its September 21 start time. No production DB mutations, live Discord edits, or bot restart were performed. The new code requires deployment and a planned restart to become active. See [the feature guide](translation-retries.md) for configuration and operating limits.
