/**
 * Shared helpers for the Playwright suite: the Development-seed cast, thin typed calls to the API
 * for test PRECONDITIONS only (the behaviour under test always goes through the rendered SPA), the
 * UI login/logout journeys, and the `press` fixture that drives buttons with a mouse click on the
 * desktop project and a finger tap on the android project. Every API call here is anonymous unless
 * a bearer token is passed explicitly, exactly like a logged-out visitor; the seed facts (names,
 * password, category slugs) come from backend/src/Shared/Forum.Infrastructure/Seeding/SeedPlan.cs
 * and the module seeders.
 */

import {
  expect,
  test as base,
  type APIRequestContext,
  type Locator,
  type Page,
} from "@playwright/test";

export const apiUrl = (process.env.E2E_API_URL ?? "http://localhost:8080").replace(/\/+$/, "");

/** Every Development-seed account shares this password (SeedPlan.For(Development)). */
export const DEV_PASSWORD = "Dev#Password1";

export type DevUsername = "admin" | "mod" | "alice" | "bob" | "charlie";

export interface DevUser {
  username: DevUsername;
  email: string;
  password: string;
}

export function devUser(username: DevUsername): DevUser {
  return { username, email: `${username}@dev.local`, password: DEV_PASSWORD };
}

/** The Development seed's public category; every browse test anchors on it. */
export const PUBLIC_CATEGORY_SLUG = "general";
/** The Development seed's private category — the one whose feed 404s for anonymous readers. */
export const PRIVATE_CATEGORY_SLUG = "private-club";

/** ULID: 26 Crockford base32 characters — every id in a URL (ADR 0006). */
export const ULID_PATTERN = "[0-9A-HJKMNP-TV-Z]{26}";

export interface CategoryResponse {
  id: string;
  slug: string;
  name: string;
  visibility: "public" | "private";
  ownerId: string;
}

export interface ThreadFeedItemResponse {
  id: string;
  categoryId: string;
  categorySlug: string;
  categoryName: string;
  title: string;
  isPinned: boolean;
  ownerId: string;
  username: string;
  displayName: string;
  createdOnUtc: string;
}

export interface ThreadDetailResponse extends ThreadFeedItemResponse {
  body: string;
  tags: string[];
}

export interface CommentResponse {
  id: string;
  threadId: string;
  body: string;
  isDeleted: boolean;
  ownerId: string;
  username: string;
}

export interface CursorPage<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

interface ApiCallOptions {
  token?: string;
  method?: "GET" | "POST";
  data?: unknown;
}

/** One JSON round-trip to the API; a non-2xx answer fails loudly with the body in the message. */
export async function apiJson<T>(
  request: APIRequestContext,
  path: string,
  options: ApiCallOptions = {},
): Promise<T> {
  const method = options.method ?? "GET";
  const response = await request.fetch(`${apiUrl}${path}`, {
    method,
    headers: options.token ? { Authorization: `Bearer ${options.token}` } : undefined,
    data: options.data,
  });
  if (!response.ok()) {
    throw new Error(`${method} ${path} → ${response.status()}: ${await response.text()}`);
  }
  return (await response.json()) as T;
}

export function listCategories(request: APIRequestContext, token?: string) {
  return apiJson<CategoryResponse[]>(request, "/api/content/categories", { token });
}

export function getCategory(request: APIRequestContext, slug: string, token?: string) {
  return apiJson<CategoryResponse>(request, `/api/content/categories/${slug}`, { token });
}

export function getThreadFeed(request: APIRequestContext, categoryId: string, token?: string) {
  return apiJson<CursorPage<ThreadFeedItemResponse>>(
    request,
    `/api/content/threads?categoryId=${categoryId}&limit=20`,
    { token },
  );
}

export function getThread(request: APIRequestContext, threadId: string, token?: string) {
  return apiJson<ThreadDetailResponse>(request, `/api/content/threads/${threadId}`, { token });
}

export function getCommentTree(request: APIRequestContext, threadId: string, token?: string) {
  return apiJson<CommentResponse[]>(request, `/api/content/threads/${threadId}/comments`, {
    token,
  });
}

/** Logs in through the API (NOT the UI) and returns the access token — for preconditions only. */
export async function loginViaApi(request: APIRequestContext, user: DevUser): Promise<string> {
  const { accessToken } = await apiJson<{ accessToken: string }>(request, "/api/identity/login", {
    method: "POST",
    data: { email: user.email, password: user.password },
  });
  return accessToken;
}

/** The public seed category and its first feed page (newest first); throws when the seed is missing. */
export async function publicCategoryWithThreads(request: APIRequestContext) {
  const categories = await listCategories(request);
  const category = categories.find((c) => c.slug === PUBLIC_CATEGORY_SLUG);
  if (!category) {
    throw new Error(
      `Development seed not loaded: category '${PUBLIC_CATEGORY_SLUG}' is missing (make seed).`,
    );
  }
  const feed = await getThreadFeed(request, category.id);
  if (feed.items.length === 0) {
    throw new Error(`Development seed not loaded: '${PUBLIC_CATEGORY_SLUG}' has no threads.`);
  }
  return { category, threads: feed.items };
}

/** A public seed thread with at least one live comment (the comment tree is part of the browse checks). */
export async function publicThreadWithComments(request: APIRequestContext) {
  const { threads } = await publicCategoryWithThreads(request);
  for (const thread of threads) {
    const comments = await getCommentTree(request, thread.id);
    if (comments.some((comment) => !comment.isDeleted)) return { thread, comments };
  }
  throw new Error(
    `No thread in '${PUBLIC_CATEGORY_SLUG}' has a live comment — reseed (make seed ARGS=--force).`,
  );
}

/**
 * The texts of the SPA's error templates (components/ui/ErrorState.tsx). None of them may be on a
 * page that is supposed to have rendered its data.
 */
export async function expectNoErrorState(page: Page) {
  await expect(page.getByText("SIGNAL LOST")).toHaveCount(0);
  await expect(page.getByText("Something broke on our side")).toHaveCount(0);
  await expect(page.getByText("Category not found")).toHaveCount(0);
  await expect(page.getByText("Not found", { exact: true })).toHaveCount(0);
}

/**
 * The thread page's title heading. Seeded bodies open with a markdown H1 that repeats the title, so
 * `getByRole("heading", { level: 1 })` alone is ambiguous — the article's own title comes first.
 */
export function threadTitle(page: Page): Locator {
  return page.getByRole("heading", { level: 1 }).first();
}

/** Activates a control the way the project's input does: a mouse click (desktop) or a finger tap (android). */
export type Press = (locator: Locator) => Promise<void>;

/**
 * A finger tap at the control's centre, as a raw touch event. Used instead of `locator.tap()` once
 * a user is signed in on the phone: the signed-in TopNav is wider than the Pixel 7 (see
 * mobile-layout.spec.ts), Chrome then widens the layout viewport beyond the visual one and pans the
 * visual viewport to focused fields, and Playwright's own hit-target check compares a point taken
 * from the visual viewport against layout coordinates — it reports an unrelated element
 * "intercepting pointer events" although the real hit test is clean. The hit test is therefore
 * done here in page coordinates (the CDP box translated by the visual viewport's pan and scale):
 * whatever is on top at the centre must be the target itself or one of its descendants, so a
 * genuinely covered control still fails loudly.
 */
export async function tapAt(page: Page, locator: Locator) {
  await locator.scrollIntoViewIfNeeded();
  await expect(locator).toBeVisible();
  const box = await locator.boundingBox();
  if (!box) throw new Error("tapAt: the target has no bounding box");
  const point = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  const covering = await locator.evaluate((target, at) => {
    const viewport = window.visualViewport;
    const scale = viewport?.scale ?? 1;
    const x = at.x / scale + (viewport?.offsetLeft ?? 0);
    const y = at.y / scale + (viewport?.offsetTop ?? 0);
    const top = document.elementFromPoint(x, y);
    if (!top || target.contains(top)) return null;
    const id = top.id ? `#${top.id}` : "";
    const cls = typeof top.className === "string" ? `.${top.className.split(" ")[0]}` : "";
    return `${top.tagName.toLowerCase()}${id}${cls} at layout (${x.toFixed(0)}, ${y.toFixed(0)})`;
  }, point);
  if (covering) {
    throw new Error(`tapAt: ${covering} covers the target tapped at (${point.x}, ${point.y})`);
  }
  await page.touchscreen.tap(point.x, point.y);
}

/** `test` with the `press` fixture: click on desktop, tap (raw touch, hit-tested) on android. */
export const test = base.extend<{ press: Press }>({
  press: async ({ page, isMobile }, provide) => {
    await provide(async (locator) => {
      if (isMobile) await tapAt(page, locator);
      else await locator.click();
    });
  },
});

export { expect };

/** Logs in through the /auth page (the real form, not the API) and waits for the signed-in chrome. */
export async function loginThroughUi(page: Page, user: DevUser, press: Press) {
  await page.goto("/auth");
  await page.getByLabel("Email").fill(user.email);
  await page.getByLabel("Password").fill(user.password);
  // exact: the "LOG IN" tab button would otherwise match too (role-name matching is case-insensitive).
  await press(page.getByRole("button", { name: "Log in", exact: true }));
  await expect(page.getByRole("button", { name: user.username })).toBeVisible();
  await expect(page).toHaveURL(/\/$/);
}

/** Opens the TopNav user menu and logs out; resolves once the anonymous chrome is back. */
export async function logoutThroughUi(page: Page, user: DevUser, press: Press) {
  const menu = page.getByRole("button", { name: user.username });
  await press(menu);
  await press(page.getByRole("button", { name: "Log out", exact: true }));
  await expect(page.getByRole("link", { name: "Log in", exact: true })).toBeVisible();
  await expect(menu).toHaveCount(0);
}

/** A unique marker for rows this run creates (the seed is shared and never cleaned up). */
export function runStamp(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
}
