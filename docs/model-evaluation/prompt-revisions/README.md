# Alternative prompt revisions (November 2026 experiment)

These files are **evaluation inputs**, not a production prompt-file loading mechanism. The shipped catalogue uses an inline Tiny Aya prompt; changing an experiment file does not change the bot.

- Salamandra's final selected formatter uses the publisher-native user request with explicit source/target labels. The focused [v3 preservation experiment](salamandra-v3.json) did not clearly improve omissions/fidelity and was not selected.
- [Tiny Aya v2](tiny-aya-v2.json) adds whole-message, register and omitted-accent disambiguation instructions. Its 42-request blind review yielded 14 clean / 13 minor / 15 major judgments.
- [Tiny Aya few-shot v3](tiny-aya-v3.json) adds two short register examples distinct from the recovered inputs. The fully replayed review marginally preferred it (16 clean / 15 minor / 11 major); the exact string is in the shipped profile. Important failures remain, so this is an experimental translation option, not an approved replacement or analyzer.

Final alternatives used temperature 0, top-p 1, context 4096 and output limit 1024. Earlier runs changed template, mapping and decoding together, so no single-factor causal claim is supported. A previous unpooled evaluator added connection overhead; only the final pooled timings are the headline measurements. Recovered source hints are oracle inputs, not proof of analysis reliability.

The [Salamandra card](https://huggingface.co/BSC-LT/salamandraTA-7b-instruct) warns of Ollama compatibility problems and recommends other serving runtimes. The recorded results apply to this Ollama experiment. [Full report](../EVALUATION_REPORT.md). [Reproduction commands](../../../tools/Tsumari.Evaluation/README.md).
