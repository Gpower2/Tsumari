# Tsumari local model evaluation — 14 November 2026

**Recommendation: retain TranslateGemma 12B for analysis and translation.** The two alternatives are configured and executable, but neither met the existing fidelity requirements on this corpus. The evaluation did not switch models or restart the bot; subsequent deployments are independent of this dated measurement.

## Final comparison

Final runs used the shared production factory, formatters and clients on an isolated Ollama server, with pooled HTTP connections. All three models received the same 42 translation requests with curated source hints where known. The ambiguous artificial profanity list retained an unknown source. Warmups are excluded; durations are successful request means and p95, not complete Discord-message processing times.

| Installed model | Blind review: pass / minor / major / unavailable | Mean / p95 seconds | Model VRAM GiB | Peak whole GPU MiB | Mean sampled whole GPU utilization |
| --- | --- | --- | --- | --- | --- |
| TranslateGemma 12B Q4_K_M | 28 / 6 / 8 / 0 | 0.854 / 1.173 | 7.28 | 14,286 | 85.7% |
| SalamandraTA 7B v3 Q4_K_M | 14 / 15 / 12 / 1 | 0.394 / 0.627 | 4.33 | 10,324 | 83.6% |
| Tiny Aya Global Q8_0, few-shot v3 | 16 / 15 / 11 / 0 | 0.370 / 0.517 | 3.53 | 9,556 | 77.6% |

Model VRAM is Ollama's `size_vram` residency report. Peak memory and sampled utilization are whole-device observations including other processes; they are not isolated per-model utilization. Short requests have few samples. Measurements are a single local snapshot, not controlled repeated hardware benchmarks. The same physical GPU was shared, although server requests/residency were separated from production.

The full **per-request table, every output, timing and GPU sample** is in [FINAL_PERFORMANCE.md](results-2026-11-14/FINAL_PERFORMANCE.md) and [raw final evidence](results-2026-11-14/final-pooled-v3.jsonl). Raw responses include Ollama processing times, which closely match the final pooled client times (means 0.849 / 0.391 / 0.366 seconds respectively). Digests and effective request settings are captured with the evidence.

## Quality assessment and limits

An independent assistant reviewed anonymized outputs without model identities. These are judgments against curated semantic requirements, **not human-reviewed gold translations, expert validation or general accuracy percentages**. Register can vary by region/context; Serbian naturalness especially warrants native-speaker review. The recovered examples partly overlap TranslateGemma's existing prompt examples. Added synthetic probes extend coverage, but all 56 directed language pairs have not been tested.

Blind evidence mapping:

- [A](results-2026-11-14/BLIND_REVIEW_A.json): TranslateGemma, unchanged prompts.
- [B](results-2026-11-14/BLIND_REVIEW_B.json): Salamandra, native prompt.
- [C](results-2026-11-14/BLIND_REVIEW_C.json): Tiny Aya v2: 14 pass, 13 minor, 15 major.
- [D](results-2026-11-14/BLIND_REVIEW_D.json): Tiny Aya few-shot v3, selected above.

The pooled replay produced byte-identical outputs for every TranslateGemma and Salamandra request from the previous reviewed pass, so A/B judgments apply directly. Tiny Aya's final replay was independently reviewed as D. Case order in each blind file matches that model's non-warmup final rows.

TranslateGemma still made mistakes, including profanity distortions, some invented insults, untranslated content and train/bus or Serbian-form errors. The recommendation is relative to this evaluated set, not a claim that the baseline is flawless.

Salamandra's failures included dropped second lines and emojis, softened profanity, untranslated mixed-language material, and simplified output for an explicit Traditional Chinese target. One request with no trusted source failed explicitly by design; this is a coverage limitation, not an invented model response. Its publisher [strongly discourages Ollama](https://huggingface.co/BSC-LT/salamandraTA-7b-instruct) because of compatibility problems, recommending vLLM/full weights or llama.cpp/GGUF. These results concern the installed Ollama path and do not establish the model's quality in another runtime.

Tiny Aya's final few-shot prompt improved censorship/wrapping and some script behavior versus v2, but still produced untranslated messages, changed subjects/names, softened or redirected profanity, and confused train/bus/carriage. Its lower VRAM and roughly 2.3x lower mean request time do not compensate for these failures in the current use case.

## Prompt refinement

TranslateGemma's prompt text, source-hint rules, cleanup and default decoding were preserved. Frozen complete prompt snapshots and legacy-payload tests verify compatibility. Baseline requests used temperature 0, top-p 0.1, context 4096, no new output cap.

Salamandra progressed from an initial split system/user request to its documented native user completion with source/target labels. That restored omitted opening text and improved `Si Tasos`. A focused preservation/disambiguation v3 experiment did not clearly improve omissions or fidelity, so the native prompt remains selected.

Tiny Aya progressed from generic translation instructions to whole-message/register instructions, then two short register examples that did not copy the curated inputs. The final few-shot v3 was fully replayed and blind-reviewed before selection. These are the best observed settings among the tested revisions, not a claim of globally optimal prompting. Revisions and their confounds are documented in [prompt-revisions](prompt-revisions/README.md).

Alternatives use temperature 0, top-p 1, context 4096 and output cap 1024. Initial-to-final comparisons changed several settings together, so individual causal gains cannot be isolated. The shipped profile contains the exact selected Tiny Aya prompt. Neither alternative currently declares qualified generation-retry support.

## Language analysis

[Analysis evidence](results-2026-11-14/ANALYSIS_PERFORMANCE.md) covers 27 unique texts per model. Twenty-six have a prescribed dominant language; the artificial mixed profanity case does not.

| Model | Strict expected primary-code matches | Mean analysis seconds | Status |
| --- | --- | --- | --- |
| TranslateGemma 12B | 26 / 26 | 1.79 | Retain as analyzer |
| Tiny Aya Global | 20 / 26 | 0.87 | Not qualified; shipped profile is translation-only |
| SalamandraTA | Not attempted | — | Translation-only |

One Tiny Aya mismatch is `PT-BR` versus expected `PT`, a regional-label distinction rather than a wrong language. Other errors include English/German/Portuguese as Italian, Chinese as nonstandard `CN`, and Serbian as Czech. Its 27/27 successful parsed responses do not establish correct analysis. The Tiny Aya translation prompt revision does not change its analysis formatter.

## Operational context and maintained guidance

- Existing live configuration was copied into ignored `.local/appsettings.secrets.json`; build and publish inject it automatically. [Configuration guide](../configuration.md).
- The tracked model catalogue contains all three profiles and no active selections, keeping the original provider path as default. [Model catalogue](../../src/Tsumari.Bot/appsettings.models.json).
- A separate `Tsumari.Evaluation` project reuses production implementation and never starts the Discord host.
- Baseline and alternatives were tested without stopping, restarting or replacing the live bot. No Discord/database writes were performed by evaluation.
- Superseded initial/unpooled timing tables were removed during documentation consolidation. Final pooled raw responses/timings are retained; the earlier fresh-handler overhead is not part of the headline measurements.
- Model/profile compatibility is covered by frozen-prompt, client, configuration and deployment tests. Use the maintained [model guide](../models.md) and [deployment guide](../DEPLOYMENT.md); completed implementation audit narratives are not current operating instructions.

Keep both production tasks on TranslateGemma. Use the new profiles for further experiments; qualify retries, broader language pairs, long messages, Serbian/Chinese script behavior and native-speaker fidelity before promoting an alternative.
