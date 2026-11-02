# Marketplace Frontend Design Plan

Nov 2, 2026

The frontend works end to end but reads as unfinished: no product imagery, flat typography, no visual hierarchy. This plan sets one visual direction, specifies the backend work product images actually need, and splits the redesign into five tasks (T-085 to T-089) that slot into the existing backlog.

## What's wrong now

The gap is not polish, it is that nothing carries visual weight. A marketplace sells by showing the goods, and right now there are no goods to show.

| Problem | Why it matters | Fix lands in |
| --- | --- | --- |
| No product images at all | A catalogue of text rows cannot be browsed; this is the single biggest issue | T-085, T-087 |
| No type hierarchy | Every heading, price and label is the same weight, so nothing guides the eye | T-086 |
| Default browser form controls | Unstyled inputs and buttons read as a prototype, not a product | T-086 |
| No spacing system | Inconsistent gaps make the layout feel accidental | T-086 |
| No empty, loading or error states | A slow or empty screen looks broken rather than pending | T-086 |
| Dashboard numbers as plain text | Revenue figures with no chart or stat treatment waste the reporting work already built | T-089 |
| Admin queue as an unstyled list | The AI suggestion and refund decision need to be scannable and clearly separated | T-089 |

What good looks like: a visitor lands on the catalogue, sees a grid of product cards with real cover art, prices legible at a glance, and can tell within two seconds what is being sold and for how much.

## Visual direction

**Restrained and editorial: the product art carries the colour, the interface stays quiet.** Warm neutral greys, one accent colour reserved for actions, generous whitespace, strong typographic hierarchy.

This is the right call for three specific reasons, not just taste:

1. **Seller-uploaded images are unpredictable.** Cover art will arrive in every colour and style. A loud brand palette fights it; a neutral frame flatters whatever is dropped into it. This is why Gumroad, Creative Market and Itch.io all sit on near-white or near-black neutrals.
2. **One accent colour makes actions unmissable.** If only Buy, Approve and Submit carry the accent, the user never has to hunt for the next step. Spending the accent on decoration destroys that.
3. **It is implementable fast.** Neutral scale plus one accent maps directly onto Tailwind's defaults with a thin token layer, so this is a styling pass, not a rewrite.

**Explicitly avoid:** gradient hero sections, glassmorphism, drop shadows on everything, more than one accent colour, and icon-only buttons without labels. Each reads as decoration standing in for hierarchy.

**The one alternative worth considering** if this feels too plain once built: keep the same structure and swap the neutral base to near-black (a dark-first marketplace, like Itch.io or Framer). Same tokens, inverted. Worth deciding after seeing T-087 rendered rather than now.

## Design tokens

Define these once as CSS custom properties on `:root` in `web/app/globals.css`, with a dark-mode block, and reference them through Tailwind theme extension. No raw hex values anywhere in component code.

### Colour

| Token | Light | Dark | Used for |
| --- | --- | --- | --- |
| `--bg` | `#FAFAF9` | `#0C0A09` | Page background |
| `--surface` | `#FFFFFF` | `#1C1917` | Cards, panels, modals |
| `--border` | `#E7E5E4` | `#292524` | Dividers, card edges, input borders |
| `--text` | `#1C1917` | `#FAFAF9` | Primary text |
| `--text-muted` | `#78716C` | `#A8A29E` | Labels, metadata, placeholders |
| `--accent` | `#0F766E` | `#14B8A6` | Buttons, links, focus rings, active states |
| `--accent-hover` | `#115E59` | `#2DD4BF` | Hover on accent surfaces |
| `--danger` | `#B91C1C` | `#F87171` | Destructive actions, error text |
| `--success` | `#15803D` | `#4ADE80` | Paid, Completed, Fulfilled states |
| `--warning` | `#A16207` | `#FBBF24` | Pending, Executing, awaiting-review states |

The accent is a deep teal rather than the default blue or indigo, which read as generic SaaS. Teal also stays clearly distinct from the success green used in status badges.

**Contrast:** every pairing above meets WCAG AA for its use (4.5:1 for body text, 3:1 for large text and UI borders). Verify `--text-muted` on `--surface` specifically, as it is the tightest pair.

### Typography

One typeface. Use `next/font` with **Inter** (variable), `display: swap`, subset `latin`.

| Role | Size / line-height | Weight | Tracking |
| --- | --- | --- | --- |
| Page title | 32px / 1.2 | 600 | -0.02em |
| Section heading | 20px / 1.3 | 600 | -0.01em |
| Card title | 15px / 1.4 | 500 | normal |
| Body | 15px / 1.6 | 400 | normal |
| Price | 17px / 1.2 | 600 | -0.01em, tabular-nums |
| Label / metadata | 13px / 1.4 | 500 | normal |
| Micro (badges) | 11px / 1 | 600 | 0.04em, uppercase |

Use `font-variant-numeric: tabular-nums` on every price, revenue figure and table of numbers so columns align and digits do not jitter on update.

### Spacing, radius, elevation

- **Spacing scale:** 4, 8, 12, 16, 24, 32, 48, 64px only. No arbitrary values.
- **Radius:** `6px` inputs and buttons, `10px` cards, `14px` modals. Never fully rounded except avatars and badges.
- **Elevation:** one shadow only, for raised surfaces that overlay content (dropdowns, modals): `0 4px 16px rgb(0 0 0 / 0.08)`. Cards use a `1px` border, not a shadow.
- **Focus ring:** `2px` solid `--accent` with `2px` offset, on every interactive element. Never remove the outline without replacing it.
- **Transitions:** `150ms ease-out` on colour and background only. No layout or transform animation on hover.
- **Container:** max-width `1200px`, `24px` side padding, centred.

### Breakpoints

Product grid columns: 1 below 640px, 2 at 640px, 3 at 1024px, 4 at 1280px. Everything else stacks to a single column below 768px.

## Product images: the backend work

This is not a frontend task. The `products` table has `file_key` for the downloadable file and no image column at all, so cover images need a migration, an upload path, and a second storage container with different access rules.

### The critical security distinction

**The downloadable file and the cover image have opposite access requirements, and must not share a container.**

|  | Downloadable file | Cover image |
| --- | --- | --- |
| Who may fetch it | Only a buyer with a valid `download_grant` | Anyone, including unauthenticated visitors |
| URL | Short-lived signed URL, per download | Long-lived, cacheable, public |
| Container | Private (existing) | New, public-read |
| Size limit | 100 MB | 5 MB |
| Allowed types | Any permitted type | `image/jpeg`, `image/png`, `image/webp` only |

If cover images go into the private container, every catalogue page render needs signed URLs for every card, which is slow and uncacheable. If downloadable files go into the public container, the entire paid-download model breaks. Keep them separate and assert it in a test.

### Schema change

Add to `products`, in one migration, backward-compatible (nullable, no backfill required):

- `cover_image_key text null` — blob key, same pattern as `file_key`
- `cover_image_updated_at timestamptz null` — lets the public URL carry a cache-busting query parameter so a replaced image is not served stale from a CDN

No width or height columns: the grid crops to a fixed aspect ratio, so intrinsic dimensions are not needed for layout stability.

### Upload endpoint

A new endpoint on the existing product routes, reusing the validation already built in T-024:

- `PUT /api/v1/products/{id}/cover` — multipart, seller-only, must own the product
- Reuse the existing **magic-byte check** rather than trusting `Content-Type`; reject when the declared type and the actual bytes disagree
- Enforce the 5 MB limit before reading the whole body into memory
- Strip EXIF metadata on upload. Seller-uploaded photos can carry GPS coordinates, and this image is about to be served publicly to anyone
- Replacing an existing cover deletes the old blob and updates `cover_image_updated_at`
- Writes an `audit_log` row, consistent with other product mutations

### API response

Add `coverImageUrl` (nullable string, absolute) to every product response shape: list, detail, seller's own listings, and order-item snapshots. This changes the OpenAPI document, so the snapshot test from T-026 will need its diff reviewed and accepted.

### Placeholders: the thing that decides whether this looks finished

Most seeded and existing products will have no cover. A grid of grey boxes looks more broken than a grid of text rows, so the fallback has to look deliberate:

- Render a **deterministic generated cover** derived from the product id: a two-stop linear gradient picking hues from a fixed, pre-approved set of eight, with the product's first letter centred in a semi-transparent white at 600 weight
- Deterministic means the same product always gets the same cover, so the grid looks stable across reloads rather than reshuffling
- Pick the eight hues so they sit at similar saturation and lightness; random hues produce the clashing-confetti effect that makes a page look cheap
- Implement as a pure function in a React component, not generated server-side or stored, so it costs nothing and never needs cleanup

### Seed data

Update the demo seed so seeded products get real cover images, otherwise the demo screens and any screenshots still look empty. Two viable routes, both fine:

- Generate simple abstract SVG covers at seed time and upload them to the public container, or
- Leave seeded products coverless and rely on the generated placeholder above

The second is less work and still looks intentional. Choose it unless the demo video needs real-looking product art.

### Next.js configuration

Use `next/image` for every cover so it handles resizing, lazy loading and modern formats. This needs the blob host added to `images.remotePatterns` in `next.config.ts`, and `sizes` set correctly on grid images or Next will serve desktop-sized files to phones.

## Component library

Build these in `web/components/ui/` before touching any screen. Every screen then composes them, so the look stays consistent and a later change happens in one place.

| Component | Variants / states | Notes |
| --- | --- | --- |
| `Button` | primary, secondary, ghost, danger; loading, disabled | Loading shows a spinner and keeps its width so the layout does not jump |
| `Input`, `Textarea`, `Select` | default, focused, error, disabled | Error renders a message below, tied by `aria-describedby` |
| `Card` | plain, interactive (hover state) | `1px` border, `10px` radius, no shadow |
| `ProductCard` | with cover, with generated placeholder | Fixed 4:3 cover, `object-cover`, title clamped to two lines, price bottom-left |
| `Badge` | neutral, success, warning, danger | Used for every order, payment and refund status; micro type, uppercase |
| `Money` | — | Formats from amount + currency, tabular-nums; never hand-format a price inline |
| `EmptyState` | — | Icon, one line of explanation, one action. Used for empty library, no listings, empty queue |
| `Skeleton` | card, row, text | Shown while loading; matches the real content's dimensions to avoid layout shift |
| `Toast` | success, error | For submitted actions. Never use `alert()` |
| `PageHeader` | with and without actions | Title, optional description, optional right-aligned action |
| `Pagination` | — | Cursor-based, matching the keyset pagination the API already uses |
| `StatTile` | — | Dashboard figure: label, large tabular number, optional change indicator |

### Status badge colour mapping

Define this mapping once and import it everywhere, so a status never renders a different colour on two screens:

- **Success** (`--success`): Paid, Fulfilled, Completed, Published, Verified
- **Warning** (`--warning`): Pending, Executing, RequiresPayment, RefundRequested, Draft
- **Danger** (`--danger`): Cancelled, Expired, Failed, DeadLettered, Rejected
- **Neutral**: anything unmapped, so a new status added later renders legibly rather than crashing

Always pair the colour with the status word. Colour alone fails for colour-blind users and in greyscale print.

### Accessibility floor

Non-negotiable, and cheap if done while building rather than retrofitted:

- Every input has a real `<label>`, not a placeholder standing in for one
- Focus visible on every interactive element, keyboard order follows visual order
- Status and error messages announced via `aria-live` where they appear after an action
- All images have `alt` text; a generated placeholder cover is decorative, so `alt=""`
- Buttons that trigger destructive actions (refund, unpublish) confirm first

## Screen by screen

### Shell (every page)

Sticky top bar, `64px`: wordmark left; search field centre on catalogue pages; right side shows Library, Sell and an account menu when signed in, or Sign in and Create account when not. Below `768px` the nav collapses to a sheet. Footer is a single quiet line with a GitHub link — no fake company links.

### Catalogue (the screen that matters most)

This is the first impression and the one that currently fails hardest.

- Product grid: 1 to 4 columns by breakpoint, `24px` gap
- Each `ProductCard`: 4:3 cover, title (two-line clamp), seller name in muted type, price bottom-left in tabular numerals
- Hover: border shifts to `--accent`, nothing moves or scales
- Filters in a left rail at `1024px` and above, collapsing to a sheet below: category, price range, sort
- Search sits in the top bar, submits on Enter, keeps its term visible in the field after navigation
- Loading: skeleton cards matching the real grid, not a spinner
- Empty: `EmptyState` saying no products match, with a clear-filters action

### Product detail

Two columns at `1024px` and above, stacking below.

- Left: cover at 4:3, `10px` radius
- Right: title at page-title size, seller line, price, Buy button (primary, full width of the column), then description
- Description renders as prose with real paragraph spacing, not a single wall of text
- Metadata (category, file type, size) in a definition list under the fold, label in muted type
- If the viewer already owns it, the Buy button is replaced by Go to library — do not show a buy action for something already purchased
- If the viewer is the seller, show Edit listing instead

### Checkout

- Single narrow column, `480px` max, centred: this screen should feel focused, not spacious
- Order summary at top: cover thumbnail, title, price, total in tabular numerals
- Pay button is the only primary action on the page
- While the payment settles, the button goes to its loading state and stays disabled — a double-submit here is exactly what the idempotency work protects against, and the UI should not invite it
- On success, route to the library with a success toast, not to a dead-end confirmation page

### Library

- Row-based list, not the card grid: these are owned items, and the useful information is the download, not the art
- Each row: small cover thumbnail, title, purchase date, Download button, Request refund link in muted type
- Download triggers the signed URL; show a loading state while it is fetched, since that is a round trip
- Refund request opens a dialog with a reason textarea and a clear note that an admin reviews it
- After requesting, the row shows a `RefundRequested` badge and hides the refund link
- Empty: `EmptyState` pointing to the catalogue

### Orders

- Table at `768px` and above, stacked cards below
- Columns: order id (shortened, monospace), date, items, total, status badge
- Newest first
- Row expands or links to detail showing the items and payment state

### Seller dashboard

The reporting work behind this is substantial and the screen should show it off.

- A `StatTile` row across the top: total revenue, orders, refund rate, active listings. Large tabular numbers, muted labels
- One revenue-over-time chart below, drawn from the monthly revenue report that already exists. A line chart, single series, one y-axis starting at zero, months on x
- Top products as a compact table underneath: title, units, revenue
- Listings management section: the seller's own products as rows with status badges and edit actions
- **When implementing the chart, load the `dataviz` skill first** — it carries the palette, axis and label rules, and a chart built without it will not match the rest of this system

### Admin refund queue

The AI suggestion is the interesting part of this screen and needs to read as advisory, not as a decision already made.

- Queue as a table: requested date, buyer, product, amount, AI recommendation as a badge, status
- Detail view in two columns: left the facts (order, amount, download count, buyer refund history), right the AI suggestion
- The AI panel is visually distinct — a bordered surface with a clear label such as "AI suggestion (advisory)" — showing category, recommendation, confidence and reasons
- Approve and Reject are equally weighted buttons; Approve is primary, Reject is secondary, neither is hidden
- Both require confirmation, and the override-reason field appears when the admin's decision contradicts the AI recommendation
- Never auto-select a decision based on the AI's recommendation. The whole design intent of this feature is that a human decides

### Auth screens

- Narrow centred column, `400px`
- Real labels, inline validation on blur, errors below each field
- Password requirements shown before submission, not only after a rejection
- Verify-email and reset-password pages need clear success and failure states, since users arrive there from an email link with no context

## Tasks to add to the backlog

T-085 to T-089 are free in the Phase 7 range (T-084 is the last existing Phase 7 task, Phase 8 starts at T-090), so these slot in without renumbering anything.

| Task | What it does | Blocked by | Done when |
| --- | --- | --- | --- |
| T-085 | Product cover images, backend: migration adding `cover_image_key` and `cover_image_updated_at`; public-read blob container; `PUT /products/{id}/cover` with magic-byte check, 5 MB limit and EXIF stripping; `coverImageUrl` on all product responses | T-024 | Seller uploads a cover, it returns in the product response, and a test asserts downloadable files and cover images use different containers |
| T-086 | Design foundation: tokens in `globals.css` with dark mode, Tailwind theme extension, Inter via `next/font`, and the full component library including the generated placeholder cover | T-083 | Every component renders in all its states; no raw hex in component code |
| T-087 | Catalogue and product detail redesigned onto the new components, with `next/image` covers and `remotePatterns` configured | T-085, T-086 | Grid is responsive 1–4 columns, covers load, placeholders render for coverless products |
| T-088 | Library, orders, checkout and auth screens redesigned, including loading, empty and error states | T-086 | Every flow works with no unstyled controls and no layout shift on load |
| T-089 | Seller dashboard and admin refund queue redesigned, with the stat tile row, the revenue chart, and the advisory AI panel | T-086, T-073 | Dashboard renders the chart from real report data; AI suggestion reads as advisory and never pre-selects a decision |

### Order and why

T-085 and T-086 are independent of each other and both block the screen work, so they can run in either order or in parallel. T-087 needs both. T-088 and T-089 only need T-086, so they can proceed while image work finishes if that stalls.

Do **not** start T-087 before T-086 lands. Redesigning a screen before the component library exists means building the components inline and then refactoring them out, which is the slower path.

### What to tell Claude Code

```markdown
Add T-085 to T-089 to the backlog from the frontend design plan, in Phase 7, and
record them in docs/spec-changelog.md as an addition (the UI redesign and product
cover images were not in the original spec).

Start with T-085 and T-086. For T-085, note that cover images need their own
public-read blob container, separate from the private container holding
downloadable files - assert that separation in a test. Strip EXIF on upload.

For T-086, define all tokens as CSS custom properties with a dark-mode block,
extend the Tailwind theme from them, and use no raw hex values in component code.

When you reach the dashboard chart in T-089, load the dataviz skill first.

Stop and ask before T-087 if anything in the token set looks wrong once rendered.
```

### One decision worth making after T-087

The direction above is light-first with a teal accent. Once the catalogue is rendered with real covers, look at it and decide whether to keep it or invert to a dark base — same tokens, same structure, different values. That judgement is much easier with a real screen in front of you than in the abstract, and switching costs an hour at that point rather than a rebuild.
