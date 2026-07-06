import { describe, expect, it } from "vitest";

import { problemFromBody } from "@/lib/api/problem";

describe("problemFromBody (RFC7807 mapping, brief §4.1)", () => {
  it("reads title/code/errorType from a standard envelope", () => {
    const error = problemFromBody(
      422,
      JSON.stringify({
        type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        title: "Comments may only nest 5 levels deep.",
        status: 422,
        code: "comment.max_depth_exceeded",
        errorType: "Validation",
      }),
    );
    expect(error.status).toBe(422);
    expect(error.title).toBe("Comments may only nest 5 levels deep.");
    expect(error.code).toBe("comment.max_depth_exceeded");
    expect(error.errorType).toBe("Validation");
  });

  it("handles the empty-body 429 with a status-derived message", () => {
    const error = problemFromBody(429, "");
    expect(error.status).toBe(429);
    expect(error.errorType).toBe("TooManyRequests");
    expect(error.title).toMatch(/too many requests/i);
    expect(error.code).toBeNull();
  });

  it("falls back for 401/403 envelopes missing code and errorType", () => {
    const error = problemFromBody(403, JSON.stringify({ title: "Forbidden", status: 403 }));
    expect(error.errorType).toBe("Forbidden");
    expect(error.code).toBeNull();
    expect(error.title).toBe("Forbidden");
  });

  it("handles the bespoke admin 400 shape { error: ... }", () => {
    const error = problemFromBody(400, JSON.stringify({ error: "Invalid scope id." }));
    expect(error.title).toBe("Invalid scope id.");
    expect(error.status).toBe(400);
    expect(error.code).toBeNull();
  });

  it("degrades gracefully for non-JSON bodies", () => {
    const error = problemFromBody(502, "<html>Bad Gateway</html>");
    expect(error.status).toBe(502);
    expect(error.errorType).toBe("Failure");
  });

  it("ignores unknown errorType strings rather than trusting them", () => {
    const error = problemFromBody(404, JSON.stringify({ title: "x", errorType: "Sparkles" }));
    expect(error.errorType).toBe("NotFound");
  });
});
