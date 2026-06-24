# ADR 0008 — Direct-to-MinIO uploads via presigned URLs

**Status:** Accepted

**Context.** File bytes (images/attachments) are large relative to API payloads. Proxying them through the
backend ties up app threads/memory, inflates request size limits, and couples upload throughput to API scaling.
We want the backend to stay a thin control plane and let object bytes flow straight to object storage.

**Decision.** Uploads use **presigned URLs** so bytes **never transit the backend**:

1. **Initiate** — client `POST /api/v1/files` with `{ contentType, size }`. Backend authorizes, validates the
   declared type/size, creates a `pending` `files` row (ULID id + content-addressed `object_key`), and returns a
   **presigned PUT URL** (short TTL) for the MinIO bucket.
2. **Upload** — client `PUT`s the bytes **directly to MinIO** using that URL (browser → MinIO, not via the API).
3. **Commit** — client `POST /api/v1/files/{id}/commit`. Backend **HEADs the object**, verifies it exists and its
   real `content-type`/size match (the declared values are never trusted), decodes image dimensions, flips the
   row to `committed`, and links it to its target via `file_attachments(target_type, target_id)`.

Serving reads also go via presigned GET (or a CDN), so the backend isn't in the byte path either. A periodic
**orphan sweep** deletes `pending` files past a grace window and committed blobs with no attachment; deletion of
a target (`ThreadDeleted`/`CommentDeleted`) drives detach/cleanup via the bus.

**Consequences.** (+) Backend offloaded from byte transfer (better latency/throughput under upload load — a
benchmark talking point vs B, which proxies uploads through the Go server); native S3/MinIO scaling; smaller API
limits. (−) Two-step protocol + a commit/verify step; MinIO must be reachable from the browser (CORS + a public
endpoint or gateway); requires the orphan sweep for never-committed uploads. Bucket is private; all access is via
short-lived presigned URLs.
