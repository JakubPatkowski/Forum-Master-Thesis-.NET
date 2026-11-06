/**
 * The signed-in write path end to end, through the real UI only: log in on /auth as alice (the
 * form, not the API), publish a thread in `general` from the compose modal (title, category, tag,
 * markdown body), find it on the category page, post a comment on it, and log out from the user
 * menu. It pins the whole chain — Argon2id login + refresh cookie + in-memory access token, the
 * compose modal → POST /api/content/threads → router push to /t/<id>, the comment composer →
 * POST …/comments → query invalidation re-render, and logout clearing the signed-in chrome. On the
 * android project every control is driven with a finger tap (see the `press` fixture). Rows are
 * created with a unique stamp and never cleaned up (the seed is reset by `make seed ARGS=--force`).
 */

import {
  ULID_PATTERN,
  devUser,
  expect,
  expectNoErrorState,
  loginThroughUi,
  logoutThroughUi,
  publicCategoryWithThreads,
  runStamp,
  test,
  threadTitle,
} from "./helpers";

test.describe("authenticated write path", () => {
  test("log in, publish a thread, comment on it, log out", async ({ page, request, press }) => {
    const alice = devUser("alice");
    const { category } = await publicCategoryWithThreads(request);
    const stamp = runStamp();
    const title = `E2E thread ${stamp}`;
    const body = `Written by the Playwright suite (${stamp}).\n\n## Section\n\nA **markdown** body.`;
    const comment = `E2E comment ${stamp}`;

    await loginThroughUi(page, alice, press);

    // --- compose --------------------------------------------------------------------------------
    // TopNav and the feed header both offer "+ New thread"; the first in DOM order is the TopNav one.
    await press(page.getByRole("button", { name: "+ New thread" }).first());
    const dialog = page.getByRole("dialog", { name: "New thread" });
    await expect(dialog).toBeVisible();

    await dialog.getByLabel("Title").fill(title);
    await dialog.locator("select").selectOption(category.id);
    const tagInput = dialog.getByLabel("Add tag");
    await tagInput.fill("e2e");
    // While the debounced lookup for "e2e" is in flight the dropdown still shows the previous,
    // popularity-ranked list and Enter would commit ITS highlighted entry instead of the typed slug
    // (a UX race in TagPicker — see the report). Wait until nothing but "#e2e" itself is on offer.
    // (Escape is not an option: the modal closes on it.)
    const staleSuggestions = dialog
      .locator("button", { hasText: /^#/ })
      .filter({ hasNotText: "#e2e" });
    await expect(staleSuggestions).toHaveCount(0);
    await tagInput.press("Enter");
    await expect(dialog.getByRole("button", { name: "Remove tag e2e", exact: true })).toBeVisible();
    await dialog.locator("textarea").fill(body);
    await press(dialog.getByRole("button", { name: "Publish thread" }));

    // The modal navigates to the new thread; the article shows what was typed.
    await expect(page).toHaveURL(new RegExp(`/t/${ULID_PATTERN}$`));
    await expect(dialog).toHaveCount(0);
    await expect(threadTitle(page)).toHaveText(title);
    await expect(page.getByRole("heading", { level: 2, name: "Section" })).toBeVisible();
    await expect(page.getByRole("link", { name: "#e2e", exact: true }).first()).toBeVisible();
    const threadUrl = page.url();

    // --- it is listed in its category (fresh load, newest first) ----------------------------------
    await page.goto(`/c/${category.slug}`);
    await expect(page.getByTestId("thread-card").filter({ hasText: title }).first()).toBeVisible();
    await expectNoErrorState(page);

    // --- comment ---------------------------------------------------------------------------------
    await page.goto(threadUrl);
    const commentBox = page.getByPlaceholder("Write a comment");
    await expect(commentBox).toBeVisible();
    await commentBox.fill(comment);
    await press(page.getByRole("button", { name: "Post comment" }));
    const posted = page.locator('[id^="comment-"]').filter({ hasText: comment });
    await expect(posted).toBeVisible();
    await expect(posted.getByText("YOU", { exact: true })).toBeVisible();
    await expect(commentBox).toHaveValue("");
    await expectNoErrorState(page);

    // --- log out ---------------------------------------------------------------------------------
    await logoutThroughUi(page, alice, press);
    // Anonymous again: the comment composer is replaced by the login prompt on the same thread.
    await page.goto(threadUrl);
    await expect(page.getByRole("link", { name: "Log in to join the discussion" })).toBeVisible();
    await expect(page.getByPlaceholder("Write a comment")).toHaveCount(0);
  });
});
