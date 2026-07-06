# .NET Forum Design System

A modern, production-grade design system for the **.NET Forum** — a professional React SPA + .NET 10 modular monolith forum application built as an academic thesis project comparing architecture patterns.

## Project Context

This design system serves the **forum-dotnet** repository (React SPA frontend paired with a .NET 10 backend implementing Clean Architecture + modular monolith patterns). The system is derived from the reference implementation **Python-Forum-API** (a production fishing community forum by Jakub Patkowski) and tailored for a **dark-only, subtly cyberpunk aesthetic** with neon-orange accents.

### Sources & References

- **Backend spec (source of truth):** `forum-dotnet/CLAUDE.md` + `forum-dotnet/docs/` architecture documentation
- **API contract:** Detailed in the brief §4 (backend modules: Identity, Content, Files, Engagement; real ULID-keyed routes, JWT auth, WebSocket realtime, soft-delete semantics)
- **Reference UI:** `JakubPatkowski/Python-Forum-API` (GitHub) — production fishing forum; explore for stylistic/structural inspiration
- **Thesis context:** Master's degree comparing React SPA + .NET 10 (Architecture A) vs. Go SSR monolith (Architecture B) across five research categories

## Visual Direction

**Tone:** Modern, professional, subtly cyberpunk — a tech/science/programming-adjacent community forum, **not over the top**. Think bracketed nav labels (`01 FORUM`, `02 CATEGORY`), monospace accents, sharp typography, faint grid/scanline texture where appropriate.

**Color Palette:**
- **Dark-only theme:** `#07090d` (nearly black) through `#f7fafc` (almost white)
- **Neon-orange accent:** `#ff6b35` primary, with dark/light variants for interactive states
- **Cyan support:** `#00d9ff` sparingly, for highlights and texture (grid overlays)
- **Semantic colors:** green (success), amber (warning), red (error), blue (info)

**Typography:**
- **Sans-serif:** Space Grotesk (headline-forward scale, modern and geometric)
- **Monospace:** JetBrains Mono (code blocks, inline code, tech accents)
- **Scale:** Generous headline sizing (min 18px for h4, 24px for h2); body is 16px baseline with 1.6 line-height

**Spacing & Layout:**
- **Base unit:** 4px (0.25rem) for crisp alignment
- **Corners:** Sharp (no/minimal border-radius) — `0` to `0.5rem` max
- **Shadows:** Dark-appropriate monochromatic shadows (0.24–0.56 alpha)
- **Grid-based layout:** Flexbox + CSS Grid with explicit `gap`; no floating or margin-stacking

**Animation & Motion:**
- **Hover states:** Color shift (lighter accent), subtle opacity, smooth transitions (0.15s)
- **Focus states:** 2px solid outline in accent color
- **Pulse/highlight:** Deliberate animation for realtime content arrival (new thread, live reaction, WS status) — a first-class part of the identity

**Iconography:**
- **Style:** Filled icons, consistent stroke weight
- **Use:** Navigation, actions, statuses, category/tag badges
- *(Icon assets to be sourced from codebase or CDN; not hand-drawn SVG)*

## Content Fundamentals

**Copy tone:** Professional, direct, action-oriented. Second-person where appropriate ("Post a reply", "Manage your profile"). **No** "delightful" flourishes or emojis (unless part of user content, e.g., category names).

**Casing:** Sentence case for buttons/labels (capitalize first word + proper nouns only). Title case for page headers.

**Language:** English throughout (code, UI, docs). Polish appears only in user-generated content or thesis prose.

**API-aware:** Copy reflects the actual backend contract — e.g., "Comment depth exceeds 5 levels" (a real 422 error), "Private category — moderator access only", "WebSocket disconnected — trying to reconnect".

## Visual Foundations

### Colors

**Dark theme only.** Neutrals form a 10-step gradient from `#07090d` (bg) to `#f7fafc` (text). Neon orange `#ff6b35` is the primary interactive accent, with semantic colors for feedback:

- **Success:** `#10b981` (green)
- **Warning:** `#f59e0b` (amber)
- **Error:** `#ef4444` (red)
- **Info:** `#3b82f6` (blue)

Cyan `#00d9ff` is used sparingly as a secondary accent and for texturing (grid overlays, subtle background patterns). All surfaces are intentionally dark and opaque to establish a strong visual hierarchy.

### Typography

**Display type (headlines):** Space Grotesk, bold or semibold, geometric and modern. Used for page titles and marketing moments.

**Body type:** Space Grotesk, regular weight, 16px baseline. Generous line-height (1.6) for readability. Markdown-rendered content inherits these scales (h1–h3 for headers, numbered/bulleted lists, blockquotes, tables, code blocks).

**Monospace:** JetBrains Mono for `<code>`, `<pre>`, and inline code snippets. Unifies the tech/programming identity.

### Spacing & Rhythm

Built on a **4px base unit** for crisp, intentional layouts. Spacing tokens (`--space-*`) scale from 4px (`--space-1`) to 96px (`--space-24`), with semantic shortcuts (`--space-xs` through `--space-2xl`). Components use consistent internal padding and gaps between sibling elements.

Card/panel padding is typically `--space-4` to `--space-6` (16–24px). Grid gaps match the spacing scale. **No collapsing margins** — gaps are explicit.

### Corners & Shadows

**Corners:** Sharp. Minimal rounding: `0` to `0.5rem` max (buttons/inputs `0.25rem`, cards `0.5rem`). This reinforces the crisp, professional aesthetic.

**Shadows:** Monochromatic, dark-theme appropriate. Layered from `--shadow-xs` (lightest, 24% black) to `--shadow-2xl` (darkest, 56% black). Rarely used — only on floated/modal elements for depth separation.

### Hover & Interactive States

**Buttons:**
- Default: accent color, no background (ghost), or dark bg with accent text (primary/secondary)
- Hover: lighter accent color, subtle bg color shift
- Focus: 2px outline in accent color
- Active: darker accent (press feedback)
- Disabled: 50% opacity, cursor: not-allowed

**Inputs:**
- Default: dark secondary bg, subtle border
- Focus: accent border, tertiary bg
- Disabled: text-tertiary color, no border shift

**Links:**
- Default: accent color
- Hover: lighter accent
- Focus: outline

All transitions are `0.15s ease` for snappy, intentional feedback.

### Borders

Default border color is `--color-border-default` (`--color-neutral-700`). Accent borders are orange. Border width is typically `1px`.

### Backgrounds & Overlays

Modal overlays use `--color-surface-overlay` (`rgba(7, 9, 13, 0.8)`). Ensures legibility of layered content without overwhelming the dark palette.

### Loading & Empty States

**Skeletons:** Pulse animation on secondary surface color; never full-page spinners.

**Empty states:** Centered icon + heading + secondary text + CTA; visually balanced, not cramped.

**Error states:** Inline field errors below inputs (red text + icon). Toast for async errors. 404/403/422 have dedicated error-page templates.

### Markdown Rendering

Thread/comment bodies are raw Markdown (no HTML enforcement server-side). Frontend renders via a sanitized Markdown renderer (DOMPurify) with the typography scale above. Headings, lists, blockquotes, code blocks, tables all styled to match the design system's hierarchy and spacing.

### Animation & Realtime Feedback

Realtime notifications (new thread, live reaction count, WS status) trigger a subtle **pulse/highlight** animation — a first-class design element. The animation is a key part of the identity and communicates "content is live and updating" without being distracting. CSS `@keyframes` with `prefers-reduced-motion` gate.

## Component Inventory

### Layout & Navigation

- **TopNav** — Horizontal primary navigation with logo, nav items (bracketed labels), user menu
- **Sidebar** — Category list, user stats (own profile only)
- **MainContent** — Centered main container with max-width
- **PageHeader** — Title, breadcrumbs, action buttons

### Forms & Inputs

- **Button** — primary, secondary, ghost, danger; with loading state
- **Input** — text, email, password, number; with label, error state
- **Textarea** — multi-line input for Markdown body/comment
- **Select** — dropdown for categories, filters
- **Checkbox** — for form toggles
- **Radio** — for single-choice selections
- **TagInput** — multi-select tag picker with autocomplete dropdown (keyset badge chips)

### Content & Display

- **Card** — container for thread summaries, comments, categories
- **ThreadCard** — title, author, stats (pinned badge, like count TBD), excerpt
- **CommentNode** — nested tree node, author, body (rendered Markdown), actions, depth indication
- **CategoryCard** — icon, name, description, thread count
- **UserCard** — avatar, name, stats (threads, comments, karma), role badges
- **Badge** — role (user/moderator/admin), visibility (public/private), status (pinned/deleted)
- **Tag** — chip-style, for thread tags, clickable to filter
- **Reaction** — like button with count, idempotent toggle

### Feedback & Status

- **Toast** — ephemeral message (success, error, warning, info)
- **Modal** — overlay dialog for confirmations, large forms
- **Skeleton** — placeholder while loading
- **EmptyState** — visual feedback for no-data scenarios
- **ErrorState** — 404, 403, 422, 429 (generic "slow down") templates
- **RealtimeStatusBadge** — WebSocket connection status (connected, reconnecting, offline)
- **ProgressBar** — file upload progress (uploading → processing → ready)

### Data Visualization

- **MarkdownRenderer** — sanitized Markdown output (headings, lists, code, blockquotes, tables)
- **LoadMore** — "Load more" button for keyset pagination (never page numbers)
- **InfiniteScroll** — scroll-to-load trigger (alternative to Load More)

## File & Upload Handling

Two-phase presigned flow (initiate → client PUT to MinIO → commit). Three visible states:

1. **Uploading** — progress bar during raw PUT
2. **Processing** — during commit (dimensions/type verification)
3. **Ready** — preview + delete option

Supported: PNG, JPEG, GIF, WebP. Max 5 MiB per file. Attachment cap: 10 per thread/comment.

## Permissions & Authorization

Frontend shows edit/delete/pin controls based on ownership heuristic (`currentUser.id === ownerId`), but always handles a **403 on the actual action** gracefully. Permission is resolved server-side; client-side UI is a UX nicety, not a security boundary.

- **Edit/Delete:** Owner or moderator of the category
- **Pin/Change Category:** Moderator of the category only
- **Comment depth:** Cap at 5 levels; disable "Reply" at depth 5
- **Private categories:** Badge-only; no client-side visibility filter (not enforced server-side yet)

## Realtime Updates

WebSocket subscription model (subscribe to category/thread/user views). A change notification triggers a **pulse/highlight animation** on the affected content, then the SPA re-fetches and patches in place. Never silently reorder.

Notification types: Thread/Comment Created/Updated/Deleted, Reaction Added/Removed.

## Page Inventory

Based on the API contract (§4 of the brief):

1. **Home / Feed** — pinned threads first, latest threads below; category sidebar
2. **Category Page** — threads scoped to category; most-popular callout
3. **Thread Detail** — title, body (Markdown), tags, nested comments (max depth 5), reactions, attachments
4. **Create/Edit Thread** — title input, body editor (Markdown), category select, tag picker
5. **Profile (Own & Others)** — avatar, username, stats (threads, comments, karma), recent activity
6. **Search Results** — full-text search results (keyset paginated), filters by category/tag/author
7. **Login / Register** — forms with validation, remember-me option
8. **Admin Panel** — user list, role/permission assignment, ACL editor, block/unblock UI
9. **Notifications / Activity** — realtime activity indicator, WebSocket connection status
10. **Error Pages** — 404, 403, 422, 429, generic error boundary

## Referenced Repositories

- **Backend implementation:** `forum-dotnet/` (this project's backend, .NET 10, modules: Identity/Content/Files/Engagement)
- **Reference implementation:** `JakubPatkowski/Python-Forum-API` (GitHub, fishing forum, for stylistic/structural guidance only — not an API source)
- **Design brief:** `forum-dotnet/CLAUDE.md` (Phase 8 Frontend: React SPA + WebSocket realtime + keyset pagination)

## Token Structure

All design values are CSS custom properties under `:root`. No Figma/SCSS/Tailwind — pure CSS for maximum portability.

- **Colors:** `--color-*` (neutrals, accent, semantic, surfaces, borders)
- **Typography:** `--font-*`, `--font-size-*`, `--line-height-*`, `--letter-spacing-*`
- **Spacing:** `--space-*`, `--padding-*`, `--gap-*`, `--radius-*`, `--shadow-*`
- **Interactive:** `--ring-width`, `--ring-color`

Entry point: `styles.css` (@imports all token files + base resets + utility classes).

## Usage Notes

1. Copy `styles.css` and `tokens/` into your project root
2. Link `<link rel="stylesheet" href="./styles.css">` in your HTML
3. Components reference tokens via CSS variables (no build step needed)
4. For React, load the design system bundle (compiled components) and import reusable primitives

## Next Steps

Consuming projects (e.g., the forum-dotnet frontend in React):

1. Load this design system's `styles.css`
2. Import reusable components (Button, Input, Card, etc.) from the compiled bundle
3. Compose pages using the component library and typography/spacing/color tokens
4. Customize via token overrides if needed (e.g., accent color per org)

---

**Version:** 0.1.0  
**Status:** Foundation complete (tokens, base styles, component library in progress)  
**Last updated:** 2026-07-05
