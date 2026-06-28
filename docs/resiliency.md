# Resiliency Helper

Tsumari wraps translation and language-detection calls in a custom `ResiliencyHelper` instead of using an external resilience library.

This helper provides:

- retry with exponential backoff and jitter
- a circuit breaker with `Closed`, `Open`, and `HalfOpen` states

## Current Runtime Settings

`TranslationService` creates the helper with:

- failure threshold: `3`
- break duration: `30s`
- max retry attempts: `3`
- initial retry delay: `1s`

The same helper is used for:

- `DetectLanguageAsync(...)`
- `TranslateTextAsync(...)`

## Circuit Breaker States

```text
            +--------- success ---------+
            |                           |
            v                           |
      +-----------+   failures >= 3   +----------+
  --->|  Closed   |------------------>|   Open   |
      +-----------+                   +----------+
            ^                              |
            |                        cooldown 30s
         success                           |
            |                              v
      +-----------+                   +----------+
      | HalfOpen  |<-- next request --| HalfOpen |
      +-----------+                   +----------+
            |                              |
            +----------- failure ----------+
```

### Closed

Normal operation. Requests are allowed through. Success resets the failure count.

### Open

The helper fast-fails requests without calling the underlying provider once the failure threshold is reached.

### HalfOpen

After the cooldown window expires, the next request is allowed through as a test:

- success closes the circuit again
- failure reopens it immediately

## Retry Strategy

The retry delay uses:

```text
delay = initialDelay * 2^(attempt - 1) * jitter
```

Current details:

- base delay starts at `1s`
- jitter is a random value in `[0.5, 1.5)`
- delay is capped at `30s`

## Provider Scope

The resilience behavior applies to the **selected translation backend**, not just DeepL.

That means:

- DeepL detection/translation calls are wrapped
- Ollama detection/translation calls are wrapped
- OpenAI-compatible detection/translation calls are wrapped

## Relationship to Quota Checks

Quota enforcement is separate from resiliency:

- when `Translation.Provider = DeepL`, Tsumari checks the monthly character limit before calling the provider
- when `Translation.Provider = Ollama` or `OpenAI`, `CanTranslateAsync(...)` always returns `true`

So the circuit breaker protects backend availability, while `UsageTracker` protects DeepL billing.

## Gateway Isolation for Slow Local LLMs

Translation resiliency is only one part of runtime stability. Tsumari also isolates Discord gateway callbacks from slow local/self-hosted LLM calls by enqueueing gateway events into a dispatcher:

- gateway callbacks enqueue and return immediately
- a single router forwards events into FIFO queues keyed by linked channel group
- one worker processes each active linked group sequentially

That means:

- a slow translation in one linked channel cluster does not block unrelated clusters
- ordering inside a linked channel cluster is still preserved
- Discord.Net no longer needs to wait on the full translation pipeline inside `MessageReceived`
