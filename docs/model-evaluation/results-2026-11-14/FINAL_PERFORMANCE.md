# Local model evaluation evidence — 14 November 2026

Dated benchmark snapshot; this is not a description of the current running deployment. See [evaluation context](../EVALUATION_REPORT.md).

All outputs are model observations, not gold translations. Mechanical checks do not score semantic quality. Warmups excluded. One run per case unless repeat is recorded. GPU memory and utilization are whole-device measurements, including the untouched live bot and other workloads. Endpoint residency reports model allocation separately. Timing includes the HTTP request and generation, not the later residency query.

| Model / revision | Task | Requests | Successful | Mechanical pass | Mean seconds | Median seconds | Peak whole GPU MiB | Mean whole GPU utilization % | Endpoint model VRAM GiB |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| salamandra-v3-q4 / pooled-v3 | translation | 42 | 41 | 20 | 0.39 | 0.33 | 10324 | 83.56 | 4.33 |
| tiny-aya-global-q8 / pooled-v3 | translation | 42 | 42 | 42 | 0.37 | 0.42 | 9556 | 77.56 | 3.53 |
| translategemma-12b / existing-unchanged | translation | 42 | 42 | 42 | 0.85 | 0.98 | 14286 | 85.65 | 7.28 |

## Warmup requests

These are first requests in each run, not controlled cold-start measurements. Loading and existing cache state vary.

| Model | Revision | Seconds | Error |
|---|---|---:|---|
| translategemma-12b | existing-unchanged | 11.45 |  |
| salamandra-v3-q4 | pooled-v3 | 5.96 |  |
| tiny-aya-global-q8 | pooled-v3 | 4.84 |  |

## Per-request results

| Model / revision | Case | Source → target | Seconds | Peak whole GPU MiB | Mean GPU % | Mechanical checks | Output or failure |
|---|---|---|---:|---:|---:|---|---|
| translategemma-12b / existing-unchanged | fratello-full | EN → IT | 1.17 | 14224 | 48.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Grazie fratello! 🤗<br>Le cose si stanno già scaldando, stanno scommettendo come matti 😅 |
| translategemma-12b / existing-unchanged | fratello-full | EN → DE | 0.95 | 14240 | 96.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Vielen Dank, Bruder! 🤗<br>Die Dinge werden schon heiß, sie wetten wie verrückt 😅 |
| translategemma-12b / existing-unchanged | ma-cherie | EN → DE | 0.49 | 14240 | 97.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Meine Liebste! Wie geht es dir? |
| translategemma-12b / existing-unchanged | ma-cherie | EN → FR | 0.53 | 14237 | 97.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Ma chérie ! Comment vas-tu ? |
| translategemma-12b / existing-unchanged | ma-cherie | EN → EL | 0.61 | 14247 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Αγάπη μου! Πώς είσαι; |
| translategemma-12b / existing-unchanged | hermano | EN → DE | 0.61 | 14268 | 94.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Danke, Bruder, das wird schon verrückt 😂. |
| translategemma-12b / existing-unchanged | pt-intro-short | EN → PT | 0.56 | 14272 | 99.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | caralho, isso já está ficando louco 😂 |
| translategemma-12b / existing-unchanged | pt-intro-long | EN → PT | 1.05 | 14274 | 63.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | foda-se, caralho. As coisas já estão esquentando e as pessoas estão fazendo apostas como loucas 😅 |
| translategemma-12b / existing-unchanged | el-intro-queue | EN → EL | 0.85 | 14269 | 96.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | μαλακα, αυτή η ουρά κινείται γρήγορα απόψε 😂 |
| translategemma-12b / existing-unchanged | mixed-profanity-list | unknown → EN | 0.68 | 14269 | 68.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | fuck it, you fucking asshole, you fucking bitch, get fucked. |
| translategemma-12b / existing-unchanged | mixed-profanity-list | PT → EN | 0.58 | 14269 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | fuck it, asshole, you fucking bitch, get fucked. |
| translategemma-12b / existing-unchanged | el-profanity-sentence | EL → EN | 0.57 | 14270 | 97.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | fuck, go fuck yourself and leave us alone. |
| translategemma-12b / existing-unchanged | pt-profanity-pair | PT → EN | 0.46 | 14270 | 91.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | fuck it, you damn it |
| translategemma-12b / existing-unchanged | pt-exclamation | PT → EN | 0.28 | 14270 | 94.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | fuck! |
| translategemma-12b / existing-unchanged | el-intro-question | EN → EL | 0.57 | 14271 | 93.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | μαλακα, how are you doing tonight? |
| translategemma-12b / existing-unchanged | tasos-italian | IT → EN | 0.61 | 14272 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Yes, Tasos, they can be fun. |
| translategemma-12b / existing-unchanged | mates-monolingual | EN → DE | 0.67 | 14272 | 92.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Ich bin froh, dass ihr alle hier seid, Leute. |
| translategemma-12b / existing-unchanged | greek-name-monolingual | EL → EN | 0.58 | 14273 | 91.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | You fucking Floet, you are really funny. |
| translategemma-12b / existing-unchanged | fratello-shortened | EN → DE | 0.76 | 14274 | 90.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Danke, Bruder! Die Dinge fangen schon an, heiß zu werden. |
| translategemma-12b / existing-unchanged | fratello-question | EN → DE | 0.55 | 14274 | 28.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Vielen Dank, Bruder! Wie geht es dir? |
| translategemma-12b / existing-unchanged | hello-control | EN → DE | 0.30 | 14274 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Hallo zusammen. |
| translategemma-12b / existing-unchanged | heldout-DE | DE → EN | 1.07 | 14274 | 98.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Damn, Alex! The train doesn't leave until 6:30 PM.<br>**Wait for me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EL | EL → EN | 1.00 | 14282 | 60.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN | EN → SR | 1.06 | 14282 | 65.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Jebi ga, Alex! Voz kreće u 18:30.<br>**Čekaj me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-IT | IT → EN | 1.01 | 14284 | 86.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-PT | PT → EN | 0.98 | 14285 | 95.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-ES | ES → EN | 0.98 | 14285 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Damn, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-SR | SR → EN | 1.00 | 14285 | 96.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Fuck, Alex! The bus leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-ZH | ZH → EN | 1.02 | 14277 | 89.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Fucking hell, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-SR-Latn | SR → EN | 0.95 | 14278 | 68.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Hey Aleks! The bus leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-ZH-Hant | ZH → EN | 1.05 | 14278 | 84.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Fucking hell, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-DE | EN → DE | 1.11 | 14278 | 59.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Verdammt, Alex! Der Zug fährt um 18:30 ab.<br>**Warte auf mich!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-EL | EN → EL | 1.38 | 14283 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Άμάν, Αλέξ! Το τρένο φεύγει στις 18:30.<br>**Περίμενέ με!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-IT | EN → IT | 1.08 | 14286 | 62.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Accidenti, Alex! Il treno parte alle 18:30.<br>**Aspettami!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-PT | EN → PT | 1.05 | 14280 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Droga, Alex! O trem sai às 18:30.<br>**Espere por mim!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-ES | EN → ES | 1.20 | 14281 | 97.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | ¡Maldita sea, Alex! El tren sale a las 18:30.<br>**¡Espera a mí!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-ZH | EN → ZH | 1.01 | 14282 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | 该死，亚历克斯！火车 18:30 开车。<br>**等我！** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-SR-LATN | EN → SR-LATN | 1.09 | 14283 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Jebi ga, Alex! Voz odlazi u 18:30.<br>**Čekaj me!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-SR-CYRL | EN → SR-CYRL | 1.18 | 14283 | 50.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Јеба, Алекс! Возот полази у 18:30.<br>**Чекај ме!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-ZH-HANT | EN → ZH-HANT | 1.09 | 14283 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | 該死，亞歷克斯！ 火車在 18:30 出發。<br>**等我！** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-PT-BR | EN → PT-BR | 1.04 | 14283 | 95.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Droga, Alex! O trem sai às 18:30.<br>**Espere por mim!** 😂 |
| translategemma-12b / existing-unchanged | heldout-EN-to-PT-PT | EN → PT-PT | 1.06 | 14284 | 96.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Caralho, Alex! O comboio parte às 18:30.<br>**Espera por mim!** 😂 |
| salamandra-v3-q4 / pooled-v3 | fratello-full | EN → IT | 0.65 | 10315 | 48.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Grazie fratello! 🤗<br>Le cose si stanno già scaldando, stanno scommettendo come pazzi 😅 |
| salamandra-v3-q4 / pooled-v3 | fratello-full | EN → DE | 0.58 | 10315 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Danke, Bruder! 🤗<br>Es wird schon heiß, die Leute setzen wie verrückt 😅 |
| salamandra-v3-q4 / pooled-v3 | ma-cherie | EN → DE | 0.20 | 10315 | 94.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Ma cherie! Wie geht es dir? |
| salamandra-v3-q4 / pooled-v3 | ma-cherie | EN → FR | 0.21 | 10315 | 67.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Ma chérie ! Comment vas-tu ? |
| salamandra-v3-q4 / pooled-v3 | ma-cherie | EN → EL | 0.20 | 10315 | 67.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Αγαπημένη μου! Πώς είσαι; |
| salamandra-v3-q4 / pooled-v3 | hermano | EN → DE | 0.32 | 10315 | 91.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Danke, Bruder, das wird schon wild 😂 |
| salamandra-v3-q4 / pooled-v3 | pt-intro-short | EN → PT | 0.34 | 10315 | 91.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | caralho, isso já está ficando selvagem 😂 |
| salamandra-v3-q4 / pooled-v3 | pt-intro-long | EN → PT | 0.57 | 10316 | 2.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | foda se, caralho. As coisas já estão esquentando e as pessoas estão apostando como loucas 😅 |
| salamandra-v3-q4 / pooled-v3 | el-intro-queue | EN → EL | 0.38 | 10316 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | μαλάκα, αυτή η ουρά κινείται γρήγορα απόψε 😂 |
| salamandra-v3-q4 / pooled-v3 | mixed-profanity-list | unknown → EN | 0.00 | None | unavailable |  | NotSupportedException: Profile 'salamandra-v3-q4' has no explicit language mapping for ''. |
| salamandra-v3-q4 / pooled-v3 | mixed-profanity-list | PT → EN | 0.27 | 10314 | 85.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | fuck, fuck, μαλακα, γαμήσου |
| salamandra-v3-q4 / pooled-v3 | el-profanity-sentence | EL → EN | 0.23 | 10314 | 85.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Dude, fuck off and leave us alone. |
| salamandra-v3-q4 / pooled-v3 | pt-profanity-pair | PT → EN | 0.16 | 10315 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | fuck, damn it |
| salamandra-v3-q4 / pooled-v3 | pt-exclamation | PT → EN | 0.10 | 10315 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Damn! |
| salamandra-v3-q4 / pooled-v3 | el-intro-question | EN → EL | 0.22 | 10317 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | μαλάκα, πώς είσαι απόψε; |
| salamandra-v3-q4 / pooled-v3 | tasos-italian | IT → EN | 0.25 | None | unavailable | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Yes, Tasos, they can be amusing. |
| salamandra-v3-q4 / pooled-v3 | mates-monolingual | EN → DE | 0.32 | 10320 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Ich bin froh, dass ihr alle hier seid, Leute |
| salamandra-v3-q4 / pooled-v3 | greek-name-monolingual | EL → EN | 0.31 | 10320 | 91.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Shit, Floet, you're really funny after all. |
| salamandra-v3-q4 / pooled-v3 | fratello-shortened | EN → DE | 0.27 | 10320 | 94.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Danke, Bruder! Die Stimmung wird schon heiß |
| salamandra-v3-q4 / pooled-v3 | fratello-question | EN → DE | 0.25 | 10323 | 94.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Danke, Bruder! Wie geht es dir? |
| salamandra-v3-q4 / pooled-v3 | hello-control | EN → DE | 0.10 | None | unavailable | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Hallo zusammen! |
| salamandra-v3-q4 / pooled-v3 | heldout-DE | DE → EN | 0.56 | 10322 | 71.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Damn Alex! The train doesn't leave until 6:30. **Wait for me!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EL | EL → EN | 0.33 | 10324 | 1.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved FAIL; lineBreakPreserved FAIL; boldPreserved FAIL | Damn, Alex! The train leaves at 6:30. |
| salamandra-v3-q4 / pooled-v3 | heldout-EN | EN → SR | 0.59 | 10322 | 1.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Дођавола, Алекс! Воз иде у 18:30. **Чекај ме!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-IT | IT → EN | 0.32 | 10322 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved FAIL; lineBreakPreserved FAIL; boldPreserved FAIL | Shit, Alex! The train leaves at 6:30. |
| salamandra-v3-q4 / pooled-v3 | heldout-PT | PT → EN | 0.32 | 10322 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved FAIL; lineBreakPreserved FAIL; boldPreserved FAIL | Damn, Alex! The train leaves at 6:30. |
| salamandra-v3-q4 / pooled-v3 | heldout-ES | ES → EN | 0.32 | 10322 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved FAIL; lineBreakPreserved FAIL; boldPreserved FAIL | Fuck, Alex! The train leaves at 6:30. |
| salamandra-v3-q4 / pooled-v3 | heldout-SR | SR → EN | 0.32 | 10320 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved FAIL; lineBreakPreserved FAIL; boldPreserved FAIL | Fuck, Alex! The train leaves at 6:30. |
| salamandra-v3-q4 / pooled-v3 | heldout-ZH | ZH → EN | 0.52 | 10320 | 92.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Shit, Alex! The train leaves at 6:30. **Wait for me!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-SR-Latn | SR → EN | 0.32 | 10321 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved FAIL; lineBreakPreserved FAIL; boldPreserved FAIL | Fuck, Alex! The train leaves at 6:30. |
| salamandra-v3-q4 / pooled-v3 | heldout-ZH-Hant | ZH → EN | 0.51 | 10322 | 94.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Mother, Alex! The train leaves at 6:30. **Wait for me!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-DE | EN → DE | 0.66 | 10322 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Verdammt nochmal, Alex! Der Zug fährt um 18:30 Uhr ab. **Wartet auf mich!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-EL | EN → EL | 0.70 | 10322 | 88.50 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Διάολε, Άλεξ! Το τρένο φεύγει στις 18:30. **Περιμένετέ με!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-IT | EN → IT | 0.56 | 10322 | 97.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Cazzo, Alex! Il treno parte alle 18:30. **Aspettami!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-PT | EN → PT | 0.55 | 10317 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Caraca, Alex! O trem parte às 18:30. **Espere por mim!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-ES | EN → ES | 0.63 | 10317 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | ¡Maldita sea, Alex! El tren sale a las 18:30. **¡Espérame!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-ZH | EN → ZH | 0.47 | 10321 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | 该死的，Alex！火车18:30出发。**等我！** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-SR-LATN | EN → SR-LATN | 0.35 | 10321 | 91.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved FAIL; lineBreakPreserved FAIL; boldPreserved FAIL | Dođavola, Aleks! Voz polazi u 18:30. |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-SR-CYRL | EN → SR-CYRL | 0.59 | 10321 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Дођавола, Алекс! Воз иде у 18:30. **Чекај ме!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-ZH-HANT | EN → ZH-HANT | 0.46 | 10318 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | 该死的，Alex！火车18:30出发。**等我！** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-PT-BR | EN → PT-BR | 0.55 | 10318 | 96.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Caraca, Alex! O trem parte às 18:30. **Espere por mim!** 😂 |
| salamandra-v3-q4 / pooled-v3 | heldout-EN-to-PT-PT | EN → PT-PT | 0.59 | 10318 | 97.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved FAIL; boldPreserved ✓ | Maldita seja, Alex! O trem parte às 18:30. **Espere por mim!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | fratello-full | EN → IT | 0.47 | 9545 | 59.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Grazie fratello! 🤗<br>Le cose stanno già surriscaldando, stanno facendo scommesse come pazzi 😅 |
| tiny-aya-global-q8 / pooled-v3 | fratello-full | EN → DE | 0.48 | 9551 | 92.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Danke, Bruder! 🤗<br>Die Dinge werden schon heiß, sie setzen Wetten wie verrückt 😅 |
| tiny-aya-global-q8 / pooled-v3 | ma-cherie | EN → DE | 0.23 | 9546 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Ma cherie! Wie geht es dir? |
| tiny-aya-global-q8 / pooled-v3 | ma-cherie | EN → FR | 0.21 | 9546 | 1.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Ma chérie! Comment ça va? |
| tiny-aya-global-q8 / pooled-v3 | ma-cherie | EN → EL | 0.23 | 9546 | 1.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Μα γεια σου! Πώς είσαι; |
| tiny-aya-global-q8 / pooled-v3 | hermano | EN → DE | 0.25 | 9546 | 92.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Danke hermano, das wird schon verrückt 😂 |
| tiny-aya-global-q8 / pooled-v3 | pt-intro-short | EN → PT | 0.25 | 9546 | 92.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Caralho, isso já está ficando selvagem mesmo 😂 |
| tiny-aya-global-q8 / pooled-v3 | pt-intro-long | EN → PT | 0.49 | 9546 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Foda-se, caralho. As coisas já estão a aquecer e as pessoas estão a fazer apostas como loucas 😅 |
| tiny-aya-global-q8 / pooled-v3 | el-intro-queue | EN → EL | 0.30 | 9546 | 91.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Μαλάκα, η ουρά κινείται γρήγορα απόψε 😂 |
| tiny-aya-global-q8 / pooled-v3 | mixed-profanity-list | unknown → EN | 0.26 | 9542 | 90.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Shit, fuck, idiot, fuck yourself. |
| tiny-aya-global-q8 / pooled-v3 | mixed-profanity-list | PT → EN | 0.26 | 9542 | 90.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Fuck you, shit, fuck you, fuck you. |
| tiny-aya-global-q8 / pooled-v3 | el-profanity-sentence | EL → EN | 0.25 | 9542 | 63.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Shit, fuck yourself and leave us alone. |
| tiny-aya-global-q8 / pooled-v3 | pt-profanity-pair | PT → EN | 0.17 | 9546 | 40.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Fuck you, shit. |
| tiny-aya-global-q8 / pooled-v3 | pt-exclamation | PT → EN | 0.13 | 9546 | 40.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Shit! |
| tiny-aya-global-q8 / pooled-v3 | el-intro-question | EN → EL | 0.20 | 9546 | 69.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Τι κάνεις απόψε; |
| tiny-aya-global-q8 / pooled-v3 | tasos-italian | IT → EN | 0.23 | 9546 | 69.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Yes, Taso, you can be funny |
| tiny-aya-global-q8 / pooled-v3 | mates-monolingual | EN → DE | 0.33 | None | unavailable | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Ich freue mich, dass ihr alle hier seid, Kumpels. |
| tiny-aya-global-q8 / pooled-v3 | greek-name-monolingual | EL → EN | 0.27 | 9549 | 68.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Idiot Fletch, you're actually very funny. |
| tiny-aya-global-q8 / pooled-v3 | fratello-shortened | EN → DE | 0.27 | 9552 | 93.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Danke fratello! Die Dinge werden schon heiß. |
| tiny-aya-global-q8 / pooled-v3 | fratello-question | EN → DE | 0.27 | 9553 | 93.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Danke, Bruder! Wie geht's dir? |
| tiny-aya-global-q8 / pooled-v3 | hello-control | EN → DE | 0.11 | 9553 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓ | Hallo zusammen |
| tiny-aya-global-q8 / pooled-v3 | heldout-DE | DE → EN | 0.46 | 9553 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Damn, Alex! The train only leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EL | EL → EN | 0.49 | 9555 | 93.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Shit, Alex! The train leaves at 6:30 PM.  <br>**Wait for me!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN | EN → SR | 0.53 | 9555 | 92.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Проклетство, Алекс! Вагон креће у 18:30.<br>**Чекај ме!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-IT | IT → EN | 0.44 | 9553 | 93.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Crap, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-PT | PT → EN | 0.44 | 9553 | 92.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Shit, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-ES | ES → EN | 0.49 | 9553 | 94.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Joder, Alex! El tren sale a las 18:30.<br>**¡Espérame!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-SR | SR → EN | 0.47 | 9554 | 94.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Shit, Alex! The bus leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-ZH | ZH → EN | 0.40 | 9554 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Alex, fuck! The train leaves at 6:30 PM.  <br>**Wait!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-SR-Latn | SR → EN | 0.47 | 9554 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Jebote, Aleks! Voz polazi u 18:30.<br>**Sačekaj me!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-ZH-Hant | ZH → EN | 0.42 | 9556 | 76.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Alex, shit! The train leaves at 6:30 PM.  <br>**Wait!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-DE | EN → DE | 0.47 | 9556 | 76.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Verdammt, Alex! Der Zug fährt um 18:30 Uhr ab.<br>**Warte auf mich!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-EL | EN → EL | 0.59 | 9556 | 58.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | **Γαμώτο, Άλεξ!** Το τρένο φεύγει στις 18:30.<br>**Περίμενε με!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-IT | EN → IT | 0.47 | 9555 | 94.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | **Al diavolo, Alex!** Il treno parte alle 18:30.<br>**Aspettami!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-PT | EN → PT | 0.45 | 9555 | 94.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | **Droga, Alex! O trem parte às 18:30.**<br>**Espera por mim!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-ES | EN → ES | 0.50 | 9552 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | ¡Maldición, Alex! El tren sale a las 18:30.<br>**¡Espera por mí!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-ZH | EN → ZH | 0.36 | 9552 | 70.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | 该死，Alex！火车18:30出发。<br>**等我！** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-SR-LATN | EN → SR-LATN | 0.54 | 9552 | 69.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | **Prokletstvo, Aleks!** Voz polazi u 18:30.<br>**Čekaj me!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-SR-CYRL | EN → SR-CYRL | 0.52 | 9552 | 1.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Проклетство, Алекс! Вагон креће у 18:30.<br>**Чекај ме!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-ZH-HANT | EN → ZH-HANT | 0.42 | 9553 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | **該死，Alex！火車18:30出發。**<br>**等我！** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-PT-BR | EN → PT-BR | 0.44 | 9553 | 95.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Droga, Alex! O trem sai às 18:30.<br>**Espere por mim!** 😂 |
| tiny-aya-global-q8 / pooled-v3 | heldout-EN-to-PT-PT | EN → PT-PT | 0.49 | 9553 | 86.00 | nonEmpty ✓; noMarkdownFenceAdded ✓; emojiPreserved ✓; lineBreakPreserved ✓; boldPreserved ✓ | Merda, Alex! O comboio parte às 18:30.<br>**Espera por mim!** 😂 |
