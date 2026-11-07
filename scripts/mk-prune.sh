#!/usr/bin/env bash
# Reclaim disk inside the minikube node: old app image tags + the BuildKit cache behind them.
#   scripts/mk-prune.sh                    prune (see the keep rules below)
#   scripts/mk-prune.sh --dry-run          print the keep/archive/remove plan, change nothing
#   scripts/mk-prune.sh --all-cache        drop the whole build cache instead of capping it
#   scripts/mk-prune.sh --archive-to DIR   also move archive-referenced images out of the node into
#                                          DIR/minikube-images-<stamp>.tar (verified before removal)
#
# WHY this exists: deploy.sh and gomx-bench-deploy.sh build straight into the node's Docker daemon
# (`minikube docker-env`) under a new git-<sha>[-dirty] tag per deploy, and nothing ever removed the
# old tags or the build cache behind them (34 GB of cache had piled up by 2026-11-07). All of it lives
# in the WSL ext4.vhdx on C:, which grows but never shrinks on its own — after a prune, compact it
# (docs/runbooks/wsl-minikube-setup.md §6).
#
# Only forum-dotnet-api / forum-dotnet-web / gomx tags (plus untagged leftovers) are candidates —
# third-party images are never touched (the MinIO images can no longer be pulled). A candidate is KEPT
# in the node when a pod or workload template in any namespace uses it (matched by image ID, so every
# tag of the running image survives, e.g. gomx:bench and gomx:bench-<sha>), or when it is the build of
# a `thesis-*` git tag (this repo, and $GOMX_DIR for gomx). A candidate whose tag or image ID appears
# anywhere under thesis/results (archived runs record both) is kept too — or, with --archive-to, saved
# to a tarball first and then removed. The api and web images of one build always share one fate.
# The cache is pruned least-recently-used down to MK_PRUNE_CACHE_KEEP (default 10GB), so the next
# deploy of a recent commit still builds warm.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd minikube; require_cmd kubectl; require_cmd docker

DRY_RUN=false; ALL_CACHE=false; ARCHIVE_DIR=""
while (( $# )); do
  case "$1" in
    --dry-run)    DRY_RUN=true ;;
    --all-cache)  ALL_CACHE=true ;;
    --archive-to) [[ -n "${2:-}" ]] || die "--archive-to needs a directory"; ARCHIVE_DIR="$2"; shift ;;
    *) die "Unknown arg '$1' (use: --dry-run | --all-cache | --archive-to <dir>)" ;;
  esac
  shift
done
: "${MK_PRUNE_CACHE_KEEP:=10GB}"
RESULTS_DIR="$REPO_ROOT/thesis/results"

mk status >/dev/null 2>&1 || die "minikube profile '$MINIKUBE_PROFILE' is not running (make mk-up)."
# Pruning is disk-heavy: it must never overlap a measured run.
if pgrep -f '^([^ ]*/)?bash [^ ]*(ab-round|ab-chaos|bench-run|run-load-test)\.sh' >/dev/null \
   || pgrep -x k6 >/dev/null; then
  die "A benchmark or load test is running — prune after it finishes."
fi

eval "$(mk docker-env)"

# --- Keep set -------------------------------------------------------------------
step "Collecting what must be kept"
declare -A IN_USE=() ARCHIVED=() THESIS_SHA=()

while read -r ref; do
  [[ -n "$ref" ]] || continue
  id="$(docker image inspect --format '{{.Id}}' "$ref" 2>/dev/null || true)"
  [[ -n "$id" ]] && IN_USE["${id#sha256:}"]=1
done < <(kubectl get pods,deploy,sts,ds,job,cronjob -A -o jsonpath='{..image}' | tr ' ' '\n' | sort -u)
while read -r hex; do
  IN_USE["$hex"]=1
done < <(kubectl get pods -A -o jsonpath='{..imageID}' | grep -oE '[0-9a-f]{64}' || true)
info "${#IN_USE[@]} image IDs in use by the cluster"

if [[ -d "$RESULTS_DIR" ]]; then
  while read -r token; do
    ARCHIVED["$token"]=1
  done < <(grep -rhoIE '[0-9a-f]{64}|(forum-dotnet-(api|web)|gomx):[A-Za-z0-9._-]+' "$RESULTS_DIR" | sort -u || true)
  info "${#ARCHIVED[@]} image tags/IDs referenced under thesis/results"
else
  warn "No $RESULTS_DIR — only the cluster and thesis-* tags protect images."
fi

for dir in "$REPO_ROOT" "$GOMX_DIR"; do
  [[ -d "$dir/.git" ]] || continue
  while read -r tag; do
    [[ -n "$tag" ]] && THESIS_SHA["$(git -C "$dir" rev-parse "$tag^{commit}")"]="$tag"
  done < <(git -C "$dir" tag -l 'thesis-*')
done
info "${#THESIS_SHA[@]} thesis-* git tags"

# thesis_tag_of <tag>: the thesis-* tag whose commit a clean git-<sha> / bench-<sha> build came from.
thesis_tag_of() {
  local sha full
  [[ "$1" =~ ^(git|bench)-([0-9a-f]{7,40})$ ]] || return 1
  sha="${BASH_REMATCH[2]}"
  for full in "${!THESIS_SHA[@]}"; do
    [[ "$full" == "$sha"* ]] && { printf '%s' "${THESIS_SHA[$full]}"; return 0; }
  done
  return 1
}
is_app_ref() { [[ "$1" == "$IMAGE_NAME:"* || "$1" == "$IMAGE_NAME_WEB:"* ]]; }

# --- Plan -------------------------------------------------------------------------
# Keyed by "<id> <ref>": every untagged leftover shares the ref <none>:<none>.
declare -A ACTION=() REASON=() PINNED_BUILD=() ARCHIVED_BUILD=()
CANDIDATES=()
while read -r id ref; do
  entry="$id $ref"; CANDIDATES+=("$entry")
  hex="${id#sha256:}"; tag="${ref##*:}"
  if [[ -n "${IN_USE[$hex]:-}" ]]; then ACTION["$entry"]=keep; REASON["$entry"]="in use by the cluster"
  elif t="$(thesis_tag_of "$tag")"; then ACTION["$entry"]=keep; REASON["$entry"]="build of $t"
  elif [[ -n "${ARCHIVED[$hex]:-}" || -n "${ARCHIVED[$ref]:-}" ]]; then
    ACTION["$entry"]=archive; REASON["$entry"]="referenced by thesis/results"
  else continue
  fi
  if is_app_ref "$ref"; then
    if [[ "${ACTION[$entry]}" == keep ]]; then PINNED_BUILD["$tag"]=1; else ARCHIVED_BUILD["$tag"]=1; fi
  fi
done < <(docker images --no-trunc --format '{{.ID}} {{.Repository}}:{{.Tag}}' \
           | awk -v a="$IMAGE_NAME" -v w="$IMAGE_NAME_WEB" \
               '{ split($2, p, ":") } p[1] == a || p[1] == w || p[1] == "gomx" || $2 == "<none>:<none>"')

ARCHIVE=(); REMOVE=()
for entry in "${CANDIDATES[@]}"; do
  id="${entry%% *}"; ref="${entry#* }"; tag="${ref##*:}"
  if is_app_ref "$ref"; then
    if [[ -n "${PINNED_BUILD[$tag]:-}" && "${ACTION[$entry]:-}" != keep ]]; then
      ACTION["$entry"]=keep; REASON["$entry"]="twin of a kept build"
    elif [[ -z "${ACTION[$entry]:-}" && -n "${ARCHIVED_BUILD[$tag]:-}" ]]; then
      ACTION["$entry"]=archive; REASON["$entry"]="twin of an archived build"
    fi
  fi
  # docker addresses an untagged image by its ID only.
  if [[ "$ref" == "<none>:<none>" ]]; then target="$id"; label="<none> ${id:7:12}"; else target="$ref"; label="$ref"; fi
  case "${ACTION[$entry]:-remove}" in
    keep)    printf '    keep     %-40s %s\n' "$label" "${REASON[$entry]}" ;;
    archive)
      if [[ -n "$ARCHIVE_DIR" ]]; then
        printf '    archive  %-40s %s\n' "$label" "${REASON[$entry]}"; ARCHIVE+=("$target")
      else
        printf '    keep     %-40s %s\n' "$label" "${REASON[$entry]}"
      fi ;;
    remove)  printf '    remove   %-40s\n' "$label"; REMOVE+=("$target") ;;
  esac
done

cache_size() { docker system df --format '{{if eq .Type "Build Cache"}}{{.Size}}{{end}}' | grep -v '^$' || echo 0B; }
if $ALL_CACHE; then cache_plan="drop all of it"; else cache_plan="trim least-recently-used down to $MK_PRUNE_CACHE_KEEP"; fi
info "Build cache: $(cache_size) now — $cache_plan"

if $DRY_RUN; then
  ok "Dry run — nothing changed (${#ARCHIVE[@]} image(s) would be archived, ${#REMOVE[@]} removed)."
  exit 0
fi

# --- Archive ----------------------------------------------------------------------
if (( ${#ARCHIVE[@]} )); then
  mkdir -p "$ARCHIVE_DIR"
  tarball="$ARCHIVE_DIR/minikube-images-$(date +%Y%m%d-%H%M).tar"
  step "Saving ${#ARCHIVE[@]} image(s) to $tarball"
  docker save -o "$tarball" "${ARCHIVE[@]}"
  # Check the manifest lists every saved tag/ID before anything leaves the node.
  python3 -I - "$tarball" "${ARCHIVE[@]}" <<'PY' || die "Archive check failed — nothing removed ($tarball)."
import json, sys, tarfile
path, wanted = sys.argv[1], sys.argv[2:]
with tarfile.open(path) as t:
    manifest = json.load(t.extractfile("manifest.json"))
tags = {tag for m in manifest for tag in (m.get("RepoTags") or [])}
configs = {m["Config"].rsplit("/", 1)[-1].removesuffix(".json") for m in manifest}
missing = [w for w in wanted if w not in tags and w.removeprefix("sha256:") not in configs]
if missing:
    print("missing from the archive:", *missing)
    sys.exit(1)
PY
  {
    echo "# Images moved out of minikube by scripts/mk-prune.sh on $(date -Iseconds)."
    echo "# Restore: eval \"\$(minikube -p $MINIKUBE_PROFILE docker-env)\" && docker load -i $(basename "$tarball")"
    for target in "${ARCHIVE[@]}"; do
      echo "$target $(docker image inspect --format '{{.Id}}' "$target")"
    done
  } > "$tarball.txt"
  ok "Archived $(du -h "$tarball" | cut -f1) — listing in $(basename "$tarball").txt"
  REMOVE+=("${ARCHIVE[@]}")
fi

# --- Prune ------------------------------------------------------------------------
step "Removing ${#REMOVE[@]} image(s)"
for target in "${REMOVE[@]}"; do
  docker rmi "$target" >/dev/null 2>&1 && info "removed $target" || warn "could not remove $target (still used by a container?)"
done

step "Pruning the build cache"
if $ALL_CACHE; then
  docker builder prune -af | tail -1
else
  docker builder prune -af --max-used-space "$MK_PRUNE_CACHE_KEEP" | tail -1
fi

docker system df
ok "Done. The space is free inside WSL; Windows gets it back only after compacting ext4.vhdx (runbook §6)."
