"use client";

/**
 * THE sanitized markdown renderer (brief hard constraint #4): bodies are raw untrusted
 * strings and the backend does zero sanitization, so everything renders through
 * react-markdown (raw HTML is never parsed — it stays inert text) plus rehype-sanitize
 * as defense in depth. The sanitize schema is the default GitHub-style schema extended
 * ONLY with the `image:`/`video:` pseudo-protocols of the inline-media convention
 * (lib/markdown/media-convention.ts) — nothing broader.
 */

import { useMemo } from "react";
import ReactMarkdown, { type Components } from "react-markdown";
import rehypeSanitize, { defaultSchema } from "rehype-sanitize";
import remarkGfm from "remark-gfm";

import { InlineMedia } from "@/components/markdown/InlineMedia";
import { slugifyHeading } from "@/lib/markdown/headings";
import {
  IMAGE_REF_PREFIX,
  VIDEO_REF_PREFIX,
  remarkVideoEmbed,
} from "@/lib/markdown/media-convention";

const sanitizeSchema = {
  ...defaultSchema,
  protocols: {
    ...defaultSchema.protocols,
    src: ["http", "https", "image", "video"],
  },
};

/** react-markdown drops custom URL schemes unless the transform allows them. */
function urlTransform(url: string): string {
  if (url.startsWith(IMAGE_REF_PREFIX) || url.startsWith(VIDEO_REF_PREFIX)) return url;
  if (/^https?:/i.test(url) || url.startsWith("#") || url.startsWith("/")) return url;
  return "";
}

function headingWithId(Tag: "h1" | "h2" | "h3") {
  return function Heading({ children }: { children?: React.ReactNode }) {
    const text = Array.isArray(children) ? children.join("") : String(children ?? "");
    return <Tag id={slugifyHeading(text)}>{children}</Tag>;
  };
}

const components: Components = {
  h1: headingWithId("h1"),
  h2: headingWithId("h2"),
  h3: headingWithId("h3"),
  a: ({ href, children }) => (
    <a href={href} target="_blank" rel="noopener noreferrer">
      {children}
    </a>
  ),
  img: ({ src, alt }) => {
    const url = typeof src === "string" ? src : "";
    if (url.startsWith(IMAGE_REF_PREFIX)) {
      return (
        <InlineMedia
          kind="image"
          mediaRef={url.slice(IMAGE_REF_PREFIX.length)}
          caption={alt ?? ""}
        />
      );
    }
    if (url.startsWith(VIDEO_REF_PREFIX)) {
      return (
        <InlineMedia
          kind="video"
          mediaRef={url.slice(VIDEO_REF_PREFIX.length)}
          caption={alt ?? ""}
        />
      );
    }
    // Plain external image (http/https already enforced by the sanitizer).
    // eslint-disable-next-line @next/next/no-img-element
    return <img src={url} alt={alt ?? ""} loading="lazy" />;
  },
};

export function MarkdownView({ markdown, className }: { markdown: string; className?: string }) {
  const plugins = useMemo(() => [remarkGfm, remarkVideoEmbed], []);
  return (
    <div className={className ? `markdown-body ${className}` : "markdown-body"}>
      <ReactMarkdown
        remarkPlugins={plugins}
        rehypePlugins={[[rehypeSanitize, sanitizeSchema]]}
        urlTransform={urlTransform}
        components={components}
      >
        {markdown}
      </ReactMarkdown>
    </div>
  );
}
