# Recovered cases

Semantic checks below are curated requirements, not human-verified gold translations. See cases.json for exact text, source hints and provenance.

## fratello-full

Origin: `user_reported_real_example`. Dominant language: `EN`.

```text
Thank you fratello! 🤗
Things are already heating up, they are placing bets like crazy 😅
```

English dominates; translate the English body even when the target is Italian. Preserve the brother/address meaning, newline and emojis.

## ma-cherie

Origin: `user_reported_real_example`. Dominant language: `EN`.

```text
Ma cherie! How are you doing?
```

English dominates, with a meaningful French phrase. Translate the whole message and retain the affectionate address.

## hermano

Origin: `copilot_added_probe`. Dominant language: `EN`.

```text
Thanks hermano, this is getting wild already 😂
```

English dominates; a Spanish address must not suppress translation of the rest. Preserve emoji and address.

## pt-intro-short

Origin: `copilot_added_probe`. Dominant language: `EN`.

```text
caralho, this is getting wild already 😂
```

English dominates; preserve the Portuguese expletive force and emoji without inventing a directed insult.

## pt-intro-long

Origin: `copilot_added_probe`. Dominant language: `EN`.

```text
foda se, caralho. Things are already heating up and people are placing bets like crazy 😅
```

Longer English clause dominates; preserve both Portuguese profanity expressions and emoji.

## el-intro-queue

Origin: `copilot_added_probe`. Dominant language: `EN`.

```text
μαλακα, this queue is moving fast tonight 😂
```

English dominates; preserve Greek profanity/address force. When translating to Greek, do not soften μαλακα to μάγκα.

## mixed-profanity-list

Origin: `copilot_probe_from_user_terms`. Dominant language: `not prescribed`.

```text
foda se, caralho, μαλακα, γαμήσου
```

Artificial mixed-language stress case. Do not enforce a unique dominant language. Preserve expletives without omissions, softening or invented personal targets; human review required.

## el-profanity-sentence

Origin: `copilot_added_probe`. Dominant language: `EL`.

```text
μαλακα, γαμήσου και άσε μας ήσυχους
```

Preserve address/insult, explicit dismissal and leave-us-alone meaning without censorship or omitted elements.

## pt-profanity-pair

Origin: `copilot_probe_from_user_terms`. Dominant language: `PT`.

```text
foda se, caralho
```

Preserve both expletives; distinguish standalone frustration from a directed personal insult. Human review of register required.

## pt-exclamation

Origin: `copilot_probe_from_user_terms`. Dominant language: `PT`.

```text
caralho!
```

Treat as a standalone exclamation; do not invent a person or target.

## el-intro-question

Origin: `copilot_added_probe`. Dominant language: `EN`.

```text
μαλακα, how are you doing tonight?
```

English question dominates; preserve Greek address/profanity force, rather than changing μαλακα to μάγκα.

## tasos-italian

Origin: `user_reported_real_example`. Dominant language: `IT`.

```text
Si Tasos , sanno essere divertenti
```

Si means affirmative sì/Yes, not If. Tasos is a proper name and does not establish Greek content. Preserve the plural they and amusement meaning.

## mates-monolingual

Origin: `user_reported_production_example`. Dominant language: `EN`.

```text
I'm glad you all are here mates
```

Monolingual English: mates is not evidence of Italian; do not hallucinate a second language.

## greek-name-monolingual

Origin: `user_reported_production_example`. Dominant language: `EL`.

```text
Μαλάκα Φλοετ τελικά είσαι πολύ αστείος
```

Monolingual Greek with a proper/transliterated name. Do not hallucinate English; preserve name and slang/register.

## fratello-shortened

Origin: `copilot_derived_variant`. Dominant language: `EN`.

```text
Thank you fratello! Things are already heating up
```

English dominates; do not let the short Italian address change routing. Minor-language collapse is a separate application policy.

## fratello-question

Origin: `copilot_derived_variant`. Dominant language: `EN`.

```text
Thank you fratello! How are you doing?
```

English dominates; preserve address and question. Do not confuse raw language identification with label policy.

## hello-control

Origin: `production_log_prefix_reused_as_control`. Dominant language: `EN`.

```text
Hello everybody
```

Simple English control used for warm/cold timing and JSON output experiments; original log alone does not prove full-message completeness.
