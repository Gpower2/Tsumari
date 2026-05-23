# Multi-Master Routing Engine

Tsumari manages bi-directional cross-language translation using two distinct execution flows depending on which channel catches the user's message.

---

## 🛣️ Pipeline Routing Flows

### Branch A: Message Received in a Master Channel (e.g., `#general`)
When a member posts in the master hub, the bot keeps the original message untouched inside the room and routes copies into localized child rooms.

1.  **Context Check**: The bot verifies that the channel exists in `MasterChannels`.
2.  **Language Detection**: DeepL identifies the message's language code.
3.  **Target Dispatch**: The engine queries all child channels linked to this Master.
4.  **Translation Rules**:
    *   **Mismatch:** If the message language *does not* match a child's target language, the bot translates the content into the target language and posts it.
    *   **Match:** If the message language *matches* the child's target language, the bot forwards the original text as-is.
5.  **Tracking**: All generated mirrored snowflake IDs are mapped into the `MessageLinks` table.

---

### Branch B: Message Received in a Localized Channel (e.g., `#general-greek`)
When a member posts inside a localized room, the bot routes translations based on whether the input language matches that room's target settings.

```
                  Message in Localized Room (e.g., #general-greek)
                                    |
                       Detect Language & Compare
                                    |
                     +--------------+--------------+
                     |                             |
             MATCH (Greek input)            MISMATCH (English input)
                     |                             |
      1. Broadcast raw to #general        1. Leave untouched in current
      2. Translate & broadcast to         2. Translate to Greek and reply 
         siblings (#general-english,         as inline follow-up in current
         #general-italian, etc.)          3. Broadcast raw to #general and 
                                             home sibling (#general-english)
                                          4. Translate & broadcast to 
                                             other siblings (#general-italian)
```

#### Flow 1: Match Flow (User types Greek in `#general-greek`):
*   **Action A:** Broadcasts the original, untouched text payload directly to its parent Master Channel (`#general`).
*   **Action B:** Queries all other sibling channels (e.g., `#general-english`, `#general-italian`). It translates the message into each sibling's target language and posts it there.

#### Flow 2: Mismatch Flow (User types English in `#general-greek`):
*   **Action A:** **Does not delete** the user's message. The raw text is left untouched inside `#general-greek`.
*   **Action B:** Translates the text into Greek and posts it as an inline reply or immediate follow-up message inside `#general-greek` so native readers understand it.
*   **Action C:** Posts the original raw English text as-is to the parent Master Channel (`#general`) and to its proper home channel (`#general-english`).
*   **Action D:** Translates the input into all other remaining sibling channels (e.g., `#general-italian`) and posts it there.
*   **Action E:** Logs all newly generated message Snowflake IDs into `MessageLinks`.
