# Expiring CDN Media Re-Upload Layer

Discord's Content Delivery Network (CDN) links expire after 24 hours due to attached security signatures (`ex`, `is`, `hm` query parameters). Direct mirroring of URLs results in broken image and video attachments after the signature window closes.

Tsumari completely avoids this issue by downloading assets instantly when captured and re-uploading them directly as fresh native attachments to target rooms.

---

## 💾 Memory-Efficient Stream Management

Downloading and re-uploading large attachments can quickly exhaust the RAM limits of a container (especially HidenCloud's 1GB memory boundary). Tsumari implements a strict allocation strategy to keep footprint to an absolute minimum:

```
 Incoming Message (with Attachments)
                 |
        Download Bytes Once
      (Single byte[] in memory)
                 |
      +----------+----------+
      |                     |
Create Stream A       Create Stream B
  (MemStream)           (MemStream)
      |                     |
Upload to Channel 1   Upload to Channel 2
      |                     |
   Dispose               Dispose
```

### 1. Single Download Pattern
Instead of opening multiple network connections or creating separate duplicate byte arrays, Tsumari's `ProcessMessageRoutingPipelineAsync` downloads each attachment from the Discord CDN exactly once into a shared `byte[]` in memory.

### 2. Isolated Stream Instantiation
Discord.Net's `SendFilesAsync` consumes and advances streams during transmission. To support concurrent uploads to multiple channel rooms:
*   A new, isolated `MemoryStream` is created from the shared `byte[]` for each destination.
*   The stream is wrapped inside a `FileAttachment` model along with its original filename.

### 3. Immediate Garbage Collection & Disposal
All upload actions are bound inside structural `try/finally` blocks:
```csharp
finally
{
    foreach (var stream in streams)
    {
        stream.Dispose(); // CRITICAL: Free bytes immediately
    }
}
```
As soon as the Discord API confirms file delivery (or fails), `.Dispose()` is instantly called on all temporary memory streams. This releases the buffers immediately rather than waiting for the .NET Garbage Collector, preventing RAM memory leak scaling inside the HidenCloud container.
