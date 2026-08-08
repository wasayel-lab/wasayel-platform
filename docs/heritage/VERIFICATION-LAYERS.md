# The Verification Layers — what each catches (and why none overlap)

Five **static** layers plus one **runtime** layer give near-complete coverage
without manual screenshot review.  Each layer answers exactly one question;
no question is answered by two layers.

## Quick reference

| Script | Layer | Question | Scope | Unique to this layer |
|---|---|---|---|---|
| `verify-page-structure.sh` | 1 — Code hygiene | "Does every page use widgets correctly?" | lexical | Raw `<button>`, Bootstrap classes, inline hex in razor |
| `verify-css.sh` | 2 — Class existence | "Does every class used in razor exist in CSS?" | semantic | Undefined classes, typos |
| `verify-design-tokens.sh` | 3 — Per-value scale | "Is THIS value on the allowed scale?" | per-value | Off-scale font-size/spacing/icon-size, deep nesting |
| `verify-design-quality.sh` | 4 — Per-app diversity | "How many distinct values does THIS app ship?" | per-app aggregate | Color / spacing / font-size / icon-size counts, widget distribution, sibling symmetry |
| `verify-widget-contracts.sh` | 5 — Property contracts | "Does each widget declare its required properties?" | per-widget | Missing padding / border / background |
| `verify-runtime.mjs` | 6 — Spatial contracts | "Do rendered elements sit where they should?" | runtime | Overlap, alignment, position, computed style |

## Scope separation — why no check is duplicated

The layers are stacked by **abstraction level**, not by feature area. Each
value in the codebase is inspected by at most one aggregate check:

- **Is it present?** → Layer 1 (lexical) and Layer 2 (semantic)
- **Is it valid?** → Layer 3 asks per-value; Layer 5 asks per-widget
- **How many distinct values?** → Layer 4 asks per-app aggregate
- **Does it render correctly?** → Layer 6

Concretely:

| Concern | Sole owner | Not in… |
|---|---|---|
| Raw `#hex` in razor inline style | Layer 2 rule 8 | (removed from Layer 3 — was duplicate) |
| Distinct color count | Layer 4, **per-app** (limit 60) | (removed from Layer 3 — was global count) |
| Off-scale icon `Size="..."` | Layer 3 (per-value) | (removed from Layer 4 — kept only count) |
| Distinct icon size count | Layer 4 (per-app, limit 6) | — |
| Font-size value on scale | Layer 3 (per-value) | — |
| Font-size distinct count | Layer 4 (per-app, limit 10) | — |
| Spacing value on 4-px grid | Layer 3 (per-value) | — |
| Spacing distinct count | Layer 4 (per-app, limit 20) | — |

## Static vs runtime — what Layer 6 does and does NOT repeat

The first five layers read source files. Layer 6 loads rendered pages with
a real browser (Playwright). The two are **complementary**, not duplicates.
This matrix shows the rule:

| Static layer | Does Layer 6 re-run it? | Why / why not |
|---|---|---|
| **1 — code hygiene** (forbidden patterns) | ❌ No | The rendered HTML contains only what the source emitted. Blazor Server does not synthesize `<button>` or `.btn` out of thin air. Static is authoritative. |
| **2 — class existence** | ⚠ Partially | Static confirms `.foo` is *defined*. Runtime additionally detects: (a) CSS bundle failed to load, (b) a later rule overrode `.foo`, (c) specificity war made the rule inert. These are orthogonal failures and live in the Layer 5 computed-style assertions of Layer 6. |
| **3 — per-value scale** (inline values) | ⚠ Partially | Static only inspects *inline* values. Runtime `getComputedStyle` reveals the *cascaded* final value — a font-size of `17px` can appear in the computed layer without ever appearing in source (inherited `rem`, browser default, media query, etc.). |
| **4 — per-app diversity counts** | ❌ No | Distinct-value counts at runtime are always **≤** static counts — a source that declares 40 colors cannot render 41. Static is an **upper bound**, so if static passes, runtime passes. Re-running it at runtime adds zero information. |
| **5 — widget property contracts** | ⚠ Partially | Static confirms `.ac-input` *declares* `padding` in CSS. Runtime confirms the padding *actually applied* to the rendered element after the cascade. Layer 6 re-checks computed values for every contracted widget. |

What **only** Layer 6 can catch (no static equivalent exists):

- **Position** — sticky bars actually at `top: 0` on scroll; bottom navs
  glued to viewport bottom; anchored badges attached to parent edge.
- **Containment** — child elements not escaping their parent's bounding
  box horizontally or vertically (overflow issues).
- **Alignment** — flex/grid siblings on the same horizontal centre-line.
- **Overlap** — sibling cards not stacking on top of each other.
- **Contrast** — computed foreground vs. background `rgb` pair hitting
  WCAG AA 4.5:1 ratio (inherited colours matter; source can't predict).

The principle: **if a check only needs source, it lives in layers 1–5;
if it needs the rendered tree, it lives in Layer 6.** Nothing is measured
twice.

## Why per-app for Layer 4?

A user loads exactly **one** app at a time.  Global counts summed across all
apps (≈95 colors platform-wide) misrepresent what any single user sees.
Layer 4 resolves each app's CSS scope from its `.csproj`:

```
scope = app's CSS + each referenced libs/frontend/<Lib>/*.css
```

With that scope, every current app ships fewer than 60 colors (range 22–42),
fewer than 20 spacing values, 8 font-sizes, and at most 2 icon sizes —
well inside the well-known usability limits.

## Layer-by-layer

### Layer 1 — Code Hygiene (`verify-page-structure.sh`)
"Are pages written against the component library correctly?"  Forbids raw
`<button>`, Bootstrap classes (`row`, `col-md-*`, `btn`, `card`, `alert alert-*`,
`spinner-border`, `form-control`) and inline hex colors.  **Blocks CI.**

### Layer 2 — Class Existence (`verify-css.sh`)
"Does every class used in razor resolve to a CSS rule?"  Catches typos and
dangling references.  **Blocks CI.**

### Layer 3 — Per-Value Scale (`verify-design-tokens.sh`)
"Is each individual value on the allowed scale?"  Inspects every inline
`font-size`, `padding`, `margin`, `AcIcon Size=`, and razor nesting depth.
Does **not** count distinct values — that is Layer 4's job.  **Blocks CI.**

### Layer 4 — Per-App Diversity (`verify-design-quality.sh`)
"How many distinct values does each individual app ship?"  Iterates each
`Apps/*/Frontend/*.csproj`, resolves its referenced libs, measures:

| Metric | Hard limit per app | Soft limit per app |
|---|---|---|
| Distinct colors | 60 | 50 |
| Distinct spacings | 20 | 12 |
| Distinct font-sizes | 10 | 8 |
| Distinct icon sizes | 6 | — |

Also reports top-10 widget usage and container-hierarchy conformance.
**Report-only (exit 0)** — intended to trend over time.

### Layer 5 — Widget Property Contracts (`verify-widget-contracts.sh`)
"Does each contracted widget declare padding, border, background, color,
font-size, border-radius as required?"  Catches MISSING properties
(Layer 3 catches WRONG values).  **Blocks CI.**

### Layer 6 — Runtime Spatial Contracts (`verify-runtime.mjs`)
Playwright loads each page and asserts positions, alignments, overlap,
and computed styles from `spatial-contracts.json`.  **Blocks CI.**

## The full pipeline

```bash
./scripts/verify-page-structure.sh      # Layer 1 — build breaker
./scripts/verify-css.sh                 # Layer 2 — build breaker
./scripts/verify-design-tokens.sh       # Layer 3 — build breaker
./scripts/verify-design-quality.sh      # Layer 4 — report (per-app)
./scripts/verify-widget-contracts.sh    # Layer 5 — build breaker
./scripts/verify-runtime.sh             # Layer 6 — build breaker (Playwright)
```

## What this does NOT cover

- **Computed color-contrast** beyond what Layer 6 asserts (WCAG AA is an
  explicit spatial-contracts entry, not a universal sweep)
- **Animation timing / jank**
- **Viewport overflow on narrow mobile** (can be added to Layer 6 with a
  viewport preset)

## For the designer / reviewer

Instead of clicking through every page:
1. Open `/catalog` in each app → verify widget rendering.
2. Run Layer 4 → per-app diversity score.
3. If a metric trends wrong, investigate that metric — not every page.

One URL + one script = no more screenshot review.
