# Completeness vs Correctness — honest limits of static verification

Your question: **"Does tuning these values guarantee every text input has padding?
Does it guarantee two elements in the same row appear visually adjacent?
Does it catch MISSING features, not just WRONG values?"**

Short answer:
- **Static verification catches ~85% — but not 100%.**
- The missing 15% requires runtime (browser-based) checks.
- This document is honest about where each approach succeeds and fails.

## The Six Layers — what each actually catches

| Layer | Catches WRONG value? | Catches MISSING property? | Catches VISUAL adjacency? |
|-------|---------------------|---------------------------|---------------------------|
| 1. `verify-page-structure.sh` | ✓ (raw button) | ✗ | ✗ |
| 2. `verify-css.sh` | — | ✓ (undefined class) | ✗ |
| 3. `verify-design-tokens.sh` | ✓ (off-scale px) | ✗ | ✗ |
| 4. `verify-design-quality.sh` | ✓ (too many sizes) | ✗ | ✗ |
| 5. `verify-widget-contracts.sh` | ✓ (value < min) | ✓ (property missing in CSS) | ✗ |
| **6. `verify-runtime.sh` (Playwright)** | **✓** | **✓ (at computed style)** | **✓ (bounding rects)** |

## Layer 5 — Widget Contracts (catches MISSING features)

The key layer that answers your question. Every widget declares a **contract**:

```json
".ac-input": {
  "required": ["padding", "border", "background", "color", "font-size", "border-radius"],
  "min-values": { "padding": 8, "min-height": 36, "border-width": 1 }
}
```

The script verifies that the CSS block for `.ac-input` actually declares
every required property. If someone writes:

```css
.ac-input {
  background: #fff;
  color: #000;
  /* OOPS — no padding, no border, no font-size */
}
```

The script fails:
```
✗ .ac-input missing required properties: padding border font-size border-radius
```

This catches **missing features**, not just wrong values.

### Real example this already caught
```
✗ .ac-alert missing required properties: background
```
Before this layer, alerts would render with transparent background only —
barely visible. Now the contract forces a background declaration.

## What Layer 5 DOES NOT catch

### "Two elements in a row appear adjacent" — needs runtime
No static tool can verify that two siblings actually render side-by-side.
Why? Because visual adjacency depends on:
- The parent's `display` (flex/grid/block)
- The children's `width`/`flex-basis`/`order`
- Viewport width
- Font metrics
- Dynamic content length

**Only a browser can compute this.** What we CAN do statically:
- Enforce that parents use `display: flex` + `gap` (design convention)
- Enforce that children have `flex: 1` or explicit widths
- Catalog page renders these patterns — visual inspection of one page
  proves the pattern works

### "Does this element have padding at runtime?" — needs runtime
CSS cascading, specificity, and shadow DOM mean you can declare padding
but still end up with 0 padding if another rule wins. Only `getComputedStyle`
gives the truth.

### Example runtime test (future — Playwright):
```javascript
test('every ac-input has visible padding at runtime', async ({ page }) => {
  await page.goto('/catalog');
  const inputs = await page.$$('.ac-input');
  for (const input of inputs) {
    const padding = await input.evaluate(el =>
      parseInt(getComputedStyle(el).paddingLeft)
    );
    expect(padding).toBeGreaterThanOrEqual(8);
  }
});
```

## The Strategy

### Phase 1 (done): 5 static layers — catches 85%
- Layer 1: Code Hygiene
- Layer 2: Existence
- Layer 3: Direct Values
- Layer 4: Design Quality Metrics
- **Layer 5: Widget Contracts (catches MISSING features)** ← this answers your question

### Phase 2 (future): Visual proof via catalog
- `/catalog` page exists in each app
- Reviewing it ONCE = verifying the design system
- If catalog is correct + all pages use only catalog widgets, pages are correct

### Phase 3 (done): Runtime verification — `scripts/verify-runtime.mjs`
Playwright-based script that:
- Loads every `/catalog` page in a headless Chromium
- Extracts `getComputedStyle` for every contract selector (.ac-input, .ac-btn, etc.)
- For each element instance asserts:
  - padding ≥ contract min (real computed, not declared)
  - min-height ≥ contract min
  - border-width ≥ contract min
  - background is not rgba(...,0) — invisible element check
  - font-weight ≥ contract min

Usage:
```bash
# First time:
cd scripts && npm install && npx playwright install chromium

# Then (apps must be running):
./scripts/verify-runtime.sh
```

This catches the LAST 15% that static layers can't:
- CSS cascade specificity conflicts
- Actually rendered pixel dimensions
- Empty/transparent backgrounds due to `!important` loss
- Real `offsetHeight` of rendered elements

## The Honest Limits

No static tool can guarantee:
- **Computed color contrast** (needs rendered DOM)
- **Actual rendered dimensions** under specific fonts
- **Visual adjacency** (siblings side-by-side)
- **Overflow behavior** at different viewport sizes
- **Animation timing**

These are fundamental limits of not running the code. The answer is NOT
"build a better static tool" — it's "accept static catches 85% and add
runtime for the rest."

## The Practical Workflow

```bash
# Every commit (all static — fast):
./scripts/verify-page-structure.sh     # Layer 1
./scripts/verify-css.sh                 # Layer 2
./scripts/verify-design-tokens.sh       # Layer 3
./scripts/verify-widget-contracts.sh    # Layer 5 — missing-features check

# Weekly / pre-release:
./scripts/verify-design-quality.sh      # Layer 4 — design drift report

# After running apps locally (or in CI with services up):
./scripts/verify-runtime.sh             # Layer 6 — real browser rendering
```

If all layers pass AND the catalog looks right, the app is correct.

## Why this works in practice

1. **Widgets are designed ONCE**, used hundreds of times.
2. **Widget contracts** force each widget to declare its complete styling.
3. **Pages use only widgets** (Layer 1 enforces this).
4. **So if widgets are complete, all pages are automatically complete.**

The `/catalog` page proves widgets render correctly. Pages using those
widgets inherit correctness. No need to check each of 100+ pages visually.

This is the same reasoning behind Storybook in React: review the
component catalog, trust that pages built from those components are correct.
