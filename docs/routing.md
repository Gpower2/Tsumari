# Routing Flows, Edit Synchronization, Delete Synchronization, and Reaction Mirroring

Tsumari has two live routing paths for new messages plus follow-up paths for edited messages, deleted messages, and linked reaction changes.

## Event Entry Points

`Worker` subscribes to:

- `MessageReceived` for new user-authored messages
- `MessageDeleted` and `MessagesBulkDeleted` for linked message cleanup
- `MessageUpdated` for edited user-authored messages
- `ReactionAdded`, `ReactionRemoved`, `ReactionsCleared`, and `ReactionsRemovedForEmote` for linked reaction reconciliation
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
2. Compare cached previous text with the current text when a cached pre-edit snapshot exists.
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
- Edits only apply to already-linked messages; the edit path never creates new messages.

### Current Limitations

- The handler only reacts to **text content changes**.
- Attachment-only edits are ignored.
- The current client cache is `50` messages. When a cached pre-edit snapshot is unavailable, Tsumari still re-synchronizes linked messages; it just cannot skip the update based on a before/after text comparison.

## Delete Synchronization

When an original source message is deleted, Tsumari deletes its existing linked bot-generated messages so the cluster does not keep orphaned mirrors.

### Current Behavior

1. On `MessageDeleted`, look up generated bot-message links by the deleted message ID.
2. If the deleted message is an original source message with linked bot messages:
   - delete each linked bot-generated message in place
   - remove that original message's `MessageLinks` rows
3. If the deleted message is itself a mirrored bot message:
   - remove only that stale `MessageLinks` row
   - do not delete any other messages
4. On `MessagesBulkDeleted`, repeat the same cleanup for each deleted message ID in the batch.

### Delete Synchronization Rules

- Delete sync only affects **existing linked bot messages**. It never creates replacement messages.
- The user's original source message is never re-created or otherwise modified.
- The same-channel translated reply created during localized mismatch flow is treated like any other linked bot message and is deleted when its source message is deleted.

### Current Limitations

- Cleanup is best-effort. If Discord rejects deletion of one linked bot message, Tsumari logs the failure and still removes the stale database rows for that original message family.
- Legacy rows created before `OriginalChannelId` existed are still cleaned up correctly when the **original** message is deleted, because delete sync for originals only depends on `OriginalMessageId`.

## Reaction Mirroring

When a standard reaction changes on any message that belongs to a linked message family, Tsumari reconciles that emoji across the family in place.

### Current Behavior

1. Resolve the linked-message family from either the original message ID or a mirrored message ID.
2. If the message is not linked, do nothing.
3. Inspect live reaction state across the original message plus every linked bot-generated copy.
4. If at least one non-bot user still has the emoji anywhere in the family:
   - ensure that every family message shows that emoji
   - add the bot's reaction only to messages that do not already have that emoji
5. If no non-bot user still has the emoji anywhere in the family:
   - remove the bot's mirrored reaction from any family message where it remains

### Reaction Mirroring Rules

- Reaction mirroring only updates **existing linked messages**. It never creates new messages.
- The original human reaction remains attributed to the human user on the message they actually reacted to.
- Mirrored reactions added elsewhere are attributed to the bot account.
- The reconciliation logic uses the live family state, so reactions can be triggered from either the original message or any mirrored copy.

### Current Limitations

- Only **standard reactions** are mirrored. Burst reactions are ignored.
- If an older mirrored message was created before source-channel IDs were stored in `MessageLinks`, family resolution from that mirrored message may be unavailable until the original message is seen again.

## Attachment Handling During New Message Routing

Attachment downloads happen before outbound routing. The same downloaded `byte[]` is reused across destinations, and each send gets its own `MemoryStream`.

If attachment upload fails for a destination, Tsumari falls back to sending plain text with:

`*(Media attachments failed to mirror)*`
