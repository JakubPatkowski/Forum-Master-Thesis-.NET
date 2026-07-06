import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";

import { MarkdownView } from "@/components/markdown/MarkdownView";
import { validateTagSlug } from "@/components/compose/TagPicker";

function Wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

const renderMarkdown = (markdown: string) =>
  render(<MarkdownView markdown={markdown} />, { wrapper: Wrapper });

describe("MarkdownView sanitization (stored-XSS defense, hard constraint #4)", () => {
  it("never executes or renders raw HTML from a body", () => {
    const { container } = renderMarkdown(
      'hello <script>window.x=1</script> <img src=x onerror="window.y=1"> world',
    );
    expect(container.querySelector("script")).toBeNull();
    const img = container.querySelector("img");
    if (img) expect(img.getAttribute("onerror")).toBeNull();
  });

  it("strips javascript: links", () => {
    const { container } = renderMarkdown("[click](javascript:alert(1))");
    const link = container.querySelector("a");
    expect(link?.getAttribute("href") ?? "").not.toContain("javascript");
  });

  it("renders normal markdown structure (headings get TOC-stable ids)", () => {
    const { container } = renderMarkdown("## Setup Notes\n\n- item");
    const heading = container.querySelector("h2");
    expect(heading?.id).toBe("setup-notes");
    expect(container.querySelector("li")).toHaveTextContent("item");
  });
});

describe("inline media convention (frontend-only, backend-inert)", () => {
  it("renders ![caption](image:<fileId>) through the file resolver, not a raw <img src>", () => {
    const { container } = renderMarkdown("![rig photo](image:01ARZ3NDEKTSV4RRFFQ69G5FAV)");
    // While the file query resolves, the placeholder renders — the raw pseudo-protocol
    // must never leak into a browser-fetched src.
    expect(container.querySelector('img[src^="image:"]')).toBeNull();
    expect(screen.getByText(/loading media/i)).toBeInTheDocument();
  });

  it("renders @video(https-url) paragraphs as a <video> element", () => {
    const { container } = renderMarkdown("before\n\n@video(https://example.com/clip.mp4)\n\nafter");
    const video = container.querySelector("video");
    expect(video).not.toBeNull();
    expect(video?.getAttribute("src")).toBe("https://example.com/clip.mp4");
  });

  it("shows the unavailable placeholder for unresolvable refs (legacy mock names)", () => {
    renderMarkdown("![x](image:not-a-ulid.png)");
    expect(screen.getByText(/image unavailable/i)).toBeInTheDocument();
  });

  it("leaves @video tokens inside code fences untouched", () => {
    const { container } = renderMarkdown("```\n@video(https://example.com/x.mp4)\n```");
    expect(container.querySelector("video")).toBeNull();
    expect(container.querySelector("code")).toHaveTextContent("@video(");
  });
});

describe("tag slug validation (mirrors the backend regex)", () => {
  it("accepts lowercase-kebab-case up to 32 chars", () => {
    expect(validateTagSlug("home-lab")).toBeNull();
    expect(validateTagSlug("a1-b2-c3")).toBeNull();
  });

  it("rejects uppercase, spaces, symbols and over-long slugs", () => {
    expect(validateTagSlug("Fly Fishing!!")).not.toBeNull();
    expect(validateTagSlug("UPPER")).not.toBeNull();
    expect(validateTagSlug("-leading")).not.toBeNull();
    expect(validateTagSlug("a".repeat(33))).not.toBeNull();
  });
});
