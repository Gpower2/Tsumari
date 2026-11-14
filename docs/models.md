# Translation backends and model profiles

Tsumari supports DeepL, Ollama and OpenAI-compatible chat completions. Backend selection and model formatting are separate concerns. The evaluated local baseline is **TranslateGemma 12B**, explicitly selected by private runtime settings. Neither alternative below is recommended as a quality replacement on the tested corpus.

## Implemented architecture

```text
ITranslationProvider
├── DeepLTranslationProvider
└── LlmTranslationProviderBase
    ├── OllamaTranslationProvider
    └── OpenAITranslationProvider

LlmModelFactory → LlmModel
                  ├── immutable ModelProfile
                  ├── IModelFormatter (TranslateGemma / Salamandra / TinyAya)
                  └── ILlmClient (Ollama / OpenAICompatible)
```

The factory assembles analysis and translation models when the selected provider is constructed. Shared parsing returns the existing `LanguageAnalysisResult`; model formatters prepare prompts and perform their own translation cleanup. Existing routing, database schema, quota handling and message publication use `ITranslationProvider` without model-name branches. Registry classes and a llama.cpp provider are not implemented.

## Catalogue and qualification

The exact settings and Tiny Aya prompt are in [appsettings.models.json](../src/Tsumari.Bot/appsettings.models.json).

| Profile | Installed model identifier used in the evaluation | Intended tasks | Retry generation options |
| --- | --- | --- | --- |
| `translategemma-12b` | `translategemma:12b` | Analysis, translation | Enabled |
| `salamandra-v3-q4` | `hf.co/BSC-LT/salamandraTA-7B-instruct-GGUF:Q4_K_M` | Translation | Unqualified/disabled |
| `tiny-aya-global-q8` | `hf.co/CohereLabs/tiny-aya-global-GGUF:Q8_0` | Translation | Unqualified/disabled |

The catalogue has no active selections. To explicitly select the factory baseline, add this **inside `Translation`** in the private local override:

```json
"Llm": {
  "AnalysisProfile": "translategemma-12b",
  "TranslationProfile": "translategemma-12b"
}
```

Change only `TranslationProfile` to test an alternative, rebuild and restart through the deployment workflow. Keep analysis on TranslateGemma: Tiny Aya failed analysis qualification; Salamandra is translation-only. Both profile selections must be supplied, and both transports must match the selected provider. Mixing an Ollama analyzer with another transport's translator is not supported by the current factory.

Profiles support `Model`, `Formatter`, `Transport`, full `Endpoint`, optional `ApiKey`, `IntendedTasks`, `Temperature`, optional `TopP`/`TopK`, `ContextLength`, optional `MaxOutputTokens`, `KeepAlive`, `Stops`, `TimeoutSeconds`, `PromptRevision`, optional `TranslationSystemPrompt`, `Languages`, and `SupportsRetry`.

Important boundaries:

- TranslateGemma production prompt overrides are forbidden. Its legacy prompt concatenation, decoding defaults and quote cleanup remain covered by frozen regression fixtures.
- Alternative prompt overrides are inline configuration strings with `{{source}}`/`{{target}}` placeholders; there is no production prompt-file loader.
- Salamandra uses its documented user prompt with source/target labels and requires a concrete trusted source hint. Unknown source fails explicitly. Tiny Aya uses separate system/user instructions.
- Alternatives preserve legitimate surrounding quotation marks. Native control-token collisions are rejected rather than silently rewriting source content.
- Unknown profiles, incompatible task/transport selections, invalid sampling and unsupported OpenAI top-k fail validation. An incomplete/truncated generation fails; empty alternative translations fail explicitly.
- Activation requires restart. There is no hot switching, model fallback, automatic English pivot or language-pair routing policy.
- Startup validates declared profile settings without inference. It does not check Ollama inventory or database-configured channel-language coverage. Model installation and quality qualification are separate checks.

## Language and script requirements

Required languages are German, Greek, English, Italian, Portuguese, Spanish, Serbian and Chinese. Retain locale/script identity (`PT-BR` versus `PT-PT`, Serbian Latin/Cyrillic, Chinese Simplified/Traditional). Existing formatter mappings and optional `Languages` overrides make supported requests explicit; unsupported mappings fail rather than guessing.

Published coverage alone is not reliable translation/script control. The November 2026 tests included both Serbian inputs/scripts and Chinese script probes, but not every one of the 56 directed language pairs. Chinese/Serbian mandatory output-script requirements still need explicit acceptance criteria for a future replacement.

## Evaluation findings and sources

[Model comparison](model-evaluation/EVALUATION_REPORT.md) records 42 requests per model, raw responses, GPU/timing evidence and independent assistant judgments. [Prompt experiments](model-evaluation/translategemma-prompt-experiments/REPORT.md) record 232 requests: no tested TranslateGemma revision was a clear replacement for the existing prompt. These are dated, limited local results, not general accuracy guarantees or current process-state claims.

Salamandra's publisher [warns against Ollama compatibility](https://huggingface.co/BSC-LT/salamandraTA-7b-instruct) and recommends llama.cpp for GGUF; a runtime improvement remains untested here. llama.cpp is on hold, not proven useless or implemented. Tiny Aya Global is instruction-tuned; Base is not the direct replacement candidate. The cards list GPL-3 for Salamandra and CC-BY-NC-4.0 for [Tiny Aya Global](https://huggingface.co/CohereLabs/tiny-aya-global); review applicable terms before deployment/redistribution.

Earlier limited-comparison downloads (recorded in [installation metadata](model-evaluation/ollama-installed-models-2026-11-11.json)) include PLaMo 3 Translate and MiLMMT-46. Their documented language sets did not meet the full requirement: PLaMo lacked listed Greek/Portuguese coverage; MiLMMT lacked Serbian. North Small Translate was outside this machine's practical local memory budget. These are historical screening findings, not implemented universal fallback choices.

Use the [evaluator guide](../tools/Tsumari.Evaluation/README.md) for isolated experiments. Separate servers still share physical GPU capacity; two task models can compete for VRAM or cause reloads. Benchmark real message latency and qualify retries before adopting a replacement.
