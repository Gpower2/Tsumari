# Attachment Mirroring

Discord CDN attachment URLs expire. Tsumari avoids stale CDN links by downloading attachments during the initial routing pass and re-uploading them as native Discord files in destination channels.

## Initial Send Path

During `MirroredMessageRoutingService` fan-out:

1. Tsumari first compares each `IAttachment.Size` against the current guild `MaxUploadLimit`.
2. Attachments that exceed the current guild upload cap are skipped before any CDN download begins.
3. `DiscordMessagePublisherService.DownloadMediaAssetsAsync(...)` downloads each remaining inbound attachment URL once.
4. The download uses `HttpClient.GetAsync(..., HttpCompletionOption.ResponseHeadersRead)` plus `ReadBytesWithStatusCheckAsync(...)` so HTTP failures are surfaced with the full logged response details.
5. The downloaded bytes are stored as:
   - filename
   - `byte[]`
6. Every outbound destination gets a fresh `MemoryStream` created from the shared `byte[]`.
7. `SendFilesAsync(...)` sends those streams as native Discord attachments.

## Why Separate Streams Are Created

Discord.Net consumes stream instances during upload. Reusing the same stream across destinations would break later sends, so Tsumari creates a new `MemoryStream` per destination while still reusing the same downloaded bytes.

## Disposal Behavior

`DiscordMessagePublisherService.SendMessageWithFilesAsync(...)` keeps a list of temporary streams and disposes all of them in a `finally` block after the send attempt finishes.

That keeps the message-routing path memory-conscious while still allowing multi-destination fan-out.

## Fallback Behavior

If `SendFilesAsync(...)` fails for a destination, Tsumari falls back to a plain text send:

```text
original text
*(Media attachments failed to mirror)*
```

The same button components are still attached to that fallback text message.

If Tsumari can tell up front that one or more attachments exceed the current guild upload limit, it skips downloading those files entirely and appends a short localized note to the mirrored text:

```text
*(Attachment too large to mirror - use Original.)*
```

The exact wording is localized from the destination/source language context for the mirrored copy. That pre-check saves time on oversized uploads while still leaving the `Original` jump button available for the source message.

## Interaction with Message Edit Sync

The edited-message path does **not** re-download or re-upload attachments.

Current edit behavior:

- mirrored message **content** is updated with `ModifyAsync(...)`
- existing buttons remain in place
- existing attachments remain as they were on the previously generated bot message

So:

- text edits are synchronized
- attachment-only edits are not
- new or removed attachments on the original message are not propagated to mirrored copies

## Attachment-Only Source Messages

If a source message has attachments but no text, Tsumari still routes the message and mirrors the files. The generated text body in those cases is just the author header, because there is no text payload to translate.
