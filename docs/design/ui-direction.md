# UI/UX Design Direction — TeamZlab ERP

> Status: **ADOPTED 2026-11-24** — owner-selected reference: TailwindAdmin React (themewagon).
> This file is the synced instruction file for ALL web UI work. When code and this file
> disagree, fix the code or update this file in the same commit — never let them drift.
> Implementation: `apps/web/app/globals.css` (tokens + component classes), `components/uikit.tsx`
> (React primitives), `app/[locale]/layout.tsx` (app shell), Tailwind CSS v4 for utilities.

---

## 1. Design principles

1. **Dark navy is the default theme.** Light is a first-class toggle (`ThemeToggle` →
   `html[data-theme="light"]`, persisted to `localStorage.theme`, applied pre-paint by the
   layout's inline script). Components never branch on theme — only tokens flip.
2. **Tables and forms are the product.** Screens are a list or a form first. Chrome is a
   sidebar (≥1024px) + topbar; below 1024px the sidebar hides and the topbar carries navigation.
3. **Every screen renders four states: loading / empty / error / success.** Fixed placement,
   never improvised spinners. Empty screens invite action; errors say what happened and how to
   fix it, in the interface's voice.
4. **WCAG 2.2 AA is the floor.** Text ≥ 4.5:1, boundaries ≥ 3:1, focus ring never removed,
   targets ≥ 24px, reflow at 320px, no keyboard traps.
5. **Status = pill + label, never color alone.** Badges are solid pills with white text on the
   status tone (adopted theme). Deltas carry ▲/▼ plus words; a disabled Approve shows *why* in
   visible text, never a tooltip-only reason.
6. **RTL-safe by construction.** Only logical properties (`margin-inline`, `inset-inline-start`,
   `border-block-end`). Layout mirrors under `dir="rtl"`. (bn currently renders `dir="ltr"` —
   flipping is a one-line layout decision awaiting the owner.)
7. **Tokens only.** Semantic tokens (`--bg --surface --surface-2 --border --text --text-muted
   --accent --success --warning --danger --info`) flip per theme; component classes reference
   them (or Tailwind utilities bridged via `@theme inline`). No raw hex below the token layer.
8. **Irreversible actions state consequences before submit.** Every destructive action confirms;
   posting failure preserves the draft and shows an inline error.
9. **Maker–checker is enforced in the UI**, not just the API: the submitter never sees an
   enabled Approve button on their own document.
10. **Numbers are data.** IDs, amounts, quantities in `.mono`, locale-formatted via `Intl`,
    end-aligned, tabular figures. Stat tiles are text (label + value + delta) — no decorative
    charts in tiles.

## 2. Palette (dark default / light toggle)

| Token | Dark (default) | Light |
|---|---|---|
| `--bg` (page) | `#0B1524` | `#EEF2F8` |
| `--surface` (cards, topbar, sidebar) | `#152238` | `#FFFFFF` |
| `--surface-2` (nested, hover, zebra) | `#1C2F4A` | `#F5F8FC` |
| `--border` | `#233752` | `#DCE4EF` |
| `--text` | `#E7EEF8` | `#101B2D` |
| `--text-muted` | `#8FA3C0` | `#5D6F8C` |
| `--accent` / `--accent-bright` | `#3E7BFA` / `#5B9CFF` | `#2563EB` / `#3B82F6` |
| `--success / --warning / --danger / --info` | `#2FBF71 #F5A524 #F04452 #22B8D4` | `#16A34A #D97706 #DC2626 #0891B2` |

Type: `Inter, system-ui, "Segoe UI", "Noto Sans", "Noto Sans Bengali", sans-serif` — no font
downloads. Page titles 24px/700, card titles 16px/600, body 14px/20, captions 12px muted.
Radii: cards 14px, controls 10px, pills 999px. Elevation = lighter surface, not shadows.

## 3. App shell

```
┌────────────┬──────────────────────────────────────────┐
│ sidebar    │ topbar: app title · locale · theme ◐     │
│ 248px      ├──────────────────────────────────────────┤
│ brand      │ content (max 1200px)                     │
│ OVERVIEW   │   page header (title + caption + CTAs)   │
│ Procurement│   KPI tile row (1/2/4 cols by breakpoint)│
│ Inventory  │   cards / tables / forms                 │
│ Finance    │                                          │
│ System     │                                          │
└────────────┴──────────────────────────────────────────┘
```

- Sidebar ≥1024px only; below it hides (topbar carries a compact nav). Groups: Overview,
  Procurement, Inventory, Finance, System. Active link = accent tint + inset accent bar,
  `aria-current="page"`.
- Topbar is sticky; actions: locale switch, theme toggle.
- Content column: `max-inline-size: 1200px`, 24/20px padding.

## 4. Component classes (globals.css)

| Class | Spec |
|---|---|
| `.card` + `.card__title/.card__subtitle` | surface fill, 1px border, 14px radius, 20px padding |
| `.kpi` + `__label/__value/__delta` | tile: label 12px muted, value 26px/700, delta colored + ▲/▼ |
| `.btn` (primary) / `--secondary` / `--danger` / `--sm` | accent fill, 10px radius, 600 weight; secondary = transparent + border |
| `.badge` (solid pill) | white text on tone: `--success/--warning/--danger/--info/--muted` modifiers; label text always present |
| `.table-scroll` > `.table` | bordered surface card; th 12px muted on `--surface-2`; rows hover `--surface-2`; mobile collapse via `data-label` |
| `.field` + `.input/.textarea` | label 600, hint/error 12px, focus = accent outline + border, 10px radius |
| `.modal` + `.modal-scrim` | scrim #050a12/60, dialog = surface card, focus trap in `uikit.tsx` |
| `.toast` (region bottom inline-end) | surface card, 4px accent inline-start edge, `role=alert` for errors |
| `.tabs__list [role=tab]` | accent underline on selected, roving tabindex |
| `.pagination__current` | accent-bordered chip |
| `.auth` / `.auth__card` / `.auth__row` | centered 460px login card; role rows = ledger list (role 600 + mono email + aligned action) |
| `.state` / `.state--error` | centered empty/error states; error = danger-tinted card + retry |
| `.skeleton__row` | shimmer at `--surface-2/--border`; disabled under reduced motion |

## 5. Screen checklist (every screen)

1. Page header: title + one-line caption (+ primary action, right).
2. Four states designed: loading (`.skeleton`), empty (`.state` + CTA), error (`.state--error`
   + retry), success (the content).
3. Tables: `.table-scroll > .table`, `data-label` on every td, row actions in `.row-actions`.
4. Forms: `.form-grid` + `.field`; validation errors inline via `.field__error`.
5. i18n: no hardcoded copy — every string through `lib/i18n/{en,bn}.ts` with key parity.
6. Create/view through `services/` — no raw fetch in components.
