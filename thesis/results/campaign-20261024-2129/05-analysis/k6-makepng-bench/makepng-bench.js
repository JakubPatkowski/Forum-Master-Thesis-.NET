// Campaign v3 diagnostic: k6-side cost of the J4 synthetic photo (load/k6/lib/assets.js makePng, imported read-only from
// the tagged tree). One VU, one iteration, N PNGs per size; wall time per PNG from inside the JS VM. Process CPU (incl. the
// Go GC on other cores) is measured outside with /usr/bin/time. Usage: k6 run -e PX=256 -e N=20 makepng-bench.js
import { makePng } from 'file:///home/ubuntuwsl/projects/forum-dotnet/load/k6/lib/assets.js';

export const options = { vus: 1, iterations: 1 };
const PX = Number(__ENV.PX || 256);
const N = Number(__ENV.N || 20);

export default function () {
  const t0 = Date.now();
  let bytes = 0;
  for (let i = 0; i < N; i++) {
    const png = makePng(PX, PX, Math.random);
    bytes += png.size;
  }
  const ms = Date.now() - t0;
  console.log(JSON.stringify({ px: PX, n: N, total_ms: ms, ms_per_png: ms / N, avg_png_bytes: bytes / N }));
}
