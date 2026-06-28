# Routing Flows and Edit Synchronization

Tsumari has two live routing paths for new messages and one follow-up path for edited messages.

## Event Entry Points

`Worker` subscribes to:

- `MessageReceived` for new user-authored messages
- `MessageUpdated` for edited user-authored messages
- `InteractionCreated` for `/tsumari` admin commands

Only user messages are processed. Bot messages are ignored.

## New Message Intake

For a new message, the routing pipeline:

1. Checks whether the channel is registered as a master or localized channel.
2. Detects the source language when text exists.
3. Downloads each attachment once into memory.
4. Builds outbound messages for the relevant cluster targets.
5. Stores generated message IDs in `MessageLinks`.
6. Edits generated bot messages to add the final jump-button set.

If language detection fails, the pipeline falls back to `EN`.

## Branch A: Message Received in a Master Channel

When a user posts in a master channel:

1. The original user message stays untouched in the master channel.
2. Tsumari queries every localized child channel for that master.
3. For each child:
   - If `detectedLang != child.TargetLanguageCode` and the message has text, Tsumari translates the content and sends:
     - `**Author** (SRC to TARGET):`
   - Otherwise, it sends the raw content:
     - `**Author**:`
4. Each outbound message is initially sent with a temporary `Original` button.
5. After all sends complete, Tsumari edits every generated copy to include:
   - `Original`
   - one button for every generated localized copy in that fan-out

### Resulting Button Behavior

In master flow, each generated localized copy receives the same final button set. That includes a button for its own language copy, because the final component layout is built from all generated bot messages.

## Branch B: Message Received in a Localized Channel

When a user posts in a localized channel, Tsumari first resolves the effective source locale for that message, then compares it with the channel configuration.

Routing comparisons are locale-aware:

- target-channel raw/translate decisions use exact locale matching (`PT` != `PT-BR`)
- source localized-channel matching can fall back from a generic detected code to that channel's configured locale (`PT` can be treated as `PT-BR` when the user posted inside a `pt-br` channel)
- locale-specific variants remain separate (`PT-BR` does not match `PT-PT`)

### Match Flow

If the detected language matches the localized channel's target:

1. The original localized message stays untouched.
2. Tsumari sends the raw message to the parent master channel.
3. Tsumari translates the content into every sibling localized channel.
4. Tsumari records every generated bot message in `MessageLinks`.
5. Tsumari edits the generated master/sibling bot messages to add jump buttons.

### Important Match-Flow Button Detail

There is **no separate button for the source localized channel** in match flow, because no bot-generated copy exists in that source channel. The original user-authored message is reached through the `Original` button instead.

### Mismatch Flow

If the detected language does **not** match the localized channel's target:

1. The original localized message stays untouched.
2. Tsumari posts a translated reply in the same localized channel:
   - `*(SRC to LOCAL):* translated text`
3. Tsumari sends the raw message to the parent master channel.
4. Tsumari sends the raw message to the sibling localized channel whose target language exactly matches the resolved source locale, if one exists.
5. Tsumari translates the message for every remaining sibling localized channel.
6. Tsumari stores the in-channel reply and every generated mirror in `MessageLinks`.
7. Tsumari edits all bot-generated messages to add jump buttons.

### Important Mismatch-Flow Button Detail

Because the translated in-channel reply is stored in `MessageLinks`, it participates in the final button layout. In mismatch flow, generated messages can therefore include buttons for:

- `Original`
- the localized reply's language
- the detected-language sibling
- every other translated sibling

## Button Generation Rules

The final button set is created in `EditJumpButtonsIntoSentMessagesAsync`.

- `Original` always links to the source user-authored message.
- Language buttons use the uppercased target language code for the generated bot message in that channel.
- Only bot-generated messages found in `sentMessages` become language buttons.
- Buttons are added by editing bot messages after all destination URLs are known.

## Edited Message Synchronization

When a user edits a message, `OnMessageUpdatedAsync` runs.

### Current Behavior

1. Ignore bot edits and non-user edits.
2. Compare cached previous text with the current text.
3. If the text is unchanged, do nothing.
4. Look up all mirrored/generated messages for the original message ID through `MessageLinks`.
5. For each mirrored message:
   - If the destination channel's target language exactly matches the resolved source locale, rewrite it as raw:
     - `**Author**:`
   - Otherwise translate it and rewrite it as:
     - `**Author** (SRC to TARGET):`
6. Update the mirrored message content in place with `ModifyAsync`.

### What Stays the Same During Edit Sync

- Existing jump buttons remain on the mirrored messages.
- Existing reply linkage remains on reply messages created by `ReplyAsync`.
- Attachments are not re-downloaded or replaced.

### Current Limitations

- The handler only reacts to **text content changes**.
- Attachment-only edits are ignored.
- The comparison relies on the Discord.Net message cache. The current client cache is `50` messages, so older edits may not have the pre-edit text available.
- The localized reply created during mismatch flow is initially sent as `*(SRC to LOCAL):* ...`, but if the source message is edited later, that reply's content is rewritten into the standard mirrored format:
  - `**Author** (SRC to LOCAL):`

## Attachment Handling During New Message Routing

Attachment downloads happen before outbound routing. The same downloaded `byte[]` is reused across destinations, and each send gets its own `MemoryStream`.

If attachment upload fails for a destination, Tsumari falls back to sending plain text with:

`*(Media attachments failed to mirror)*`
