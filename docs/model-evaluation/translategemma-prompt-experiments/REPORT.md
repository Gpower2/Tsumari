# TranslateGemma prompt refinement results

**Result: specific failures can be improved, but none of the three tested revisions is a clear replacement for the current prompt. The experiment did not establish a replacement for the baseline prompt. Production changes are governed separately.**

We ran 232 local requests: four prompt variants against 58 translation requests each. Forty-two are existing regression requests and sixteen are new synthetic validation requests covering all eight required source languages. Variants use the same installed TranslateGemma 12B weights, Ollama template and decoding settings. Analysis was not changed or retuned.

## Independent review

An independent assistant reviewed outputs with candidate identities hidden, using the same rubric across all outputs: completeness, facts, referents, negation, natural grammar, register/profanity, names, script and formatting. Judgments are not expert/native-speaker validation or human-reviewed gold accuracy. Regional register and Serbian naturalness retain uncertainty even where a categorical judgment is recorded.

| Prompt | Existing regressions: clean / minor / major | Fresh validation: clean / minor / major | Total clean / minor / major |
| --- | --- | --- | --- |
| Unchanged baseline | **28 / 6 / 8** | **13 / 0 / 3** | **41 / 6 / 11** |
| Original plus fidelity amendment | 27 / 8 / 7 | 12 / 0 / 4 | 39 / 8 / 11 |
| Shorter, language-aware rewrite | 20 / 11 / 11 | 10 / 0 / 6 | 30 / 11 / 17 |
| Original plus explicit language names | 25 / 11 / **6** | **13 / 0 / 3** | 38 / 11 / **9** |

“Clean” means no identified issue under the review rubric; it is not a general translation accuracy score. The language-name-only variant reduces major judgments by two, but adds five minor judgments and has three fewer clean results. Different severity preferences could value this tradeoff differently; we do not hide it behind a single composite score.

Blind labels: Y = baseline, Z = fidelity amendment, X = shorter rewrite, W = language names. Full judgments and pairwise comparisons are in [BLIND_REVIEW.json](BLIND_REVIEW.json) and [BLIND_REVIEW_W.json](BLIND_REVIEW_W.json). The later W stage reused validation after the first stage was reviewed; it is exploratory, not an untouched final holdout.

## What improved, and what regressed

- The unchanged prompt leaves the English part of `μαλακα, how are you doing tonight?` untranslated for a Greek target. All three revisions translate the question, although wording/accent defects can remain.
- Explicit language names help preserve Serbian transport facts: several train/bus or train/bus/car confusions improve. They do not consistently preserve the original profanity force.
- The shorter rewrite changes `Si Tasos , sanno essere divertenti` into `Yes, Tasos can be fun.` It replaces the plural subject with the addressed name. The baseline and language-name-only variant retain `Yes, Tasos, they can be fun.`
- The language-name-only variant leaves `fratello` untranslated in several targets and introduces a Greek-word defect. Its reduced major-error count therefore is not a free improvement.
- Additional fidelity instructions do not reliably prevent invented personal/gendered targets when translating impersonal Portuguese profanity. More instruction text alone did not solve this failure.
- Fresh examples reveal further regressions in profanity strength, Chinese quoted-text translation and Italian addressed names. These matter even when the recovered cases improve.

## Experimental controls

- The 42 control request payload objects exactly match the previous evaluation, and all 42 control outputs are byte-identical to the previous baseline responses.
- Fresh baseline system prompts exactly match the frozen production expressions, including conditional Italian disambiguation. No controls were replaced with a simplified approximation.
- All variants retain temperature 0, top-p 0.1, context 4096 and no additional output cap. No weights, installed model template or generation policy were changed.
- All 232 requests completed without execution errors. Quality failures remain explicitly recorded rather than counted as successes in meaning preservation.
- The experiment runner replays captured requests outside the production provider; production TranslateGemma overrides remain forbidden. No main-project source/configuration was edited for these experiments.
- During this dated experiment, only evaluation tooling and evidence were added; hashes of the protected formatter/catalogue/release files were unchanged and the dedicated server was stopped afterward. These observations do not describe the current deployment state.

## Recommendation

The experiment supports retaining its baseline prompt. The most promising small direction is explicit language naming, particularly for Serbian and Greek-target requests, but it needs narrower testing that demonstrates gains without introducing the observed regressions. Replacing the whole prompt with a shorter version is unsupported by this evidence; this is not a claim about the currently running binary version.

The new cases are synthetic, recovered regressions partly overlap the existing prompt examples, and only one deterministic local run per request was performed. There is no evidence here that a universal wording change fixes multilingual translation fidelity. A future candidate should be tested against a genuinely new validation set before adoption.

[All 232 inputs, outputs and durations](ALL_OUTPUTS.md). Exact prompts/hashes/raw responses are in [initial three variants](results.jsonl) and [language-name-only variant](language-names.jsonl). [Method and reproduction instructions](../../../tools/Tsumari.Evaluation/README.md).
