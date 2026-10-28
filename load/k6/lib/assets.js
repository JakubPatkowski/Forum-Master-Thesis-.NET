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
 * Search corpus = the word bank BOTH seeders draw from — A: SeedText.cs, B: seedbench.go benchWordBank — plus the
 * `seeded` token every body starts with. Harness h4 (2026-10-18, deviation D4 closed): 300 words that the seeders
 * draw with a Zipf(s = 1.2) RANK distribution (rank 1 is in ~100 % of 120–320-word bodies, rank 50 in ~39 %,
 * rank 100 in ~20 %, rank 300 in ~6 %), so a one-word search drawn UNIFORMLY from the bank (J6 → pick.word())
 * spans the whole selectivity range: median query ≈ 13 % of the corpus, 39 % of queries under 10 %, 12 % over
 * half. The list is byte-identical in the three places and no word is a substring of another bank word or of the
 * fixed seed text, so A's tsvector token match and B's ILIKE substring match select the same documents.
 * Generated bodies (J3/J4 below) use the same Zipf draw as the seeders so load-time writes do not drift the corpus.
 */
export const SEARCH_WORDS = [
  'abandon', 'ability', 'abstract', 'accent', 'accept', 'acoustic', 'acrobat', 'adapt',
  'address', 'adopt', 'advice', 'aerial', 'agenda', 'agree', 'alarm', 'album',
  'alley', 'alpine', 'amber', 'amount', 'ancient', 'animal', 'ankle', 'answer',
  'anvil', 'apple', 'apron', 'arctic', 'argue', 'armor', 'artist', 'asteroid',
  'atlas', 'atom', 'auction', 'august', 'author', 'avenue', 'award', 'axis',
  'bacon', 'bagel', 'balcony', 'ballad', 'banana', 'banjo', 'barley', 'barrel',
  'battery', 'beacon', 'bedrock', 'beetle', 'berry', 'bingo', 'biscuit', 'bison',
  'blizzard', 'boulder', 'bouquet', 'breeze', 'bridge', 'bronze', 'budget', 'bugle',
  'bundle', 'bunker', 'butler', 'cabin', 'cactus', 'camera', 'candle', 'canvas',
  'canyon', 'caramel', 'carbon', 'carpet', 'carrot', 'castle', 'cedar', 'celery',
  'cement', 'cereal', 'chapel', 'charcoal', 'chimney', 'cider', 'cinema', 'citrus',
  'cliff', 'clinic', 'clover', 'cocoa', 'collar', 'colony', 'compass', 'condor',
  'coral', 'cornet', 'cotton', 'cousin', 'cradle', 'crater', 'cricket', 'cuckoo',
  'cupboard', 'cushion', 'dagger', 'daisy', 'dancer', 'decade', 'denim', 'desert',
  'diamond', 'dinner', 'dolphin', 'donkey', 'drawer', 'dungeon', 'eagle', 'echo',
  'edge', 'elder', 'elephant', 'emerald', 'engine', 'ermine', 'escort', 'ethics',
  'exhibit', 'exile', 'falcon', 'fantasy', 'feather', 'fennel', 'fiddle', 'fjord',
  'flagon', 'flannel', 'fleet', 'flute', 'fossil', 'freckle', 'fungus', 'funnel',
  'galaxy', 'gander', 'garnet', 'gazelle', 'giant', 'glacier', 'goblet', 'gondola',
  'gospel', 'grapefruit', 'gravel', 'guitar', 'hamlet', 'harbor', 'harvest', 'helmet',
  'hickory', 'hinge', 'hollow', 'horizon', 'hostel', 'hurdle', 'iceberg', 'impala',
  'incense', 'insect', 'island', 'jacket', 'jaguar', 'jelly', 'jigsaw', 'juniper',
  'kayak', 'kettle', 'kitten', 'koala', 'lagoon', 'lapel', 'lasso', 'laurel',
  'leopard', 'lichen', 'lilac', 'llama', 'locket', 'lumber', 'lynx', 'magnolia',
  'mango', 'mantis', 'marble', 'marmot', 'meadow', 'melon', 'mercury', 'midnight',
  'mirror', 'mitten', 'monsoon', 'moss', 'motel', 'napkin', 'needle', 'nickel',
  'noodle', 'oasis', 'olive', 'omelet', 'orbit', 'osprey', 'oyster', 'paddle',
  'palace', 'pansy', 'papaya', 'parrot', 'peacock', 'pelican', 'pepper', 'pewter',
  'pigeon', 'pirate', 'pistachio', 'plaza', 'pocket', 'polar', 'pomelo', 'porch',
  'prairie', 'pretzel', 'pumpkin', 'quail', 'quartz', 'rabbit', 'radar', 'raffle',
  'raisin', 'raven', 'recipe', 'reef', 'rifle', 'rocket', 'rosemary', 'rubble',
  'saffron', 'sandal', 'satchel', 'scarf', 'sequoia', 'shamrock', 'sherbet', 'shutter',
  'skillet', 'slipper', 'smoke', 'spinach', 'spruce', 'squirrel', 'stadium', 'starling',
  'stool', 'stork', 'summit', 'swallow', 'tadpole', 'tapestry', 'teapot', 'thimble',
  'thistle', 'ticket', 'timber', 'tomato', 'topaz', 'tortoise', 'trombone', 'trophy',
  'tulip', 'tunnel', 'turnip', 'turtle', 'umbrella', 'urchin', 'vanilla', 'verandah',
  'viking', 'vinegar', 'violet', 'waffle', 'walrus', 'wardrobe', 'wasabi', 'wicker',
  'window', 'wizard', 'yogurt', 'zephyr',
];

const ZIPF_EXPONENT = 1.2;
const ZIPF_CDF = (() => {
  const n = SEARCH_WORDS.length;
  let total = 0;
  for (let r = 1; r <= n; r++) {
    total += 1 / Math.pow(r, ZIPF_EXPONENT);
  }
  const cdf = new Array(n);
  let running = 0;
  for (let r = 1; r <= n; r++) {
    running += 1 / Math.pow(r, ZIPF_EXPONENT) / total;
    cdf[r - 1] = running;
  }
  cdf[n - 1] = 1;
  return cdf;
})();

/** Rank (0-based) for a uniform sample — the seeders' inverse-CDF draw (binary search). */
export function zipfRank(uniform) {
  let lo = 0;
  let hi = ZIPF_CDF.length - 1;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (ZIPF_CDF[mid] < uniform) {
      lo = mid + 1;
    } else {
      hi = mid;
    }
  }
  return lo;
}

/** A bank word with the seeders' Zipf rank distribution (bodies); queries draw uniformly via SEARCH_WORDS. */
export function zipfWord(rng) {
  return SEARCH_WORDS[zipfRank(rng())];
}

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
    body += zipfWord(rng);
    body += i % 18 === 17 ? '.\n\n' : ' ';
  }
  return `${body.trimEnd()}.`;
}

export function commentBody(rng, words = 18) {
  let body = 'seeded';
  for (let i = 0; i < words; i++) {
    body += ` ${zipfWord(rng)}`;
  }
  return `${body}.`;
}

export function threadTitle(rng) {
  const a = SEARCH_WORDS[Math.floor(rng() * SEARCH_WORDS.length)];
  const b = SEARCH_WORDS[Math.floor(rng() * SEARCH_WORDS.length)];
  return `Load test: ${a} ${b} ${Math.floor(rng() * 1_000_000)}`;
}

// --- PNG generator for the J4 image arm (harness h4/h6) -----------------------------------------------------------
// A real W×H RGBA PNG: the IDAT is a zlib stream of stored (uncompressed) deflate blocks, so no
// compression library is needed and the file weighs ≈ W×H×4 bytes — a small-photo-sized upload instead of the
// 68-byte 1×1 (which B would also de-duplicate by SHA-256 after the first upload and skip the object store). Both
// targets decode it for real: A's ImageProbe reads the IHDR, B's media worker decodes the whole image for its
// thumbnail.
//
// Harness h6+: The heavy uncompressed pixel buffer is built once per VU (cached in runtime memory), and every
// subsequent upload appends a unique tEXt chunk before IEND. This ensures a distinct SHA-256 digest on every upload
// (avoiding server-side deduplication) while eliminating the ~600 ms k6 CPU burn in JavaScript per J4 journey.
const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    table[n] = c >>> 0;
  }
  return table;
})();

function crc32(bytes) {
  let c = 0xffffffff;
  for (let i = 0; i < bytes.length; i++) {
    c = CRC_TABLE[(c ^ bytes[i]) & 0xff] ^ (c >>> 8);
  }
  return (c ^ 0xffffffff) >>> 0;
}

function adler32(bytes) {
  let a = 1;
  let b = 0;
  for (let i = 0; i < bytes.length; i++) {
    a = (a + bytes[i]) % 65521;
    b = (b + a) % 65521;
  }
  return ((b << 16) | a) >>> 0;
}

function be32(value) {
  return [(value >>> 24) & 0xff, (value >>> 16) & 0xff, (value >>> 8) & 0xff, value & 0xff];
}

function pngChunk(type, data) {
  const typeBytes = [...type].map((ch) => ch.charCodeAt(0));
  const body = new Uint8Array(typeBytes.length + data.length);
  body.set(typeBytes, 0);
  body.set(data, typeBytes.length);
  const out = new Uint8Array(4 + body.length + 4);
  out.set(be32(data.length), 0);
  out.set(body, 4);
  out.set(be32(crc32(body)), 4 + body.length);
  return out;
}

let pngBaseCache = null;
let uploadNonceSeq = 1;

function buildBasePng(width, height, rng) {
  const stride = 1 + width * 4; // filter byte + RGBA
  const raw = new Uint8Array(stride * height);
  let x = (Math.floor(rng() * 0xffffffff) >>> 0) || 0x9e3779b9;
  for (let y = 0; y < height; y++) {
    const row = y * stride;
    raw[row] = 0; // filter: none
    for (let i = 1; i < stride; i += 4) {
      x ^= x << 13; x >>>= 0; x ^= x >>> 17; x ^= x << 5; x >>>= 0;
      raw[row + i] = x & 0xff;
      raw[row + i + 1] = (x >>> 8) & 0xff;
      raw[row + i + 2] = (x >>> 16) & 0xff;
      raw[row + i + 3] = (x >>> 24) & 0xff;
    }
  }
  const blocks = Math.ceil(raw.length / 65535);
  const z = new Uint8Array(2 + raw.length + blocks * 5 + 4);
  let p = 0;
  z[p++] = 0x78; z[p++] = 0x01; // zlib header: deflate, no dictionary, fastest
  for (let i = 0; i < blocks; i++) {
    const off = i * 65535;
    const len = Math.min(65535, raw.length - off);
    z[p++] = i === blocks - 1 ? 1 : 0; // BFINAL, BTYPE=00 (stored)
    z[p++] = len & 0xff; z[p++] = (len >>> 8) & 0xff;
    z[p++] = (~len) & 0xff; z[p++] = ((~len) >>> 8) & 0xff;
    z.set(raw.subarray(off, off + len), p);
    p += len;
  }
  z.set(be32(adler32(raw)), p);
  const ihdr = new Uint8Array([...be32(width), ...be32(height), 8, 6, 0, 0, 0]); // 8-bit RGBA
  const parts = [
    new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk('IHDR', ihdr), pngChunk('IDAT', z), pngChunk('IEND', new Uint8Array(0)),
  ];
  const size = parts.reduce((n, part) => n + part.length, 0);
  const out = new Uint8Array(size);
  let o = 0;
  for (const part of parts) {
    out.set(part, o);
    o += part.length;
  }
  return { width, height, bytes: out };
}

/**
 * Returns { bytes: ArrayBuffer, size } — a valid RGBA PNG of width×height.
 * Uses cached base image buffer and stamps a unique tEXt chunk per call for instant (<0.1 ms) generation.
 */
export function makePng(width, height, rng) {
  if (!pngBaseCache || pngBaseCache.width !== width || pngBaseCache.height !== height) {
    pngBaseCache = buildBasePng(width, height, rng);
  }
  const base = pngBaseCache.bytes;
  const iendOffset = base.length - 12;

  const seqStr = String(uploadNonceSeq++);
  const randStr = String(Math.floor(rng() * 10000000));
  const commentStr = `Comment\0bench-${seqStr}-${randStr}`;
  const textBytes = new Uint8Array(commentStr.length);
  for (let i = 0; i < commentStr.length; i++) {
    textBytes[i] = commentStr.charCodeAt(i);
  }

  const typeAndData = new Uint8Array(4 + textBytes.length);
  typeAndData[0] = 116; typeAndData[1] = 69; typeAndData[2] = 88; typeAndData[3] = 116; // "tEXt"
  typeAndData.set(textBytes, 4);

  const crc = crc32(typeAndData);
  const chunkLen = 4 + 4 + textBytes.length + 4;
  const chunk = new Uint8Array(chunkLen);
  chunk.set(be32(textBytes.length), 0);
  chunk[4] = 116; chunk[5] = 69; chunk[6] = 88; chunk[7] = 116;
  chunk.set(textBytes, 8);
  chunk.set(be32(crc), 8 + textBytes.length);

  const out = new Uint8Array(base.length + chunkLen);
  out.set(base.subarray(0, iendOffset), 0);
  out.set(chunk, iendOffset);
  out.set(base.subarray(iendOffset), iendOffset + chunkLen);

  return { bytes: out.buffer, size: out.length };
}
