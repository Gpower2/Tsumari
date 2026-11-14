# Model and prompt evaluator

This console project references the bot's model factory, formatters and endpoint clients. It never starts Discord, reads private bot settings, writes the production database or changes production selection. Python helpers use only the standard library; .NET requires the same SDK and PowerShell build merge as the bot.

## Build and model comparison

From the repository root:

```powershell
dotnet build tools/Tsumari.Evaluation/Tsumari.Evaluation.csproj --artifacts-path .artifacts/eval-build
dotnet .artifacts/eval-build/bin/Tsumari.Evaluation/debug/Tsumari.Evaluation.dll --endpoint http://127.0.0.1:11435/api/generate --suite smoke --trusted-hints --prompt-overrides docs/model-evaluation/prompt-revisions/tiny-aya-v3.json --revision fewshot-v3 --output .artifacts/evaluation/smoke.jsonl
```

Start a separate Ollama server on port 11435 before running inference. For example, in a separate PowerShell terminal set `$env:OLLAMA_HOST='127.0.0.1:11435'`, `$env:OLLAMA_MAX_LOADED_MODELS='1'`, `$env:OLLAMA_NUM_PARALLEL='1'`, then run `ollama serve`. It shares installed models and physical GPU capacity with production. Do not stop the production server, unload its models or assume a separate port isolates VRAM/performance.

The .NET runner builds **in-memory experimental profiles**, not the private runtime configuration or shipped catalogue. Without `--prompt-overrides`, Tiny Aya uses its formatter's generic instruction, not the catalogue's selected few-shot v3. The example explicitly supplies that revision. `--revision` is a label only; it does not load a prompt.

| Option | Meaning |
| --- | --- |
| `--suite smoke|full|heldout` | Smoke subset; all 17 recovered inputs plus added language/script cases; or synthetic added cases only |
| `--profiles translategemma-12b,salamandra-v3-q4,tiny-aya-global-q8` | Model order/selection, sequential requests grouped by model |
| `--tasks translation|analysis|both` | Task selection; omit Salamandra for analysis because it is translation-only |
| `--trusted-hints` | Use curated source labels where historical requests omitted them, deduplicating identical pairs; this is oracle-hint translation, not end-to-end proof |
| `--repeats 1` | 1–10 repetitions |
| `--cases tasos-italian,fratello-full` | Case filter |
| `--prompt-overrides path.json` | Alternative-profile IDs mapped to system-instruction strings; baseline overrides are forbidden |
| `--revision name` | Evidence label; baseline always records `existing-unchanged` |
| `--output path.jsonl` | Raw evidence; existing output requires explicit `--append` |
| `--root path` | Repository root when launched elsewhere |

The shipped Tiny Aya profile is translation-only after failed qualification. The runner deliberately permits experimental Tiny Aya analysis, e.g. `--profiles translategemma-12b,tiny-aya-global-q8 --tasks analysis`. Never infer production qualification from that capability setting. Unknown-source Salamandra requests fail explicitly; the artificial mixed profanity list has no prescribed source. In `--tasks both`, unsupported analysis is recorded as an error rather than silently switched to another model.

## Evidence and measurement

Every model starts with a separate warmup. Raw JSONL contains exact inputs/outputs, HTTP payloads, effective options/revision, completion errors, latency, residency and sampled NVIDIA data. Warmups are not controlled cold-start tests. Timeouts are bounded to 180 seconds and Ctrl+C cancels requests. The runner pools connections; superseded unpooled experiments had fixed connection overhead and are not headline timings.

Model VRAM comes from endpoint residency; GPU usage/memory samples are **whole-device**, including other processes. Missing samples remain null. Client timing includes HTTP/queue/generation and excludes the later residency query. Mechanical checks cover only a few preservation properties; successful execution can still contain refusal, censorship, omissions or incorrect meaning.

```powershell
python tools/Tsumari.Evaluation/report.py PATH_TO_RESULTS.jsonl .artifacts/evaluation/report.md
python tools/Tsumari.Evaluation/summarize.py PATH_TO_RESULTS.jsonl
```

Generators also accept multiple input files before the output path (report) or as arguments (summarize). Keep revisions separate when interpreting aggregates. [Retained dated results and corpus](../../docs/model-evaluation/README.md).

## TranslateGemma prompt experiments

`translategemma_prompts.py` replays frozen baseline request payloads and changes prompts only in evaluation. It does not relax the production formatter's override guard. It rejects production port 11434, never reads credentials and protects existing output files.

```powershell
python tools/Tsumari.Evaluation/translategemma_prompts.py --dry-run --output .artifacts/gemma-prompts/requests.jsonl
python tools/Tsumari.Evaluation/translategemma_prompts.py --output .artifacts/gemma-prompts/results.jsonl
python tools/Tsumari.Evaluation/translategemma_prompts.py --variants explicit-language-names --output .artifacts/gemma-prompts/language-names.jsonl
```

Options include `--suite all|regression|fresh`, `--variants`, and the dedicated local `--endpoint`. The original 42 baseline requests and 16 newly constructed baseline prompts match the frozen production expressions. All candidates retain temperature 0, top-p 0.1, context 4096 and no added output cap. Prompt hashes and raw responses are recorded. Analysis and model weights are not tuned.

Recovered cases overlap baseline prompt examples; synthetic validation is not a professional gold corpus. Independent assistant judgments are not expert/native-speaker validation. The second language-name stage reused validation, so it is exploratory rather than an untouched final holdout. [Prompt results and method](../../docs/model-evaluation/translategemma-prompt-experiments/REPORT.md).
