/**
 * Android checks (project `android` = Pixel 7 emulation: Chrome-for-Android UA, touch, 412 CSS px
 * wide at DPR 2.625) — the phone pass Hubert insists on for the A/B comparison. On the home page,
 * the category page, a thread page and a search results page it pins: no horizontal overflow (the
 * document never wider than the window AND the window never wider than the phone — Android Chrome
 * silently widens the layout viewport when content overflows, which the naive scrollWidth check
 * alone would miss), the primary navigation reachable without any menu (the SPA has no hamburger:
 * the 01 FORUM · 02 SEARCH · 03 PROFILE tab row stays visible and tappable at every width), tap
 * targets of the main actions at least 40 CSS px tall, and the login + compose + comment + logout
 * journey driven with touch. The last describe block asserts the two product gaps measured on
 * 2026-10-18 and fixed on 2026-10-24 (signed-in TopNav wider than the phone; secondary controls
 * below the 40 px floor) — they were `test.fail`-pinned until then. Skipped under the desktop project.
 */

import type { Locator, Page } from "@playwright/test";

import {
  PUBLIC_CATEGORY_SLUG,
  ULID_PATTERN,
  devUser,
  expect,
  expectNoErrorState,
  loginThroughUi,
  logoutThroughUi,
  publicCategoryWithThreads,
  publicThreadWithComments,
  runStamp,
  test,
  threadTitle,
} from "./helpers";

test.skip(({ isMobile }) => !isMobile, "Android device emulation only (project 'android')");

/** Minimum height of a primary tap target, CSS px (Android guidance is 48dp; 40 is the agreed floor). */
const MIN_TAP_TARGET_PX = 40;

const NAV_TABS = ["01 FORUM", "02 SEARCH", "03 PROFILE"] as const;

async function expectNoHorizontalOverflow(page: Page) {
  const phoneWidth = page.viewportSize()?.width ?? 0;
  const { scrollWidth, innerWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  expect(
    scrollWidth,
    `document.documentElement.scrollWidth (${scrollWidth}) must not exceed window.innerWidth (${innerWidth})`,
  ).toBeLessThanOrEqual(innerWidth);
  expect(
    innerWidth,
    `window.innerWidth (${innerWidth}) must not exceed the phone's width (${phoneWidth}) — a wider value means Chrome widened the layout viewport to fit overflowing content`,
  ).toBeLessThanOrEqual(phoneWidth);
}

/** The tab row is the primary navigation: every tab visible AND fully inside the phone's width. */
async function expectPrimaryNavReachable(page: Page) {
  const phoneWidth = page.viewportSize()?.width ?? 0;
  for (const name of NAV_TABS) {
    const tab = page.getByRole("link", { name, exact: true });
    await expect(tab).toBeVisible();
    const box = await tab.boundingBox();
    if (!box) throw new Error(`${name}: no bounding box`);
    expect(box.x, `${name} starts inside the viewport`).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width, `${name} ends inside the viewport`).toBeLessThanOrEqual(phoneWidth);
  }
}

interface TapTarget {
  label: string;
  locator: Locator;
}

async function expectTapTargets(targets: TapTarget[]) {
  const tooShort: string[] = [];
  for (const { label, locator } of targets) {
    await expect(locator, label).toBeVisible();
    const box = await locator.boundingBox();
    if (!box) throw new Error(`${label}: no bounding box`);
    if (box.height < MIN_TAP_TARGET_PX) tooShort.push(`${label} = ${box.height.toFixed(1)}px`);
  }
  expect(tooShort, `tap targets shorter than ${MIN_TAP_TARGET_PX}px`).toEqual([]);
}

function navTabTargets(page: Page): TapTarget[] {
  return NAV_TABS.map((name) => ({
    label: `nav tab "${name}"`,
    locator: page.getByRole("link", { name, exact: true }),
  }));
}

test.describe("Pixel 7 layout", () => {
  test("home page", async ({ page }) => {
    await page.goto("/");
    const cards = page.getByTestId("thread-card");
    await expect(cards.first()).toBeVisible();
    await expectNoErrorState(page);
    await expectNoHorizontalOverflow(page);
    await expectPrimaryNavReachable(page);
    await expectTapTargets([
      ...navTabTargets(page),
      { label: "first thread card", locator: cards.first() },
      {
        label: "top search box",
        locator: page.getByRole("textbox", { name: "Search", exact: true }),
      },
      { label: "sidebar: All threads", locator: page.getByRole("link", { name: "All threads" }) },
      {
        label: "sidebar: General",
        locator: page.getByRole("link", { name: "General", exact: true }).first(),
      },
    ]);
  });

  test("category page", async ({ page }) => {
    await page.goto(`/c/${PUBLIC_CATEGORY_SLUG}`);
    const cards = page.getByTestId("thread-card");
    await expect(cards.first()).toBeVisible();
    await expectNoErrorState(page);
    await expectNoHorizontalOverflow(page);
    await expectPrimaryNavReachable(page);
    await expectTapTargets([
      ...navTabTargets(page),
      { label: "first thread card", locator: cards.first() },
      {
        label: "+ New thread (category header)",
        locator: page.getByRole("button", { name: "+ New thread" }),
      },
    ]);
  });

  test("thread page", async ({ page, request }) => {
    const { thread } = await publicThreadWithComments(request);
    await page.goto(`/t/${thread.id}`);
    await expect(threadTitle(page)).toHaveText(thread.title);
    await expect(page.locator('[id^="comment-"]').first()).toBeVisible();
    await expectNoErrorState(page);
    await expectNoHorizontalOverflow(page);
    await expectPrimaryNavReachable(page);
    await expectTapTargets([
      ...navTabTargets(page),
      {
        label: "top search box",
        locator: page.getByRole("textbox", { name: "Search", exact: true }),
      },
    ]);
  });

  test("search results page", async ({ page }) => {
    await page.goto("/search?q=seeded");
    const cards = page.getByTestId("thread-card");
    await expect(cards.first()).toBeVisible();
    await expectNoErrorState(page);
    await expectNoHorizontalOverflow(page);
    await expectPrimaryNavReachable(page);
    await expectTapTargets([
      ...navTabTargets(page),
      { label: "first result card", locator: cards.first() },
      { label: "search input", locator: page.getByRole("textbox", { name: "Search threads" }) },
    ]);
  });

  test("login page", async ({ page }) => {
    await page.goto("/auth");
    await expectNoHorizontalOverflow(page);
    await expectPrimaryNavReachable(page);
    await expectTapTargets([
      { label: "LOG IN tab", locator: page.getByRole("button", { name: "LOG IN", exact: true }) },
      { label: "Email", locator: page.getByLabel("Email") },
      { label: "Password", locator: page.getByLabel("Password") },
      { label: "Log in", locator: page.getByRole("button", { name: "Log in", exact: true }) },
    ]);
  });
});

test.describe("Pixel 7 touch flows", () => {
  test("login, compose a thread, comment and log out with taps", async ({
    page,
    request,
    press,
  }) => {
    const alice = devUser("alice");
    const { category } = await publicCategoryWithThreads(request);
    const stamp = runStamp();
    const title = `E2E touch thread ${stamp}`;
    const comment = `E2E touch comment ${stamp}`;

    // Anonymous pages fit the phone, so Playwright's own tap() is used up to and including login;
    // after that `press` taps through a raw touch event (why: helpers.ts, tapAt).
    await page.goto("/auth");
    await page.getByLabel("Email").tap();
    await page.getByLabel("Email").fill(alice.email);
    await page.getByLabel("Password").tap();
    await page.getByLabel("Password").fill(alice.password);
    await page.getByRole("button", { name: "Log in", exact: true }).tap();
    await expect(page.getByRole("button", { name: alice.username })).toBeVisible();
    await expect(page).toHaveURL(/\/$/);

    // Compose from the category header (`.last()`; the TopNav's own "+ New thread" is `.first()`).
    await page.goto(`/c/${category.slug}`);
    await expect(page.getByRole("button", { name: alice.username })).toBeVisible();
    await press(page.getByRole("button", { name: "+ New thread" }).last());
    const dialog = page.getByRole("dialog", { name: "New thread" });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel("Title").fill(title);
    await dialog.locator("select").selectOption(category.id);
    await dialog.locator("textarea").fill(`Tapped out on a phone (${stamp}).`);
    await expectTapTargets([
      { label: "Publish thread", locator: dialog.getByRole("button", { name: "Publish thread" }) },
    ]);
    await press(dialog.getByRole("button", { name: "Publish thread" }));

    await expect(page).toHaveURL(new RegExp(`/t/${ULID_PATTERN}$`));
    await expect(threadTitle(page)).toHaveText(title);
    await expectNoErrorState(page);

    const commentBox = page.getByPlaceholder("Write a comment");
    await commentBox.fill(comment);
    await press(page.getByRole("button", { name: "Post comment" }));
    await expect(page.locator('[id^="comment-"]').filter({ hasText: comment })).toBeVisible();
    await expectNoErrorState(page);

    await logoutThroughUi(page, alice, press);
  });
});

/**
 * Formerly `test.fail`-pinned product gaps (measured 2026-10-18: the signed-in TopNav needed ~532
 * CSS px so Chrome widened the layout viewport past 412 px and cut off the user menu and the
 * composer; Post comment 32.8 px, TopNav + New thread 32.8, icon buttons 36, user menu 38, REPLY
 * 17.6, breadcrumb 17.6, …). Fixed 2026-10-24: the top row wraps the search onto its own line and
 * collapses the button labels at phone widths, and every control carries a 40 px touch floor under
 * `(pointer: coarse), (max-width: 480px)` — these now assert the fix.
 */
test.describe("signed-in chrome and secondary actions on the phone", () => {
  test("signed-in chrome fits the phone width", async ({ page, press }) => {
    await loginThroughUi(page, devUser("alice"), press);
    await page.goto(`/c/${PUBLIC_CATEGORY_SLUG}`);
    await expect(page.getByRole("button", { name: "alice" })).toBeVisible();
    await expect(page.getByTestId("thread-card").first()).toBeVisible();
    await expectNoHorizontalOverflow(page);
  });

  test("secondary actions reach the 40 px tap floor", async ({ page, request, press }) => {
    const { thread } = await publicThreadWithComments(request);
    await loginThroughUi(page, devUser("alice"), press);
    await page.goto(`/t/${thread.id}`);
    await expect(page.getByRole("button", { name: "alice" })).toBeVisible();
    await expect(page.locator('[id^="comment-"]').first()).toBeVisible();
    await expectTapTargets([
      { label: "Post comment", locator: page.getByRole("button", { name: "Post comment" }) },
      {
        label: "TopNav + New thread",
        locator: page.getByRole("button", { name: "+ New thread" }).first(),
      },
      { label: "user menu", locator: page.getByRole("button", { name: "alice" }) },
      { label: "activity bell", locator: page.getByRole("button", { name: "Realtime activity" }) },
      { label: "REPLY", locator: page.getByRole("button", { name: "REPLY", exact: true }).first() },
      {
        label: "category breadcrumb",
        locator: page
          .getByRole("link", { name: thread.categoryName.toUpperCase(), exact: true })
          .first(),
      },
    ]);
  });
});
