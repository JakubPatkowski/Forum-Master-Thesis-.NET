# k8s/minio

MinIO StatefulSet (PSS `restricted`, uid 1000, pinned RELEASE tag) + Service + one-shot
`minio-create-bucket` Job (mc) + a dedicated Ingress for the presigned-URL host
`minio.forum.local` (scoped `proxy-body-size: 10m`; ADR 0008/G5 — browser bytes bypass the backend).

**Phase 10c contract:** Service `minio` carries `labels: {app: minio}` and named ports `s3` 9000 /
`console` 9001. Metrics are on the `s3` port (`/minio/v2/metrics/...`, `MINIO_PROMETHEUS_AUTH_TYPE=public`);
NetworkPolicy `40-minio-allow` already admits the `monitoring` namespace on 9000.
Console: `make tunnels` → http://localhost:19001 (never via ingress).
