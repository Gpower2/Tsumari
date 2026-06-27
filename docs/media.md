# Attachment Mirroring

Discord CDN attachment URLs expire. Tsumari avoids stale CDN links by downloading attachments during the initial routing pass and re-uploading them as native Discord files in destination channels.

## Initial Send Path

During `ProcessMessageRoutingPipelineAsync`:

1. Each inbound attachment URL is downloaded once with `HttpClient.GetByteArrayAsync(...)`.
2. The downloaded bytes are stored as:
   - filename
   - `byte[]`
3. Every outbound destination gets a fresh `MemoryStream` created from the shared `byte[]`.
4. `SendFilesAsync(...)` sends those streams as native Discord attachments.

## Why Separate Streams Are Created

Discord.Net consumes stream instances during upload. Reusing the same stream across destinations would break later sends, so Tsumari creates a new `MemoryStream` per destination while still reusing the same downloaded bytes.

## Disposal Behavior

`SendMessageWithFilesAsync(...)` keeps a list of temporary streams and disposes all of them in a `finally` block after the send attempt finishes.

That keeps the message-routing path memory-conscious while still allowing multi-destination fan-out.

## Fallback Behavior

If `SendFilesAsync(...)` fails for a destination, Tsumari falls back to a plain text send:

```text
original text
*(Media attachments failed to mirror)*
```

The same button components are still attached to that fallback text message.

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
