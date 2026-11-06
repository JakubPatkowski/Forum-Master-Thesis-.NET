/**
 * Read-only browsing on the Development seed — the pages an anonymous visitor reaches from the
 * feed. Each test pins one claim of the SPA against the live API: the category page lists the
 * seeded threads, a thread page renders the article + comment tree + author stats card, full-text
 * search finds the seeded bodies (every seeded body contains the token "seeded" — SeedText.cs),
 * and a seeded user's profile renders real Engagement stats. Nothing here depends on IDs: every
 * ULID is resolved through the API first, so a reseed never breaks the suite.
 */

import {
  PUBLIC_CATEGORY_SLUG,
  expect,
  expectNoErrorState,
  publicCategoryWithThreads,
  publicThreadWithComments,
  test,
  threadTitle,
} from "./helpers";

test.describe("browsing the seed anonymously", () => {
  test("category page lists the seeded threads", async ({ page, request }) => {
    const { category, threads } = await publicCategoryWithThreads(request);

    await page.goto(`/c/${PUBLIC_CATEGORY_SLUG}`);
    await expect(page.getByRole("heading", { level: 1, name: category.name })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Threads" })).toBeVisible();

    const cards = page.getByTestId("thread-card");
    await expect(cards.first()).toBeVisible();
    // The first page of the feed is what the API returned; newest first, pinned on top.
    const firstThread = threads[0];
    if (!firstThread) throw new Error("unreachable: helper guarantees a non-empty feed");
    await expect(cards.filter({ hasText: firstThread.title }).first()).toBeVisible();
    expect(await cards.count()).toBeGreaterThanOrEqual(Math.min(threads.length, 1));
    await expectNoErrorState(page);
  });

  test("thread page shows the article, its comments and the author stats card", async ({
    page,
    request,
    isMobile,
  }) => {
    const { thread, comments } = await publicThreadWithComments(request);

    await page.goto(`/t/${thread.id}`);
    await expect(threadTitle(page)).toHaveText(thread.title);
    await expect(
      page.locator("main").getByText(`@${thread.username}`, { exact: true }).first(),
    ).toBeVisible();

    // Comment tree: the heading carries the count, every node is `#comment-<ulid>`.
    await expect(page.getByRole("heading", { level: 2, name: "Comments" })).toBeVisible();
    const nodes = page.locator('[id^="comment-"]');
    await expect(nodes.first()).toBeVisible();
    expect(await nodes.count()).toBe(comments.length);

    // Author stats card (THREADS / COMMENTS / KARMA from GET /api/engagement/users/{id}/stats).
    // The left rail is display:none under 1200px CSS px, so phones never see it — a layout
    // decision this suite records rather than hides (see the report), checked on desktop only.
    const stats = page.getByTestId("author-stats");
    if (isMobile) {
      await expect(stats).toBeHidden();
    } else {
      await expect(stats).toBeVisible();
      await expect(stats).toContainText(/\d+\s*THREADS/);
      await expect(stats).toContainText(/\d+\s*COMMENTS/);
      await expect(stats).toContainText(/-?\d+\s*KARMA/);
    }
    await expectNoErrorState(page);
  });

  test("full-text search finds the seeded bodies", async ({ page }) => {
    await page.goto("/search?q=seeded");
    await expect(page.getByText('RESULTS FOR "seeded"')).toBeVisible();
    await expect(page.getByTestId("thread-card").first()).toBeVisible();
    await expect(page.getByRole("textbox", { name: "Search threads" })).toHaveValue("seeded");
    await expectNoErrorState(page);
  });

  test("profile page renders a seeded user's stats", async ({ page, request }) => {
    const { threads } = await publicCategoryWithThreads(request);
    const author = threads[0];
    if (!author) throw new Error("unreachable: helper guarantees a non-empty feed");

    await page.goto(`/u/${author.ownerId}`);
    await expect(page.getByText(`@${author.username}`).first()).toBeVisible();
    await expect(page.getByText(author.displayName, { exact: true }).first()).toBeVisible();

    const stats = page.getByTestId("profile-stats");
    await expect(stats).toBeVisible();
    // A thread author has at least one thread; "–" would mean the stats query never resolved.
    await expect(stats).toContainText(/[1-9]\d*\s*THREADS/);
    await expect(stats).toContainText(/\d+\s*COMMENTS/);
    await expect(stats).toContainText(/-?\d+\s*KARMA/);
    await expect(page.getByText("RECENT ACTIVITY")).toBeVisible();
    await expectNoErrorState(page);
  });
});
