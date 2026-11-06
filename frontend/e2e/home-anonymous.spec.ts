/**
 * THE REGRESSION THIS FILE PINS: the home page is a client-side k-way merge over one keyset feed per
 * category (lib/feed/feed-merge.ts + use-home-feed.ts). When the backend's A-12 read gate started
 * answering 404 for a private category's feed, ONE rejected source threw the whole merge into the
 * "// SIGNAL LOST" boundary with "Category not found" — although every public thread was readable.
 * 248 backend tests and 69 Vitest tests stayed green because nothing rendered the SPA against the
 * real API; this file does. The SPA contract under test: a listed category whose feed the viewer
 * may not read contributes nothing — it never fails the merged feed.
 *
 * Three angles, all on the Development seed (public `general`, private `private-club`):
 *  1. the plain anonymous home page renders the public feed and a card opens its thread;
 *  2. deterministic reproduction of the campaign trigger: the real private category row is added to
 *     the anonymous category-list response (only that one response is patched — the feed request
 *     it provokes goes to the real API and is really rejected), and the page must still render;
 *  3. the mock-free user journey that produces the same state: bob (the seed's `moderate` member of
 *     private-club) opens the home page, which caches the category list INCLUDING private-club
 *     (React Query keeps it across logout), logs out, and lands on the home page again as an
 *     anonymous reader whose cached list still names a category that now answers 404.
 * Whether the list endpoint hides private categories from anonymous readers is a backend policy
 * (it changed during the A/B alignment); the suite must not depend on it, so the private row is
 * resolved through bob whenever the anonymous listing omits it.
 */

import type { APIRequestContext, Page } from "@playwright/test";

import {
  PRIVATE_CATEGORY_SLUG,
  PUBLIC_CATEGORY_SLUG,
  ULID_PATTERN,
  apiUrl,
  devUser,
  expect,
  expectNoErrorState,
  getCategory,
  getThread,
  listCategories,
  loginThroughUi,
  loginViaApi,
  logoutThroughUi,
  test,
  threadTitle,
  type CategoryResponse,
} from "./helpers";

/** bob is the one non-staff member of private-club (SeedPlan.PrivateCategoryMemberIndices). */
const MEMBER = devUser("bob");

async function resolvePrivateCategory(anonymous: APIRequestContext, member: APIRequestContext) {
  const listed = (await listCategories(anonymous)).find((c) => c.visibility === "private");
  if (listed) return listed;
  const token = await loginViaApi(member, MEMBER);
  return getCategory(member, PRIVATE_CATEGORY_SLUG, token);
}

async function expectPrivateFeedRejectedAnonymously(
  anonymous: APIRequestContext,
  privateCategory: CategoryResponse,
) {
  const rejected = await anonymous.get(
    `${apiUrl}/api/content/threads?categoryId=${privateCategory.id}&limit=10`,
  );
  expect(
    [403, 404],
    "the private feed must be rejected anonymously — that is the regression trigger",
  ).toContain(rejected.status());
}

function collectPageErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  return errors;
}

/** The feed rendered: a card from `general`, the heading, no error template, no uncaught errors. */
async function expectHomeFeedRendered(page: Page, pageErrors: string[]) {
  const cards = page.getByTestId("thread-card");
  await expect(cards.first()).toBeVisible();
  await expect(
    cards.filter({ hasText: PUBLIC_CATEGORY_SLUG.toUpperCase() }).first(),
    "at least one card from the public category renders",
  ).toBeVisible();
  await expect(page.getByRole("heading", { level: 1, name: "Latest threads" })).toBeVisible();
  await expectNoErrorState(page);
  expect(pageErrors, "no uncaught browser errors while rendering the feed").toEqual([]);
  return cards;
}

test.describe("home page, anonymous visitor", () => {
  test("renders the public feed and a card opens its thread", async ({ page, request }) => {
    const pageErrors = collectPageErrors(page);
    await page.goto("/");
    const cards = await expectHomeFeedRendered(page, pageErrors);

    const firstLink = cards.first().locator('a[href^="/t/"]');
    const href = await firstLink.getAttribute("href");
    const threadId = href?.match(new RegExp(`/t/(${ULID_PATTERN})$`))?.[1];
    if (!threadId) throw new Error(`thread card link is not /t/<ulid>: ${href}`);
    const detail = await getThread(request, threadId);

    await firstLink.click();
    await expect(page).toHaveURL(new RegExp(`/t/${threadId}$`));
    await expect(threadTitle(page)).toHaveText(detail.title);
    await expectNoErrorState(page);
  });

  test("survives a listed private category whose feed answers 404", async ({
    page,
    playwright,
  }) => {
    const anonymous = await playwright.request.newContext();
    const member = await playwright.request.newContext();
    const privateCategory = await resolvePrivateCategory(anonymous, member);
    expect(privateCategory.visibility).toBe("private");
    await expectPrivateFeedRejectedAnonymously(anonymous, privateCategory);

    // Reproduce the trigger exactly: the category list the SPA merges names the private category.
    // Only this response is patched; the feed call it provokes hits the real API and is rejected.
    await page.route(`${apiUrl}/api/content/categories`, async (route) => {
      const response = await route.fetch();
      const categories = (await response.json()) as CategoryResponse[];
      if (!categories.some((c) => c.id === privateCategory.id)) categories.push(privateCategory);
      await route.fulfill({ response, json: categories });
    });
    const privateFeedStatuses: number[] = [];
    page.on("response", (response) => {
      if (response.url().includes(`/api/content/threads?categoryId=${privateCategory.id}`)) {
        privateFeedStatuses.push(response.status());
      }
    });

    const pageErrors = collectPageErrors(page);
    await page.goto("/");
    await expectHomeFeedRendered(page, pageErrors);
    await expect(page.getByRole("link", { name: privateCategory.name })).toBeVisible();
    expect(
      privateFeedStatuses,
      "the SPA did request the private feed and the API rejected it",
    ).not.toEqual([]);
    expect(privateFeedStatuses.every((status) => status === 403 || status === 404)).toBe(true);

    await anonymous.dispose();
    await member.dispose();
  });

  test("a member who logs out keeps a usable home page (cached list still names the private category)", async ({
    page,
    press,
  }) => {
    const pageErrors = collectPageErrors(page);
    await loginThroughUi(page, MEMBER, press);
    // The member's category list (public + private-club) is now in the React Query cache.
    await expect(page.locator(`a[href="/c/${PRIVATE_CATEGORY_SLUG}"]`)).toBeVisible();
    await expectHomeFeedRendered(page, pageErrors);

    // Log out from another route so the home page MOUNTS again on the post-logout redirect, within
    // the list's 60 s staleTime — the merge then runs over the cached, now partly unreadable list.
    await press(page.getByRole("link", { name: "02 SEARCH", exact: true }));
    await expect(page).toHaveURL(/\/search$/);
    await logoutThroughUi(page, MEMBER, press);
    await expect(page).toHaveURL(/\/$/);

    await expectHomeFeedRendered(page, pageErrors);
  });
});
