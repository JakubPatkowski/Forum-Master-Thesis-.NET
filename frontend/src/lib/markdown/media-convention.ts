/**
 * FRONTEND-ONLY INLINE-MEDIA CONVENTION
 * =====================================
 * Thread/comment bodies are plain markdown strings; the backend stores and returns them
 * as inert text and has NO opinion on this syntax. The convention lets an author place
 * uploaded media inline, at the cursor:
 *
 *   ![caption](image:<fileId>)     — an inline image; <fileId> is the ULID of a file
 *                                    uploaded + committed through the Files API (§4.7).
 *   @video(<fileId>)               — an inline video, on its own line/paragraph.
 *
 * At render time the markdown renderer resolves <fileId> → GET /api/files/{fileId} and
 * renders a real <img>/<video> with the short-lived presigned URL. The sanitizer allows
 * exactly the `image:`/`video:` pseudo-protocols (plus plain http/https for images),
 * nothing broader. Unresolvable refs (deleted/orphan-swept files, legacy mock names)
 * degrade to an "unavailable" placeholder — never a broken request to the API origin.
 *
 * NOTE: the backend's upload allow-list is images only (png/jpeg/gif/webp) today, so the
 * @video token cannot yet reference an uploaded file in practice; the composer still
 * emits it and the renderer fully supports it, so enabling video uploads server-side is
 * a backend-only change.
 */

import type { Root, Paragraph, Image, Parent } from "mdast";
import { visit } from "unist-util-visit";

export const IMAGE_REF_PREFIX = "image:";
export const VIDEO_REF_PREFIX = "video:";

/** `@video(ref)` alone in a paragraph. Ref = ULID or (forward-compat) an https URL. */
const VIDEO_TOKEN_RE = /^@video\(([^\s()]+)\)$/;

export function imageToken(fileId: string, caption = ""): string {
  return `![${caption}](${IMAGE_REF_PREFIX}${fileId})`;
}

export function videoToken(ref: string): string {
  return `@video(${ref})`;
}

/** ULID: 26 chars of Crockford base32. Distinguishes file refs from external URLs. */
export function isFileRef(ref: string): boolean {
  return /^[0-9A-HJKMNP-TV-Z]{26}$/i.test(ref);
}

/** Concatenated text content of an mdast subtree (handles GFM-autolinked URLs). */
function textContent(node: unknown): string {
  if (typeof node !== "object" || node === null) return "";
  const record = node as { value?: unknown; children?: unknown[] };
  if (typeof record.value === "string") return record.value;
  if (Array.isArray(record.children)) return record.children.map(textContent).join("");
  return "";
}

/**
 * remark plugin: rewrites a paragraph consisting solely of `@video(ref)` into an image
 * node with a `video:` pseudo-protocol URL, so the single `img` component override in
 * the renderer handles both media kinds. Matching happens on the paragraph's full text
 * content because remark-gfm autolinks https refs at parse time (splitting the token
 * across text+link nodes). Operating on the AST (not the raw string) means tokens
 * inside code fences/inline code are left alone.
 */
export function remarkVideoEmbed() {
  return (tree: Root) => {
    visit(tree, "paragraph", (node: Paragraph, index, parent: Parent | undefined) => {
      if (!parent || index === undefined) return;
      const match = VIDEO_TOKEN_RE.exec(textContent(node).trim());
      if (!match || !match[1]) return;

      const image: Image = {
        type: "image",
        url: `${VIDEO_REF_PREFIX}${match[1]}`,
        alt: "",
      };
      parent.children[index] = { type: "paragraph", children: [image] } as Paragraph;
    });
  };
}
