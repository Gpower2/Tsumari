# Recovered translation regression corpus

`cases.json` is the input used by the .NET evaluator: **17 distinct inputs**, including five user-reported real examples and twelve historical probes. [CASES.md](CASES.md) is their readable catalogue. Per-case source hints, targets, semantic checks and event-line provenance remain in the JSON.

Collected 11 November 2026 from Copilot session **Fix Translation Button Logic**, ID `56123b7e-2b3a-4bd3-97e7-063bf46de215`, covering prompt tuning on 29–30 June 2026 UTC. The original session belongs to the user's local Copilot session store, outside this repository; the collector and large duplicate checkpoint/Markdown/source-code exports are not required to run evaluation.

## Retained evidence

| File | Role |
| --- | --- |
| [cases.json](cases.json) | Curated reusable inputs, source hints, targets and expectations |
| [CASES.md](CASES.md) | Readable input catalogue |
| [historical-observations.json](historical-observations.json) | Forty earlier model observations, distinguished by tuning stage |
| [historical-live-probes.json](historical-live-probes.json) | Thirty-two recovered commands/results; inert historical text, never automatically executed |
| [session-excerpts.json](session-excerpts.json) | Selected user requirements and session events with provenance |
| [manifest.json](manifest.json) | Collection identity and checksums for retained original probe artifacts |
| `original-artifacts/` | Two unmodified saved Copilot probe/coverage-audit JSON artifacts |

No human-reviewed gold translations were found. Historical outputs may be wrong: an earlier coverage audit still labels Tasos mostly Greek, and some Greek profanity was softened. Those are failures to test, not desired outputs. The artificial mixed profanity list has no prescribed dominant language; do not turn an old PT hint into trusted ground truth.

Core regressions include `fratello`, `Ma cherie`, affirmative Italian `Si Tasos`, false secondary languages on monolingual English/Greek, and profanity function/strength. Source hints should be distinguished from analysis-derived hints. Many inputs overlap the existing analysis-prompt examples, so add genuinely new validation data before claiming general improvement.

The current 85%/15% secondary-language collapse is application policy described in [models and configuration](../../models.md), not a requirement to reproduce historical raw percentages. The production compatibility tests keep their own frozen legacy prompt fixture under the test project; they do not depend on removed source snapshots or copied test-method excerpts.

[Run evaluation](../../../tools/Tsumari.Evaluation/README.md). [Dated model results](../EVALUATION_REPORT.md).
