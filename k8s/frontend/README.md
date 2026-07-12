# k8s/frontend

Next.js standalone shell (PSS `restricted`, ro-rootfs with an emptyDir at `.next/cache`,
`runAsUser: 1000` pinned because the node image's USER is the non-numeric string `node`).

The image bakes `NEXT_PUBLIC_API_URL` at **build time** (`scripts/build-images.sh`, default
`https://$INGRESS_HOST`) — changing scheme/host means REBUILDING the image, not editing manifests.

**Phase 10c contract:** Service `frontend` (`labels: {app: frontend}`, named port `http` 80→3000).
The shell exposes no `/metrics`; no ServiceMonitor needed.
