/**
 * Extracts h1–h3 headings from raw markdown for the thread page's "ON THIS PAGE" panel.
 * Line-based on purpose (cheap, no AST pass), but fence-aware so headings inside code
 * blocks are ignored. The slug algorithm must stay in sync with the renderer's heading
 * id override in MarkdownView.
 */

export interface MarkdownHeading {
  depth: number;
  text: string;
  slug: string;
}

export function slugifyHeading(text: string): string {
  return text
    .toLowerCase()
    .trim()
    .replace(/[^\p{L}\p{N}\s-]/gu, "")
    .replace(/\s+/g, "-")
    .replace(/-+/g, "-");
}

export function extractHeadings(markdown: string): MarkdownHeading[] {
  const headings: MarkdownHeading[] = [];
  let inFence = false;
  for (const line of markdown.split("\n")) {
    if (/^\s*(```|~~~)/.test(line)) {
      inFence = !inFence;
      continue;
    }
    if (inFence) continue;
    const match = /^(#{1,3})\s+(.+?)\s*#*\s*$/.exec(line);
    if (!match || !match[1] || !match[2]) continue;
    const text = match[2].trim();
    headings.push({ depth: match[1].length, text, slug: slugifyHeading(text) });
  }
  return headings;
}
