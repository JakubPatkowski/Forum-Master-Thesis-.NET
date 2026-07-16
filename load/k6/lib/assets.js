// Static assets for the k6 traffic mix (Phase 9c). No external files, no node_modules —
// everything the scenarios need is embedded so `k6 run load/k6/main.js` works from a bare checkout.

/**
 * A real, complete 1x1 RGBA PNG (68 bytes) — signature, IHDR, IDAT (zlib), IEND with correct CRCs.
 * The upload scenario declares image/png + this exact byte length on initiate, PUTs these bytes to the
 * presigned MinIO URL, then commits: the backend's ImageProbe parses the IHDR for real dimensions and
 * the commit handler stats the real object size, so a fake/truncated payload would 422 the commit.
 */
export const PNG_1X1 = new Uint8Array([
  0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d, 0x49, 0x48, 0x44, 0x52,
  0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1f, 0x15, 0xc4,
  0x89, 0x00, 0x00, 0x00, 0x0b, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9c, 0x63, 0x60, 0x00, 0x02, 0x00,
  0x00, 0x05, 0x00, 0x01, 0x7a, 0x5e, 0xab, 0x3f, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44,
  0xae, 0x42, 0x60, 0x82,
]).buffer;

export const PNG_1X1_BYTES = 68;

/**
 * Search corpus = the seeder's exact word bank (SeedText.cs) — every seeded thread body draws from
 * these words, so websearch_to_tsquery gets real FTS hits instead of benchmarking the empty-result path.
 */
export const SEARCH_WORDS = [
  'forum', 'thread', 'comment', 'keyset', 'pagination', 'benchmark', 'architecture', 'module',
  'postgres', 'rabbitmq', 'outbox', 'reaction', 'moderator', 'category', 'markdown', 'latency',
  'throughput', 'cluster', 'kubernetes', 'observability', 'trace', 'metric', 'cache', 'index',
  'aggregate', 'domain', 'event', 'handler', 'query', 'cursor', 'tsvector', 'seeded',
];

/** Prefixes for tag autocomplete — the Benchmark seed names tags tag-001..tag-060. */
export const TAG_PREFIXES = ['tag', 'tag-0', 'tag-00', 'tag-01', 'tag-02', 'tag-03', 'tag-04', 'tag-05'];

/** Existing seeded tag slugs for thread creation (get-or-create: existing slugs avoid unbounded tag growth). */
export function randomTagSlugs(rng) {
  const count = 1 + Math.floor(rng() * 3); // 1–3 tags per thread, validator caps at 5
  const slugs = [];
  while (slugs.length < count) {
    const slug = `tag-${String(1 + Math.floor(rng() * 60)).padStart(3, '0')}`;
    if (!slugs.includes(slug)) {
      slugs.push(slug);
    }
  }
  return slugs;
}

/** Markdown body in the same shape the seeder writes (heading + word-bank paragraph). */
export function threadBody(rng, words = 60) {
  let body = '## Load-generated thread\n\nseeded ';
  for (let i = 0; i < words; i++) {
    body += SEARCH_WORDS[Math.floor(rng() * SEARCH_WORDS.length)];
    body += i % 18 === 17 ? '.\n\n' : ' ';
  }
  return `${body.trimEnd()}.`;
}

export function commentBody(rng, words = 18) {
  let body = 'seeded';
  for (let i = 0; i < words; i++) {
    body += ` ${SEARCH_WORDS[Math.floor(rng() * SEARCH_WORDS.length)]}`;
  }
  return `${body}.`;
}

export function threadTitle(rng) {
  const a = SEARCH_WORDS[Math.floor(rng() * SEARCH_WORDS.length)];
  const b = SEARCH_WORDS[Math.floor(rng() * SEARCH_WORDS.length)];
  return `Load test: ${a} ${b} ${Math.floor(rng() * 1_000_000)}`;
}
