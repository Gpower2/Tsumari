# Model and prompt evaluation

This folder contains **dated local measurements and reusable evidence**, not live deployment configuration. Current backend/profile setup is documented in [models](../models.md), private settings in [configuration](../configuration.md), and operation in [deployment](../DEPLOYMENT.md).

- [November 2026 model comparison](EVALUATION_REPORT.md): TranslateGemma, SalamandraTA and Tiny Aya, final outputs/timings and analysis results.
- [TranslateGemma prompt comparison](translategemma-prompt-experiments/REPORT.md): unchanged baseline versus three experimental revisions; no universal prompt upgrade established.
- [Recovered regression corpus](reference-data/README.md): curated cases and historical provenance; observations are not human gold.
- [Prompt revision lineage](prompt-revisions/README.md): the tested alternative prompts and selected configuration.
- [Installation snapshot](ollama-installed-models-2026-11-11.json): recorded model identifiers, digests and templates as checked on that date; not a claim about every future machine's installed models.
- [Evaluator instructions](../../tools/Tsumari.Evaluation/README.md): isolated builds, running the tests and regenerating reports.

The retained result Markdown/JSONL files are immutable benchmark observations. Their GPU utilization includes concurrent device workloads. Their response text may contain profanity from the test corpus. Blind review is independent assistant judgment rather than expert/native-speaker validation. Model listings, successful HTTP responses and formatting checks are not quality guarantees.

Superseded integration plans, seven-language screening notes, completed implementation audits, duplicate transcripts/source snapshots and invalid initial timing tables have been removed. Their useful configuration/architecture/qualification guidance is consolidated in the maintained guides above. Final evidence, source cases, selected previous-prompt comparisons and original small historical probe artifacts remain available for reproducibility.
