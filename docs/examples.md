# Discord Message Formatting & Live Examples

This guide displays visual representations of exactly how mirrored and translated messages appear in Discord chat interface layout for all routing workflows.

---

## 🎨 Visual Layout System

Every synchronized message dispatched by Tsumari uses clean formatting to represent the original sender without requiring webhook allocations, keeping RAM footprint extremely light:
1.  **Bold Author Header:** The sender's name is highlighted in bold: `**Username**`.
2.  **Interactive View Context Button:** An interactive Link-style button labeled verbatim as `View Context` is appended to the bottom of the dispatch. Clicking this jumps users directly to the original conversation source.
3.  **Expiring Media Assets:** Original file attachments are dynamically re-uploaded as native Discord uploads in target rooms alongside the button.

---

## 📖 Live Scenarios and Chat Simulations

### Scenario 1: Message in Master Channel (`#general`)
*Sender: `gpowe` types in English:*
> "Let's review the code deployment schedules for this evening."

#### 1. Master Channel (`#general`):
The user's original message is left untouched by the bot:
> **gpowe**: Let's review the code deployment schedules for this evening.

#### 2. Localized Greek Channel (`#general-greek`):
DeepL translates the English text into Greek:
> **gpowe** (Translated to EL):
> Ας αναθεωρήσουμε τα προγράμματα ανάπτυξης κώδικα για απόψε.
> 
> `[Button: View Context]`

#### 3. Localized Italian Channel (`#general-italian`):
DeepL translates the English text into Italian:
> **gpowe** (Translated to IT):
> Rivediamo i programmi di implementazione del codice per questa sera.
> 
> `[Button: View Context]`

---

### Scenario 2: Correct Language in Localized Channel (Match Flow)
*Sender: `nikos` types Greek in `#general-greek` (Target: Greek):*
> "Η δοκιμή ολοκληρώθηκε με επιτυχία!"

#### 1. Greek Local Channel (`#general-greek`):
The user's original Greek message is left untouched:
> **nikos**: Η δοκιμή ολοκληρώθηκε με επιτυχία!

#### 2. Master Channel (`#general`):
The bot broadcasts the original untouched Greek payload:
> **nikos**:
> Η δοκιμή ολοκληρώθηκε με επιτυχία!
> 
> `[Button: View Context]`

#### 3. English Local Sibling (`#general-english`):
DeepL translates the Greek text into English and disperses:
> **nikos** (Translated to EN-US):
> The test has been completed successfully!
> 
> `[Button: View Context]`

---

### Scenario 3: Incorrect Language in Localized Channel (Mismatch Flow)
*Sender: `gpowe` mistakenly types English inside the Greek local room `#general-greek`:*
> "Can we finalize the database parameters before lunch?"

#### 1. Greek Local Channel (`#general-greek`):
Tsumari **does not delete** the user's message. It posts an inline reply translation in Greek:
> 💬 **gpowe**: Can we finalize the database parameters before lunch?
> ↳ 🤖 **Tsumari** (Bot): *Translation (EL):* Μπορούμε να οριστικοποιήσουμε τις παραμέτρους της βάσης δεδομένων πριν από το μεσημεριανό γεύμα;

#### 2. Master Channel (`#general`):
Tsumari forwards the original raw English text:
> **gpowe**:
> Can we finalize the database parameters before lunch?
> 
> `[Button: View Context]`

#### 3. English Local Sibling (`#general-english`):
Since the original message was in English, Tsumari forwards it raw directly to its home room:
> **gpowe**:
> Can we finalize the database parameters before lunch?
> 
> `[Button: View Context]`

#### 4. Italian Local Sibling (`#general-italian`):
DeepL translates the original English text into Italian and disperses:
> **gpowe** (Translated to IT):
> Possiamo definire i parametri del database prima di pranzo?
> 
> `[Button: View Context]`
