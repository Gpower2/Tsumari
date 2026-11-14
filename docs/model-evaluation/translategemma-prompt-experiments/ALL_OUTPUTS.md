# TranslateGemma prompt experiments: complete outputs

232 requests. All prompts are evaluation-only. Timing includes client overhead and first-call loading; not a controlled speed benchmark. Blind judgments and the recommendation are in REPORT.md.

| Prompt variant | Case | Source to target | Seconds | Input | Output |
|---|---|---|---:|---|---|
| baseline | fratello-full | EN to IT | 5,947 | Thank you fratello! 🤗<br>Things are already heating up, they are placing bets like crazy 😅 | Grazie fratello! 🤗<br>Le cose si stanno già scaldando, stanno scommettendo come matti 😅 |
| baseline | fratello-full | EN to DE | 1,012 | Thank you fratello! 🤗<br>Things are already heating up, they are placing bets like crazy 😅 | Vielen Dank, Bruder! 🤗<br>Die Dinge werden schon heiß, sie wetten wie verrückt 😅 |
| baseline | ma-cherie | EN to DE | 0,544 | Ma cherie! How are you doing? | Meine Liebste! Wie geht es dir? |
| baseline | ma-cherie | EN to FR | 0,588 | Ma cherie! How are you doing? | Ma chérie ! Comment vas-tu ? |
| baseline | ma-cherie | EN to EL | 0,656 | Ma cherie! How are you doing? | Αγάπη μου! Πώς είσαι; |
| baseline | hermano | EN to DE | 0,666 | Thanks hermano, this is getting wild already 😂 | Danke, Bruder, das wird schon verrückt 😂. |
| baseline | pt-intro-short | EN to PT | 0,612 | caralho, this is getting wild already 😂 | caralho, isso já está ficando louco 😂 |
| baseline | pt-intro-long | EN to PT | 1,092 | foda se, caralho. Things are already heating up and people are placing bets like crazy 😅 | foda-se, caralho. As coisas já estão esquentando e as pessoas estão fazendo apostas como loucas 😅 |
| baseline | el-intro-queue | EN to EL | 0,860 | μαλακα, this queue is moving fast tonight 😂 | μαλακα, αυτή η ουρά κινείται γρήγορα απόψε 😂 |
| baseline | mixed-profanity-list |  to EN | 0,689 | foda se, caralho, μαλακα, γαμήσου | fuck it, you fucking asshole, you fucking bitch, get fucked. |
| baseline | mixed-profanity-list | PT to EN | 0,609 | foda se, caralho, μαλακα, γαμήσου | fuck it, asshole, you fucking bitch, get fucked. |
| baseline | el-profanity-sentence | EL to EN | 0,569 | μαλακα, γαμήσου και άσε μας ήσυχους | fuck, go fuck yourself and leave us alone. |
| baseline | pt-profanity-pair | PT to EN | 0,459 | foda se, caralho | fuck it, you damn it |
| baseline | pt-exclamation | PT to EN | 0,278 | caralho! | fuck! |
| baseline | el-intro-question | EN to EL | 0,586 | μαλακα, how are you doing tonight? | μαλακα, how are you doing tonight? |
| baseline | tasos-italian | IT to EN | 0,613 | Si Tasos , sanno essere divertenti | Yes, Tasos, they can be fun. |
| baseline | mates-monolingual | EN to DE | 0,699 | I'm glad you all are here mates | Ich bin froh, dass ihr alle hier seid, Leute. |
| baseline | greek-name-monolingual | EL to EN | 0,626 | Μαλάκα Φλοετ τελικά είσαι πολύ αστείος | You fucking Floet, you are really funny. |
| baseline | fratello-shortened | EN to DE | 0,789 | Thank you fratello! Things are already heating up | Danke, Bruder! Die Dinge fangen schon an, heiß zu werden. |
| baseline | fratello-question | EN to DE | 0,570 | Thank you fratello! How are you doing? | Vielen Dank, Bruder! Wie geht es dir? |
| baseline | hello-control | EN to DE | 0,310 | Hello everybody | Hallo zusammen. |
| baseline | heldout-DE | DE to EN | 1,119 | Verdammt, Alex! Der Zug fährt erst um 18:30.<br>**Warte auf mich!** 😂 | Damn, Alex! The train doesn't leave until 6:30 PM.<br>**Wait for me!** 😂 |
| baseline | heldout-EL | EL to EN | 1,084 | Γαμώτο, Άλεξ! Το τρένο φεύγει στις 18:30.<br>**Περίμενέ με!** 😂 | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| baseline | heldout-EN | EN to SR | 1,108 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Jebi ga, Alex! Voz kreće u 18:30.<br>**Čekaj me!** 😂 |
| baseline | heldout-IT | IT to EN | 1,092 | Cazzo, Alex! Il treno parte alle 18:30.<br>**Aspettami!** 😂 | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| baseline | heldout-PT | PT to EN | 1,034 | Porra, Alex! O comboio parte às 18:30.<br>**Espera por mim!** 😂 | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| baseline | heldout-ES | ES to EN | 1,032 | ¡Joder, Alex! El tren sale a las 18:30.<br>**Espérame!** 😂 | Damn, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| baseline | heldout-SR | SR to EN | 1,017 | Јеботе, Алекс! Воз полази у 18:30.<br>**Сачекај ме!** 😂 | Fuck, Alex! The bus leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| baseline | heldout-ZH | ZH to EN | 1,082 | 妈的，Alex！火车18:30发车。<br>**等我！** 😂 | Fucking hell, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| baseline | heldout-SR-Latn | SR to EN | 0,987 | Jebote, Aleks! Voz polazi u 18:30.<br>**Sačekaj me!** 😂 | Hey Aleks! The bus leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| baseline | heldout-ZH-Hant | ZH to EN | 1,105 | 媽的，Alex！火車18:30發車。<br>**等我！** 😂 | Fucking hell, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| baseline | heldout-EN-to-DE | EN to DE | 1,150 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Verdammt, Alex! Der Zug fährt um 18:30 ab.<br>**Warte auf mich!** 😂 |
| baseline | heldout-EN-to-EL | EN to EL | 1,378 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Άμάν, Αλέξ! Το τρένο φεύγει στις 18:30.<br>**Περίμενέ με!** 😂 |
| baseline | heldout-EN-to-IT | EN to IT | 1,133 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Accidenti, Alex! Il treno parte alle 18:30.<br>**Aspettami!** 😂 |
| baseline | heldout-EN-to-PT | EN to PT | 1,080 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Droga, Alex! O trem sai às 18:30.<br>**Espere por mim!** 😂 |
| baseline | heldout-EN-to-ES | EN to ES | 1,228 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | ¡Maldita sea, Alex! El tren sale a las 18:30.<br>**¡Espera a mí!** 😂 |
| baseline | heldout-EN-to-ZH | EN to ZH | 1,037 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | 该死，亚历克斯！火车 18:30 开车。<br>**等我！** 😂 |
| baseline | heldout-EN-to-SR-LATN | EN to SR-LATN | 1,133 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Jebi ga, Alex! Voz odlazi u 18:30.<br>**Čekaj me!** 😂 |
| baseline | heldout-EN-to-SR-CYRL | EN to SR-CYRL | 1,201 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Јеба, Алекс! Возот полази у 18:30.<br>**Чекај ме!** 😂 |
| baseline | heldout-EN-to-ZH-HANT | EN to ZH-HANT | 1,108 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | 該死，亞歷克斯！ 火車在 18:30 出發。<br>**等我！** 😂 |
| baseline | heldout-EN-to-PT-BR | EN to PT-BR | 1,124 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Droga, Alex! O trem sai às 18:30.<br>**Espere por mim!** 😂 |
| baseline | heldout-EN-to-PT-PT | EN to PT-PT | 1,111 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Caralho, Alex! O comboio parte às 18:30.<br>**Espera por mim!** 😂 |
| baseline | fresh-00-DE | DE to EN | 1,402 | Scheiße! Nicht den Bus nehmen — die Fähre fährt morgen um 07:15.<br>**Mara wartet nicht auf uns.** 😅 | Shit! Don't take the bus — the ferry leaves tomorrow at 07:15.<br>**Mara isn't waiting for us.** 😅 |
| baseline | fresh-01-EL | EL to EN | 1,221 | Γαμώτο! Δεν πλήρωσα 50 ευρώ, πλήρωσα 15.<br>**Ρώτα τη Νίκη αύριο.** 😅 | Fucking hell! I didn't pay 50 euros, I paid 15.<br>**Ask Niki tomorrow.** 😅 |
| baseline | fresh-02-EN | EN to DE | 1,272 | Shit! Ada did not cancel the meeting; she moved it to Friday.<br>**Keep the receipt.** 😅 | Scheiße! Ada hat das Meeting nicht abgesagt; sie hat es auf Freitag verschoben.<br>**Behalte den Beleg.** 😅 |
| baseline | fresh-03-IT | IT to EN | 1,215 | Cazzo! Si Luca, abbiamo ordinato due sedie, non tre tavoli.<br>**Non chiamare ancora.** 😅 | Fuck! Yes, it's Luca, we ordered two chairs, not three tables.<br>**Don't call yet.** 😅 |
| baseline | fresh-04-PT | PT to EN | 1,247 | Porra! A Rita não perdeu a chave; deixou-a na gaveta azul.<br>**Não abras a porta.** 😅 | Fuck! Rita didn't lose the key; she left it in the blue drawer.<br>**Don't open the door.** 😅 |
| baseline | fresh-05-ES | ES to EN | 0,970 | ¡Joder! Clara llega el jueves, no el martes.<br>**Guarda el número 47.** 😅 | Fuck! Clara arrives on Thursday, not on Tuesday.<br>**Save number 47.** 😅 |
| baseline | fresh-06-SR | SR to EN | 1,133 | Јеботе! Ана није продала бицикл; позајмила га је Милану.<br>**Позови је сутра.** 😅 | Fucking hell! Ana didn't sell the bicycle; she lent it to Milan.<br>**Call her tomorrow.** 😅 |
| baseline | fresh-07-ZH | ZH to EN | 1,321 | 妈的！小林没丢钱包，他把钱包放在绿色盒子里了。<br>**别打电话。** 😅 | Fucking hell! Xiaolin didn't lose his wallet; he put his wallet in the green box.<br>**Don't call.** 😅 |
| baseline | fresh-08-DE | DE to EN | 1,415 | Er sagte: "Bitte nicht löschen." Der Link bleibt https://example.com/a?q=7 und der Code `x != 3`. | He said: "Please do not delete." The link remains https://example.com/a?q=7 and the code is `x != 3`. |
| baseline | fresh-09-EL | EL to EN | 1,273 | Η Ίριδα είπε «όχι ακόμα». Το λεωφορείο φεύγει στις 21:40, το πλοίο στις 22:10. | Irida said, "Not yet." The bus leaves at 9:40 PM, the ferry at 10:10 PM. |
| baseline | fresh-10-EN | EN to EL | 1,996 | My friend said "no thanks", not "yes please". The password example is `red_blue`, not a real credential. | Ο φίλος μου είπε "όχι, ευχαριστώ", όχι "ναι, παρακαλώ". Το παράδειγμα κωδικού πρόσβασης είναι `red_blue`, όχι ένα πραγματικό διαπιστευτήριο. |
| baseline | fresh-11-IT | IT to EN | 0,988 | Si Marta, sono loro che devono aspettare noi, non il contrario. | Yes, Marta, it's them who have to wait for us, not the other way around. |
| baseline | fresh-12-PT | PT to EN | 0,874 | O autocarro está atrasado, mas o comboio chegou a horas. A Joana não está zangada. | The bus is late, but the train arrived on time. Joana is not angry. |
| baseline | fresh-13-ES | ES to EN | 1,176 | No es una amenaza: "vete a la mierda" es la frase que quiero citar, sin dirigirla a nadie aquí. | It's not a threat: "go to hell" is the phrase I want to quote, without directing it at anyone here. |
| baseline | fresh-14-SR | SR to EN | 0,951 | Voz je stigao pre autobusa. Jovana je rekla da ne čekamo Petra. | The bus arrived before the car. Jovana said that we shouldn't wait for Petar. |
| baseline | fresh-15-ZH | ZH to EN | 1,159 | 「別急」不是「快一點」。梅琳明天下午三點到，不是今天。 | "Don't rush" is not "hurry up." Melin will arrive tomorrow afternoon at 3 PM, not today. |
| fidelity-amendment | fratello-full | EN to IT | 1,101 | Thank you fratello! 🤗<br>Things are already heating up, they are placing bets like crazy 😅 | Grazie fratello! 🤗<br>Le cose si stanno già scaldando, stanno scommettendo come matti 😅 |
| fidelity-amendment | fratello-full | EN to DE | 1,078 | Thank you fratello! 🤗<br>Things are already heating up, they are placing bets like crazy 😅 | Vielen Dank, fratello! 🤗<br>Die Dinge werden schon heiß, sie wetten wie verrückt 😅 |
| fidelity-amendment | ma-cherie | EN to DE | 0,569 | Ma cherie! How are you doing? | Meine Liebste! Wie geht es dir? |
| fidelity-amendment | ma-cherie | EN to FR | 0,644 | Ma cherie! How are you doing? | Ma chérie ! Comment vas-tu ? |
| fidelity-amendment | ma-cherie | EN to EL | 0,691 | Ma cherie! How are you doing? | Αγάπη μου! Πώς είσαι; |
| fidelity-amendment | hermano | EN to DE | 0,688 | Thanks hermano, this is getting wild already 😂 | Danke, Bruder, das wird schon verrückt 😂. |
| fidelity-amendment | pt-intro-short | EN to PT | 0,669 | caralho, this is getting wild already 😂 | caralho, isso já está ficando louco 😂 |
| fidelity-amendment | pt-intro-long | EN to PT | 1,176 | foda se, caralho. Things are already heating up and people are placing bets like crazy 😅 | foda-se, caralho. As coisas já estão esquentando e as pessoas estão fazendo apostas como loucas 😅 |
| fidelity-amendment | el-intro-queue | EN to EL | 0,987 | μαλακα, this queue is moving fast tonight 😂 | μαλακία, αυτή η ουρά κινείται γρήγορα απόψε 😂 |
| fidelity-amendment | mixed-profanity-list |  to EN | 0,697 | foda se, caralho, μαλακα, γαμήσου | fuck it, you asshole, you bastard, fuck you. |
| fidelity-amendment | mixed-profanity-list | PT to EN | 0,764 | foda se, caralho, μαλακα, γαμήσου | fuck it, you asshole, you fucking bitch, go fuck yourself. |
| fidelity-amendment | el-profanity-sentence | EL to EN | 0,714 | μαλακα, γαμήσου και άσε μας ήσυχους | You fucking idiot, go fuck yourself and leave us alone. |
| fidelity-amendment | pt-profanity-pair | PT to EN | 0,537 | foda se, caralho | fuck it, you bastard. |
| fidelity-amendment | pt-exclamation | PT to EN | 0,308 | caralho! | fuck! |
| fidelity-amendment | el-intro-question | EN to EL | 0,711 | μαλακα, how are you doing tonight? | μαλακά, πώς είσαι απόψε; |
| fidelity-amendment | tasos-italian | IT to EN | 0,674 | Si Tasos , sanno essere divertenti | Yes, Tasos, they can be fun. |
| fidelity-amendment | mates-monolingual | EN to DE | 0,755 | I'm glad you all are here mates | Ich bin froh, dass ihr alle hier seid, Leute. |
| fidelity-amendment | greek-name-monolingual | EL to EN | 0,717 | Μαλάκα Φλοετ τελικά είσαι πολύ αστείος | You fucking Floet, you're really hilarious. |
| fidelity-amendment | fratello-shortened | EN to DE | 0,707 | Thank you fratello! Things are already heating up | Vielen Dank, Bruder! Die Dinge werden schon heiß. |
| fidelity-amendment | fratello-question | EN to DE | 0,678 | Thank you fratello! How are you doing? | Vielen Dank, Bruder! Wie geht es dir? |
| fidelity-amendment | hello-control | EN to DE | 0,340 | Hello everybody | Hallo zusammen. |
| fidelity-amendment | heldout-DE | DE to EN | 1,154 | Verdammt, Alex! Der Zug fährt erst um 18:30.<br>**Warte auf mich!** 😂 | Damn, Alex! The train doesn't leave until 6:30 PM.<br>**Wait for me!** 😂 |
| fidelity-amendment | heldout-EL | EL to EN | 1,080 | Γαμώτο, Άλεξ! Το τρένο φεύγει στις 18:30.<br>**Περίμενέ με!** 😂 | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| fidelity-amendment | heldout-EN | EN to SR | 1,181 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Jebo ga, Alex! Voz polazi u 18:30.<br>**Čekaj me!** 😂 |
| fidelity-amendment | heldout-IT | IT to EN | 1,231 | Cazzo, Alex! Il treno parte alle 18:30.<br>**Aspettami!** 😂 | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| fidelity-amendment | heldout-PT | PT to EN | 1,138 | Porra, Alex! O comboio parte às 18:30.<br>**Espera por mim!** 😂 | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| fidelity-amendment | heldout-ES | ES to EN | 1,096 | ¡Joder, Alex! El tren sale a las 18:30.<br>**Espérame!** 😂 | Damn, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| fidelity-amendment | heldout-SR | SR to EN | 1,184 | Јеботе, Алекс! Воз полази у 18:30.<br>**Сачекај ме!** 😂 | Fucking hell, Alex! The bus leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| fidelity-amendment | heldout-ZH | ZH to EN | 1,169 | 妈的，Alex！火车18:30发车。<br>**等我！** 😂 | Fucking hell, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| fidelity-amendment | heldout-SR-Latn | SR to EN | 1,111 | Jebote, Aleks! Voz polazi u 18:30.<br>**Sačekaj me!** 😂 | Damn, Aleks! The bus leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| fidelity-amendment | heldout-ZH-Hant | ZH to EN | 1,242 | 媽的，Alex！火車18:30發車。<br>**等我！** 😂 | Fucking hell, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| fidelity-amendment | heldout-EN-to-DE | EN to DE | 1,243 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Verdammt, Alex! Der Zug fährt um 18:30 ab.<br>**Warte auf mich!** 😂 |
| fidelity-amendment | heldout-EN-to-EL | EN to EL | 1,480 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Άμάν, Αλέξ! Το τρένο φεύγει στις 18:30.<br>**Περίμενέ με!** 😂 |
| fidelity-amendment | heldout-EN-to-IT | EN to IT | 1,216 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Maledizione, Alex! Il treno parte alle 18:30.<br>**Aspettami!** 😂 |
| fidelity-amendment | heldout-EN-to-PT | EN to PT | 1,186 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Droga, Alex! O trem sai às 18:30.<br>**Espere por mim!** 😂 |
| fidelity-amendment | heldout-EN-to-ES | EN to ES | 1,328 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | ¡Maldita sea, Alex! El tren sale a las 18:30.<br>**¡Espera a mí!** 😂 |
| fidelity-amendment | heldout-EN-to-ZH | EN to ZH | 1,147 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | 该死，亚历克斯！火车 18:30 开车。<br>**等我！** 😂 |
| fidelity-amendment | heldout-EN-to-SR-LATN | EN to SR-LATN | 1,192 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Jebo ga, Alex! Voz polazi u 18:30.<br>**Čekaj me!** 😂 |
| fidelity-amendment | heldout-EN-to-SR-CYRL | EN to SR-CYRL | 1,291 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Јеба, Алекс! Возот полази у 18:30.<br>**Чекај ме!** 😂 |
| fidelity-amendment | heldout-EN-to-ZH-HANT | EN to ZH-HANT | 1,220 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | 該死，亞歷克斯！ 火車在 18:30 出發。<br>**等我！** 😂 |
| fidelity-amendment | heldout-EN-to-PT-BR | EN to PT-BR | 1,186 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Droga, Alex! O trem sai às 18:30.<br>**Espere por mim!** 😂 |
| fidelity-amendment | heldout-EN-to-PT-PT | EN to PT-PT | 1,211 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Caralho, Alex! O comboio parte às 18:30.<br>**Espera por mim!** 😂 |
| fidelity-amendment | fresh-00-DE | DE to EN | 1,478 | Scheiße! Nicht den Bus nehmen — die Fähre fährt morgen um 07:15.<br>**Mara wartet nicht auf uns.** 😅 | Shit! Don't take the bus — the ferry leaves tomorrow at 07:15.<br>**Mara isn't waiting for us.** 😅 |
| fidelity-amendment | fresh-01-EL | EL to EN | 1,340 | Γαμώτο! Δεν πλήρωσα 50 ευρώ, πλήρωσα 15.<br>**Ρώτα τη Νίκη αύριο.** 😅 | Fucking hell! I didn't pay 50 euros, I paid 15.<br>**Ask Niki tomorrow.** 😅 |
| fidelity-amendment | fresh-02-EN | EN to DE | 1,432 | Shit! Ada did not cancel the meeting; she moved it to Friday.<br>**Keep the receipt.** 😅 | Scheiße! Ada hat das Meeting nicht abgesagt; sie hat es auf Freitag verschoben.<br>**Bewahren Sie den Beleg auf.** 😅 |
| fidelity-amendment | fresh-03-IT | IT to EN | 1,296 | Cazzo! Si Luca, abbiamo ordinato due sedie, non tre tavoli.<br>**Non chiamare ancora.** 😅 | Fuck! Yes, it's Luca, we ordered two chairs, not three tables.<br>**Don't call yet.** 😅 |
| fidelity-amendment | fresh-04-PT | PT to EN | 1,324 | Porra! A Rita não perdeu a chave; deixou-a na gaveta azul.<br>**Não abras a porta.** 😅 | Fuck! Rita didn't lose the key; she left it in the blue drawer.<br>**Don't open the door.** 😅 |
| fidelity-amendment | fresh-05-ES | ES to EN | 1,049 | ¡Joder! Clara llega el jueves, no el martes.<br>**Guarda el número 47.** 😅 | Damn! Clara arrives on Thursday, not on Tuesday.<br>**Keep number 47.** 😅 |
| fidelity-amendment | fresh-06-SR | SR to EN | 1,233 | Јеботе! Ана није продала бицикл; позајмила га је Милану.<br>**Позови је сутра.** 😅 | Fucking hell! Ana didn't sell the bicycle; she lent it to Milan.<br>**Call her tomorrow.** 😅 |
| fidelity-amendment | fresh-07-ZH | ZH to EN | 1,385 | 妈的！小林没丢钱包，他把钱包放在绿色盒子里了。<br>**别打电话。** 😅 | Fucking hell! Xiaolin didn't lose his wallet; he put his wallet in the green box.<br>**Don't call.** 😅 |
| fidelity-amendment | fresh-08-DE | DE to EN | 1,440 | Er sagte: "Bitte nicht löschen." Der Link bleibt https://example.com/a?q=7 und der Code `x != 3`. | He said: "Please do not delete." The link remains https://example.com/a?q=7 and the code is `x != 3`. |
| fidelity-amendment | fresh-09-EL | EL to EN | 1,424 | Η Ίριδα είπε «όχι ακόμα». Το λεωφορείο φεύγει στις 21:40, το πλοίο στις 22:10. | Irida said, "Not yet." The bus leaves at 9:40 PM, and the ferry leaves at 10:10 PM. |
| fidelity-amendment | fresh-10-EN | EN to EL | 2,075 | My friend said "no thanks", not "yes please". The password example is `red_blue`, not a real credential. | Ο φίλος μου είπε "όχι, ευχαριστώ", όχι "ναι, παρακαλώ". Το παράδειγμα κωδικού πρόσβασης είναι `red_blue`, όχι ένα πραγματικό διαπιστευτήριο. |
| fidelity-amendment | fresh-11-IT | IT to EN | 1,057 | Si Marta, sono loro che devono aspettare noi, non il contrario. | Yes, Marta, it's them who have to wait for us, not the other way around. |
| fidelity-amendment | fresh-12-PT | PT to EN | 0,958 | O autocarro está atrasado, mas o comboio chegou a horas. A Joana não está zangada. | The bus is late, but the train arrived on time. Joana is not angry. |
| fidelity-amendment | fresh-13-ES | ES to EN | 1,259 | No es una amenaza: "vete a la mierda" es la frase que quiero citar, sin dirigirla a nadie aquí. | It's not a threat: "go to hell" is the phrase I want to quote, without directing it at anyone here. |
| fidelity-amendment | fresh-14-SR | SR to EN | 1,023 | Voz je stigao pre autobusa. Jovana je rekla da ne čekamo Petra. | The train arrived before the bus. Jovana said that we shouldn't wait for Petar. |
| fidelity-amendment | fresh-15-ZH | ZH to EN | 1,201 | 「別急」不是「快一點」。梅琳明天下午三點到，不是今天。 | "别急" does not mean "hurry up." Melin will arrive tomorrow afternoon at 3 PM, not today. |
| concise-language-aware | fratello-full | EN to IT | 1,063 | Thank you fratello! 🤗<br>Things are already heating up, they are placing bets like crazy 😅 | Grazie, fratello! 🤗<br>Le cose si stanno già scaldando, stanno scommettendo come dei pazzi 😅 |
| concise-language-aware | fratello-full | EN to DE | 0,984 | Thank you fratello! 🤗<br>Things are already heating up, they are placing bets like crazy 😅 | Vielen Dank, fratello! 🤗<br>Die Dinge werden schon jetzt spannend, sie wetten wie verrückt 😅 |
| concise-language-aware | ma-cherie | EN to DE | 0,490 | Ma cherie! How are you doing? | Meine Liebste! Wie geht es dir? |
| concise-language-aware | ma-cherie | EN to FR | 0,504 | Ma cherie! How are you doing? | Ma chérie ! Comment vas-tu ? |
| concise-language-aware | ma-cherie | EN to EL | 0,574 | Ma cherie! How are you doing? | Αγάπη μου! Πώς είσαι; |
| concise-language-aware | hermano | EN to DE | 0,587 | Thanks hermano, this is getting wild already 😂 | Danke, Bruder, das wird schon jetzt verrückt 😂 |
| concise-language-aware | pt-intro-short | EN to PT | 0,540 | caralho, this is getting wild already 😂 | caralho, isso já está ficando louco 😂 |
| concise-language-aware | pt-intro-long | EN to PT | 1,055 | foda se, caralho. Things are already heating up and people are placing bets like crazy 😅 | foda-se, caralho. As coisas já estão ficando tensas e as pessoas estão apostando loucamente 😅 |
| concise-language-aware | el-intro-queue | EN to EL | 0,843 | μαλακα, this queue is moving fast tonight 😂 | μαλακία, αυτή η ουρά κινείται γρήγορα απόψε 😂 |
| concise-language-aware | mixed-profanity-list |  to EN | 0,741 | foda se, caralho, μαλακα, γαμήσου | Fuck it, you bastard, you son of a bitch, go fuck yourself. |
| concise-language-aware | mixed-profanity-list | PT to EN | 0,680 | foda se, caralho, μαλακα, γαμήσου | fuck it, you bastard, you fucking idiot, go fuck yourself. |
| concise-language-aware | el-profanity-sentence | EL to EN | 0,683 | μαλακα, γαμήσου και άσε μας ήσυχους | You fucking idiot, go fuck yourself and leave us the hell alone. |
| concise-language-aware | pt-profanity-pair | PT to EN | 0,421 | foda se, caralho | fuck it, you bastard. |
| concise-language-aware | pt-exclamation | PT to EN | 0,271 | caralho! | fuck! |
| concise-language-aware | el-intro-question | EN to EL | 0,631 | μαλακα, how are you doing tonight? | μαλακά, πώς είσαι απόψε; |
| concise-language-aware | tasos-italian | IT to EN | 0,474 | Si Tasos , sanno essere divertenti | Yes, Tasos can be fun. |
| concise-language-aware | mates-monolingual | EN to DE | 0,649 | I'm glad you all are here mates | Ich freue mich, dass ihr alle hier seid, Leute. |
| concise-language-aware | greek-name-monolingual | EL to EN | 0,586 | Μαλάκα Φλοετ τελικά είσαι πολύ αστείος | You fucking Floet, you are ultimately very funny. |
| concise-language-aware | fratello-shortened | EN to DE | 0,653 | Thank you fratello! Things are already heating up | Vielen Dank, fratello! Die Dinge werden schon jetzt spannend. |
| concise-language-aware | fratello-question | EN to DE | 0,552 | Thank you fratello! How are you doing? | Vielen Dank, fratello! Wie geht es dir? |
| concise-language-aware | hello-control | EN to DE | 0,326 | Hello everybody | Hallo zusammen. |
| concise-language-aware | heldout-DE | DE to EN | 1,074 | Verdammt, Alex! Der Zug fährt erst um 18:30.<br>**Warte auf mich!** 😂 | Damn, Alex! The train doesn't leave until 6:30 PM.<br>**Wait for me!** 😂 |
| concise-language-aware | heldout-EL | EL to EN | 1,010 | Γαμώτο, Άλεξ! Το τρένο φεύγει στις 18:30.<br>**Περίμενέ με!** 😂 | Damn it, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| concise-language-aware | heldout-EN | EN to SR | 1,066 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Jebo ga, Alex! Voz polazi u 18:30.<br>**Čekaj me!** 😂 |
| concise-language-aware | heldout-IT | IT to EN | 1,019 | Cazzo, Alex! Il treno parte alle 18:30.<br>**Aspettami!** 😂 | Damn, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| concise-language-aware | heldout-PT | PT to EN | 0,987 | Porra, Alex! O comboio parte às 18:30.<br>**Espera por mim!** 😂 | Damn, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| concise-language-aware | heldout-ES | ES to EN | 0,964 | ¡Joder, Alex! El tren sale a las 18:30.<br>**Espérame!** 😂 | Damn, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| concise-language-aware | heldout-SR | SR to EN | 0,998 | Јеботе, Алекс! Воз полази у 18:30.<br>**Сачекај ме!** 😂 | Fucking hell, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| concise-language-aware | heldout-ZH | ZH to EN | 1,011 | 妈的，Alex！火车18:30发车。<br>**等我！** 😂 | Damn it, Alex! The train departs at 6:30 PM.<br>**Wait for me!** 😂 |
| concise-language-aware | heldout-SR-Latn | SR to EN | 0,999 | Jebote, Aleks! Voz polazi u 18:30.<br>**Sačekaj me!** 😂 | Damn, Aleks! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| concise-language-aware | heldout-ZH-Hant | ZH to EN | 1,014 | 媽的，Alex！火車18:30發車。<br>**等我！** 😂 | Damn it, Alex! The train departs at 6:30 PM.<br>**Wait for me!** 😂 |
| concise-language-aware | heldout-EN-to-DE | EN to DE | 1,118 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Verdammt, Alex! Der Zug fährt um 18:30 ab.<br>**Warte auf mich!** 😂 |
| concise-language-aware | heldout-EN-to-EL | EN to EL | 1,295 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Άμάν, Alex! Το τρένο αναχωρεί στις 18:30.<br>**Περίμενέ με!** 😂 |
| concise-language-aware | heldout-EN-to-IT | EN to IT | 1,108 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Porca miseria, Alex! Il treno parte alle 18:30.<br>**Aspettami!** 😂 |
| concise-language-aware | heldout-EN-to-PT | EN to PT | 1,043 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Droga, Alex! O trem parte às 18:30.<br>**Espere por mim!** 😂 |
| concise-language-aware | heldout-EN-to-ES | EN to ES | 1,203 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | ¡Maldita sea, Alex! El tren sale a las 18:30.<br>**¡Espera a mí!** 😂 |
| concise-language-aware | heldout-EN-to-ZH | EN to ZH | 1,051 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | 他妈的，亚历克斯！火车 18:30 开车。<br>**等我！** 😂 |
| concise-language-aware | heldout-EN-to-SR-LATN | EN to SR-LATN | 1,044 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Jebo ga, Alex! Voz polazi u 18:30.<br>**Čekaj me!** 😂 |
| concise-language-aware | heldout-EN-to-SR-CYRL | EN to SR-CYRL | 1,103 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Јеба, Алекс! Воз полази у 18:30.<br>**Čekaj me!** 😂 |
| concise-language-aware | heldout-EN-to-ZH-HANT | EN to ZH-HANT | 1,078 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | 該死，亞歷克斯！ 火車在 18:30 發車。<br>**等我！** 😂 |
| concise-language-aware | heldout-EN-to-PT-BR | EN to PT-BR | 1,005 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Caralho, Alex! O trem parte às 18:30.<br>**Espere por mim!** 😂 |
| concise-language-aware | heldout-EN-to-PT-PT | EN to PT-PT | 1,036 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Caralho, Alex! O comboio parte às 18:30.<br>**Espera por mim!** 😂 |
| concise-language-aware | fresh-00-DE | DE to EN | 1,364 | Scheiße! Nicht den Bus nehmen — die Fähre fährt morgen um 07:15.<br>**Mara wartet nicht auf uns.** 😅 | Shit! Don't take the bus — the ferry leaves tomorrow at 07:15.<br>**Mara is not waiting for us.** 😅 |
| concise-language-aware | fresh-01-EL | EL to EN | 1,207 | Γαμώτο! Δεν πλήρωσα 50 ευρώ, πλήρωσα 15.<br>**Ρώτα τη Νίκη αύριο.** 😅 | Fucking hell! I didn't pay 50 euros, I paid 15.<br>**Ask Niki tomorrow.** 😅 |
| concise-language-aware | fresh-02-EN | EN to DE | 1,309 | Shit! Ada did not cancel the meeting; she moved it to Friday.<br>**Keep the receipt.** 😅 | Scheiße! Ada hat das Meeting nicht abgesagt; sie hat es auf Freitag verschoben.<br>**Bewahren Sie den Beleg auf.** 😅 |
| concise-language-aware | fresh-03-IT | IT to EN | 1,129 | Cazzo! Si Luca, abbiamo ordinato due sedie, non tre tavoli.<br>**Non chiamare ancora.** 😅 | Damn it! It's Luca, we ordered two chairs, not three tables.<br>**Don't call yet.** 😅 |
| concise-language-aware | fresh-04-PT | PT to EN | 1,190 | Porra! A Rita não perdeu a chave; deixou-a na gaveta azul.<br>**Não abras a porta.** 😅 | Damn! Rita didn't lose the key; she left it in the blue drawer.<br>**Don't open the door.** 😅 |
| concise-language-aware | fresh-05-ES | ES to EN | 1,003 | ¡Joder! Clara llega el jueves, no el martes.<br>**Guarda el número 47.** 😅 | Damn! Clara arrives on Thursday, not on Tuesday.<br>**Remember to keep number 47.** 😅 |
| concise-language-aware | fresh-06-SR | SR to EN | 1,058 | Јеботе! Ана није продала бицикл; позајмила га је Милану.<br>**Позови је сутра.** 😅 | Fucking hell! Ana didn't sell the bicycle; she lent it to Milan.<br>**Call her tomorrow.** 😅 |
| concise-language-aware | fresh-07-ZH | ZH to EN | 1,218 | 妈的！小林没丢钱包，他把钱包放在绿色盒子里了。<br>**别打电话。** 😅 | Damn it! Xiaolin didn't lose his wallet; he put his wallet in the green box.<br>**Don't call.** 😅 |
| concise-language-aware | fresh-08-DE | DE to EN | 1,329 | Er sagte: "Bitte nicht löschen." Der Link bleibt https://example.com/a?q=7 und der Code `x != 3`. | He said: "Please do not delete." The link remains https://example.com/a?q=7 and the code is `x != 3`. |
| concise-language-aware | fresh-09-EL | EL to EN | 1,305 | Η Ίριδα είπε «όχι ακόμα». Το λεωφορείο φεύγει στις 21:40, το πλοίο στις 22:10. | Irida said, "Not yet." The bus leaves at 9:40 PM, and the ferry leaves at 10:10 PM. |
| concise-language-aware | fresh-10-EN | EN to EL | 1,964 | My friend said "no thanks", not "yes please". The password example is `red_blue`, not a real credential. | Ο φίλος μου είπε "όχι, ευχαριστώ", όχι "ναι, παρακαλώ". Το παράδειγμα κωδικού πρόσβασης είναι `red_blue`, και όχι ένα πραγματικό διαπιστευτήριο. |
| concise-language-aware | fresh-11-IT | IT to EN | 1,131 | Si Marta, sono loro che devono aspettare noi, non il contrario. | Yes, it's Marta. It's *they* who have to wait for *us*, not the other way around. |
| concise-language-aware | fresh-12-PT | PT to EN | 0,826 | O autocarro está atrasado, mas o comboio chegou a horas. A Joana não está zangada. | The bus is late, but the train arrived on time. Joana is not angry. |
| concise-language-aware | fresh-13-ES | ES to EN | 1,118 | No es una amenaza: "vete a la mierda" es la frase que quiero citar, sin dirigirla a nadie aquí. | It's not a threat: "go to hell" is the phrase I want to quote, without directing it at anyone here. |
| concise-language-aware | fresh-14-SR | SR to EN | 0,881 | Voz je stigao pre autobusa. Jovana je rekla da ne čekamo Petra. | The train arrived before the bus. Jovana said that we shouldn't wait for Petar. |
| concise-language-aware | fresh-15-ZH | ZH to EN | 1,255 | 「別急」不是「快一點」。梅琳明天下午三點到，不是今天。 | "Don't rush" does not mean "hurry up." Melin will arrive tomorrow afternoon at 3:00 PM, not today. |
| explicit-language-names | fratello-full | EN to IT | 1,372 | Thank you fratello! 🤗<br>Things are already heating up, they are placing bets like crazy 😅 | Grazie fratello! 🤗<br>Le cose si stanno già scaldando, stanno scommettendo come matti 😅 |
| explicit-language-names | fratello-full | EN to DE | 1,050 | Thank you fratello! 🤗<br>Things are already heating up, they are placing bets like crazy 😅 | Vielen Dank, fratello! 🤗<br>Die Dinge werden schon heiß, sie wetten wie verrückt 😅 |
| explicit-language-names | ma-cherie | EN to DE | 0,515 | Ma cherie! How are you doing? | Meine Liebste! Wie geht es dir? |
| explicit-language-names | ma-cherie | EN to FR | 0,577 | Ma cherie! How are you doing? | Ma chérie ! Comment vas-tu ? |
| explicit-language-names | ma-cherie | EN to EL | 0,643 | Ma cherie! How are you doing? | Αγάπη μου! Πώς είσαι; |
| explicit-language-names | hermano | EN to DE | 0,616 | Thanks hermano, this is getting wild already 😂 | Danke, Bruder, das wird schon verrückt 😂. |
| explicit-language-names | pt-intro-short | EN to PT | 0,603 | caralho, this is getting wild already 😂 | caralho, isso já está ficando louco 😂 |
| explicit-language-names | pt-intro-long | EN to PT | 1,107 | foda se, caralho. Things are already heating up and people are placing bets like crazy 😅 | foda-se, caralho. As coisas já estão esquentando e as pessoas estão fazendo apostas como loucas 😅 |
| explicit-language-names | el-intro-queue | EN to EL | 0,899 | μαλακα, this queue is moving fast tonight 😂 | μαλακία, αυτή η ουρά κινείται γρήγορα απόψε 😂 |
| explicit-language-names | mixed-profanity-list |  to EN | 0,657 | foda se, caralho, μαλακα, γαμήσου | fuck it, asshole, you fucking bitch, get fucked. |
| explicit-language-names | mixed-profanity-list | PT to EN | 0,686 | foda se, caralho, μαλακα, γαμήσου | fuck it, you fucking asshole, you fucking bitch, get fucked. |
| explicit-language-names | el-profanity-sentence | EL to EN | 0,620 | μαλακα, γαμήσου και άσε μας ήσυχους | fuck, go fuck yourself and leave us alone. |
| explicit-language-names | pt-profanity-pair | PT to EN | 0,440 | foda se, caralho | fuck it, you bastard |
| explicit-language-names | pt-exclamation | PT to EN | 0,278 | caralho! | fuck! |
| explicit-language-names | el-intro-question | EN to EL | 0,665 | μαλακα, how are you doing tonight? | μαλακά, πώς είσαι απόψε; |
| explicit-language-names | tasos-italian | IT to EN | 0,654 | Si Tasos , sanno essere divertenti | Yes, Tasos, they can be fun. |
| explicit-language-names | mates-monolingual | EN to DE | 0,693 | I'm glad you all are here mates | Ich bin froh, dass ihr alle hier seid, Leute. |
| explicit-language-names | greek-name-monolingual | EL to EN | 0,675 | Μαλάκα Φλοετ τελικά είσαι πολύ αστείος | You fucking Floet, you're actually very funny. |
| explicit-language-names | fratello-shortened | EN to DE | 0,583 | Thank you fratello! Things are already heating up | Danke, fratello! Die Dinge werden schon heiß. |
| explicit-language-names | fratello-question | EN to DE | 0,573 | Thank you fratello! How are you doing? | Vielen Dank, fratello! Wie geht es dir? |
| explicit-language-names | hello-control | EN to DE | 0,306 | Hello everybody | Hallo zusammen. |
| explicit-language-names | heldout-DE | DE to EN | 1,158 | Verdammt, Alex! Der Zug fährt erst um 18:30.<br>**Warte auf mich!** 😂 | Damn, Alex! The train doesn't leave until 6:30 PM.<br>**Wait for me!** 😂 |
| explicit-language-names | heldout-EL | EL to EN | 1,048 | Γαμώτο, Άλεξ! Το τρένο φεύγει στις 18:30.<br>**Περίμενέ με!** 😂 | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| explicit-language-names | heldout-EN | EN to SR | 1,140 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Jebo ga, Alex! Voz polazi u 18:30.<br>**Čekaj me!** 😂 |
| explicit-language-names | heldout-IT | IT to EN | 1,121 | Cazzo, Alex! Il treno parte alle 18:30.<br>**Aspettami!** 😂 | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| explicit-language-names | heldout-PT | PT to EN | 1,060 | Porra, Alex! O comboio parte às 18:30.<br>**Espera por mim!** 😂 | Fuck, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| explicit-language-names | heldout-ES | ES to EN | 1,069 | ¡Joder, Alex! El tren sale a las 18:30.<br>**Espérame!** 😂 | Damn, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| explicit-language-names | heldout-SR | SR to EN | 1,104 | Јеботе, Алекс! Воз полази у 18:30.<br>**Сачекај ме!** 😂 | Fucking hell, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| explicit-language-names | heldout-ZH | ZH to EN | 1,106 | 妈的，Alex！火车18:30发车。<br>**等我！** 😂 | Fucking hell, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| explicit-language-names | heldout-SR-Latn | SR to EN | 1,027 | Jebote, Aleks! Voz polazi u 18:30.<br>**Sačekaj me!** 😂 | Damn, Aleks! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| explicit-language-names | heldout-ZH-Hant | ZH to EN | 1,091 | 媽的，Alex！火車18:30發車。<br>**等我！** 😂 | Fucking hell, Alex! The train leaves at 6:30 PM.<br>**Wait for me!** 😂 |
| explicit-language-names | heldout-EN-to-DE | EN to DE | 1,177 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Verdammt, Alex! Der Zug fährt um 18:30 ab.<br>**Warte auf mich!** 😂 |
| explicit-language-names | heldout-EN-to-EL | EN to EL | 1,402 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Άμάν, Αλέξ! Το τρένο φεύγει στις 18:30.<br>**Περίμενέ με!** 😂 |
| explicit-language-names | heldout-EN-to-IT | EN to IT | 1,126 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Accidenti, Alex! Il treno parte alle 18:30.<br>**Aspettami!** 😂 |
| explicit-language-names | heldout-EN-to-PT | EN to PT | 1,098 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Droga, Alex! O trem sai às 18:30.<br>**Espere por mim!** 😂 |
| explicit-language-names | heldout-EN-to-ES | EN to ES | 1,301 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | ¡Maldita sea, Alex! El tren sale a las 18:30.<br>**¡Espera a mí!** 😂 |
| explicit-language-names | heldout-EN-to-ZH | EN to ZH | 1,086 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | 该死，亚历克斯！火车 18:30 开车。<br>**等我！** 😂 |
| explicit-language-names | heldout-EN-to-SR-LATN | EN to SR-LATN | 1,136 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Jebo ga, Alex! Voz polazi u 18:30.<br>**Čekaj me!** 😂 |
| explicit-language-names | heldout-EN-to-SR-CYRL | EN to SR-CYRL | 1,239 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Драј, Алекс! Возови полази у 18:30.<br>**Чекај ме!** 😂 |
| explicit-language-names | heldout-EN-to-ZH-HANT | EN to ZH-HANT | 1,161 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | 該死，亞歷克斯！ 火車在 18:30 出發。<br>**等我！** 😂 |
| explicit-language-names | heldout-EN-to-PT-BR | EN to PT-BR | 1,121 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Droga, Alex! O trem sai às 18:30.<br>**Espere por mim!** 😂 |
| explicit-language-names | heldout-EN-to-PT-PT | EN to PT-PT | 1,157 | Damn it, Alex! The train leaves at 18:30.<br>**Wait for me!** 😂 | Caralho, Alex! O comboio parte às 18:30.<br>**Espera por mim!** 😂 |
| explicit-language-names | fresh-00-DE | DE to EN | 1,425 | Scheiße! Nicht den Bus nehmen — die Fähre fährt morgen um 07:15.<br>**Mara wartet nicht auf uns.** 😅 | Shit! Don't take the bus — the ferry leaves tomorrow at 07:15.<br>**Mara isn't waiting for us.** 😅 |
| explicit-language-names | fresh-01-EL | EL to EN | 1,169 | Γαμώτο! Δεν πλήρωσα 50 ευρώ, πλήρωσα 15.<br>**Ρώτα τη Νίκη αύριο.** 😅 | Fuck! I didn't pay 50 euros, I paid 15.<br>**Ask Niki tomorrow.** 😅 |
| explicit-language-names | fresh-02-EN | EN to DE | 1,268 | Shit! Ada did not cancel the meeting; she moved it to Friday.<br>**Keep the receipt.** 😅 | Scheiße! Ada hat das Meeting nicht abgesagt; sie hat es auf Freitag verschoben.<br>**Behalte den Beleg.** 😅 |
| explicit-language-names | fresh-03-IT | IT to EN | 1,232 | Cazzo! Si Luca, abbiamo ordinato due sedie, non tre tavoli.<br>**Non chiamare ancora.** 😅 | Fuck! Yes, it's Luca, we ordered two chairs, not three tables.<br>**Don't call yet.** 😅 |
| explicit-language-names | fresh-04-PT | PT to EN | 1,255 | Porra! A Rita não perdeu a chave; deixou-a na gaveta azul.<br>**Não abras a porta.** 😅 | Fuck! Rita didn't lose the key; she left it in the blue drawer.<br>**Don't open the door.** 😅 |
| explicit-language-names | fresh-05-ES | ES to EN | 0,973 | ¡Joder! Clara llega el jueves, no el martes.<br>**Guarda el número 47.** 😅 | Damn! Clara arrives on Thursday, not on Tuesday.<br>**Keep number 47.** 😅 |
| explicit-language-names | fresh-06-SR | SR to EN | 1,128 | Јеботе! Ана није продала бицикл; позајмила га је Милану.<br>**Позови је сутра.** 😅 | Fucking hell! Ana didn't sell the bicycle; she lent it to Milan.<br>**Call her tomorrow.** 😅 |
| explicit-language-names | fresh-07-ZH | ZH to EN | 1,309 | 妈的！小林没丢钱包，他把钱包放在绿色盒子里了。<br>**别打电话。** 😅 | Fucking hell! Xiaolin didn't lose his wallet; he put his wallet in the green box.<br>**Don't call.** 😅 |
| explicit-language-names | fresh-08-DE | DE to EN | 1,357 | Er sagte: "Bitte nicht löschen." Der Link bleibt https://example.com/a?q=7 und der Code `x != 3`. | He said: "Please do not delete." The link remains https://example.com/a?q=7 and the code is `x != 3`. |
| explicit-language-names | fresh-09-EL | EL to EN | 1,369 | Η Ίριδα είπε «όχι ακόμα». Το λεωφορείο φεύγει στις 21:40, το πλοίο στις 22:10. | Irida said, "Not yet." The bus leaves at 9:40 PM, and the ferry leaves at 10:10 PM. |
| explicit-language-names | fresh-10-EN | EN to EL | 2,007 | My friend said "no thanks", not "yes please". The password example is `red_blue`, not a real credential. | Ο φίλος μου είπε "όχι, ευχαριστώ", όχι "ναι, παρακαλώ". Το παράδειγμα κωδικού πρόσβασης είναι `red_blue`, όχι ένα πραγματικό διαπιστευτήριο. |
| explicit-language-names | fresh-11-IT | IT to EN | 0,995 | Si Marta, sono loro che devono aspettare noi, non il contrario. | Yes, Marta, it's them who have to wait for us, not the other way around. |
| explicit-language-names | fresh-12-PT | PT to EN | 0,880 | O autocarro está atrasado, mas o comboio chegou a horas. A Joana não está zangada. | The bus is late, but the train arrived on time. Joana is not angry. |
| explicit-language-names | fresh-13-ES | ES to EN | 1,179 | No es una amenaza: "vete a la mierda" es la frase que quiero citar, sin dirigirla a nadie aquí. | It's not a threat: "go to hell" is the phrase I want to quote, without directing it at anyone here. |
| explicit-language-names | fresh-14-SR | SR to EN | 0,945 | Voz je stigao pre autobusa. Jovana je rekla da ne čekamo Petra. | The train arrived before the bus. Jovana said that we shouldn't wait for Petar. |
| explicit-language-names | fresh-15-ZH | ZH to EN | 1,202 | 「別急」不是「快一點」。梅琳明天下午三點到，不是今天。 | "Don't rush" does not mean "hurry up." Melin will arrive tomorrow afternoon at 3 PM, not today. |
