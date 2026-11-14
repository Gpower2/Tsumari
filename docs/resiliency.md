# Translation resiliency

`TranslationService` owns **two separate** `ResiliencyHelper` instances: one for `AnalyzeLanguageAsync` and one for translation (`TranslateTextAsync` and generation-option retries). Translation failure does not directly open the analysis circuit, and vice versa. Each helper applies to the selected backend, including DeepL, Ollama and OpenAI-compatible providers.

## Settings and state

Both helpers use failure threshold 3, break duration 30 seconds, at most 3 total action attempts per invocation, and initial retry delay 1 second. Circuit state can stop retries sooner.

- **Closed:** requests run; success resets failures.
- **Open:** requests fail before calling the provider.
- After the 30-second break, a request can transition the circuit to **HalfOpen**. Success closes it; failure reopens it. This is not a guarantee of exactly one exclusive concurrent probe.

Backoff is `initialDelay * 2^(attempt - 1) * jitter`, jitter in `[0.5, 1.5)`, capped at 30 seconds. The helper retries provider operations; it does not decide whether a completed translation is linguistically accurate.

## Quota and manual generation retries

Quota checks are separate. DeepL alone uses the monthly 500,000-character guard. Successful analysis and translation can each increment its usage counter. Local/self-hosted providers do not consume that application quota.

The gray Retry button and `/tsumari retry` are a different mechanism: they change a qualified model's sampling settings to generate a replacement for one existing copy. Transport retries retain the same settings and do not consume additional displayed generation attempts. An uncertain Discord PATCH is not blindly repeated. See [translation retries](translation-retries.md).

## Gateway and Discord REST isolation

Gateway message/edit/delete/reaction callbacks enqueue work and return. The dispatcher routes it into FIFO queues keyed by linked channel group; each group is processed sequentially while unrelated groups can run in parallel. This isolates gateway handling, not the shared backend's physical GPU capacity or its per-task circuit state.

Discord REST operations use their separate bounded timeout/retry policy and named HTTP clients. Defaults are documented in [configuration](configuration.md); routing order and synchronization are in [routing](routing.md).
