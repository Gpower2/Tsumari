# Discord Message Examples

These examples reflect the **current** message formatting, button labels, and edit-sync behavior in the code.

## Formatting Rules

### Raw Copies

```text
**Author**:
message text
```

### Translated Copies

```text
**Author** (XX => YY):
translated text
```

### Initial Mismatch Reply in a Localized Channel

```text
*(XX => YY):* translated text
```

### Buttons

Generated bot messages use:

- `Original` to jump back to the source user-authored message
- uppercased language-code buttons (`EL`, `IT`, `EN`, etc.) for generated bot copies

> [!NOTE]
> In master flow, a generated localized copy can include a button that links back to itself, because every generated copy receives the same final button layout.

---

## Scenario 1: Message Sent in a Master Channel

Assume `#general` is the master channel and it has localized children `#general-greek (EL)` and `#general-italian (IT)`.

### Source Message in `#general`

> **gpowe**: Let's review the deployment schedule for this evening.

### Mirrored Message in `#general-greek`

> **gpowe** (EN => EL):
> Ας εξετάσουμε το πρόγραμμα ανάπτυξης για απόψε.
>
> `[Buttons: Original | EL | IT]`

### Mirrored Message in `#general-italian`

> **gpowe** (EN => IT):
> Rivediamo il programma di distribuzione per questa sera.
>
> `[Buttons: Original | EL | IT]`

---

## Scenario 2: Localized Match Flow

Assume the cluster contains:

- master: `#general`
- localized: `#general-greek (EL)`
- localized: `#general-english (EN)`
- localized: `#general-italian (IT)`

The user writes Greek in `#general-greek`.

### Original Message in `#general-greek`

> **nikos**: Η δοκιμή ολοκληρώθηκε με επιτυχία!

### Mirrored Raw Copy in `#general`

> **nikos**:
> Η δοκιμή ολοκληρώθηκε με επιτυχία!
>
> `[Buttons: Original | EN | IT]`

### Mirrored Translation in `#general-english`

> **nikos** (EL => EN):
> The test completed successfully!
>
> `[Buttons: Original | EN | IT]`

### Mirrored Translation in `#general-italian`

> **nikos** (EL => IT):
> Il test è stato completato con successo!
>
> `[Buttons: Original | EN | IT]`

> [!NOTE]
> There is no separate `EL` button in this flow because the source Greek message is the user-authored original, not a bot-generated mirror.

---

## Scenario 3: Localized Mismatch Flow

The user writes English in `#general-greek`.

### Original Message in `#general-greek`

> **gpowe**: Can we finalize the database parameters before lunch?

### In-Channel Translated Reply in `#general-greek`

> ↳ 🤖 **Tsumari** (Bot): *(EN => EL):* Μπορούμε να οριστικοποιήσουμε τις παραμέτρους της βάσης δεδομένων πριν από το μεσημεριανό γεύμα;
>
> `[Buttons: Original | EL | EN | IT]`

### Mirrored Raw Copy in `#general`

> **gpowe**:
> Can we finalize the database parameters before lunch?
>
> `[Buttons: Original | EL | EN | IT]`

### Mirrored Raw Copy in `#general-english`

> **gpowe**:
> Can we finalize the database parameters before lunch?
>
> `[Buttons: Original | EL | EN | IT]`

### Mirrored Translation in `#general-italian`

> **gpowe** (EN => IT):
> Possiamo definire i parametri del database prima di pranzo?
>
> `[Buttons: Original | EL | EN | IT]`

---

## Scenario 4: The User Edits the Original Message Later

Starting from Scenario 3, the user edits the original message to:

> **gpowe**: Can we finalize the database parameters before lunch and share them in the release thread?

### What Tsumari Updates

- the raw copy in `#general`
- the raw copy in `#general-english`
- the translated copy in `#general-italian`
- the translated reply in `#general-greek`

### Updated Reply in `#general-greek`

After edit sync, the reply remains a Discord reply message and keeps the compact same-channel translated-reply format:

> ↳ 🤖 **Tsumari** (Bot): *(EN => EL):* Μπορούμε να οριστικοποιήσουμε τις παραμέτρους της βάσης δεδομένων πριν από το μεσημεριανό γεύμα και να τις κοινοποιήσουμε στο νήμα της έκδοσης;
>
> `[Buttons: Original | EL | EN | IT]`

### Updated Translation in `#general-italian`

> **gpowe** (EN => IT):
> Possiamo definire i parametri del database prima di pranzo e condividerli nel thread della release?
>
> `[Buttons: Original | EL | EN | IT]`

> [!NOTE]
> The canonical edit-sync rules and current limitations live in [`routing.md`](routing.md#edited-message-synchronization).
