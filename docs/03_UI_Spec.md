# 03 — UI Spec & Design Tokens (skeleton)

> Status: SKELETON. Target: **WCAG 2.2 AA** (spec assumption #11). First-class i18n + RTL (chapter 09). Dense-data-first: ERP screens are tables/forms before decoration.

## 1. Principles
- Tables and forms are the product. Density > chrome. Every destructive action confirms; every irreversible posting states consequences before submit.
- RTL-safe: only CSS logical properties (`margin-inline-start`, not `margin-left`). Icons never carry direction meaning alone.
- Dark/light both first-class (no hardcoded hex in components — tokens only).

## 2. Color tokens (light / dark)
| Token | Light | Dark | Use |
|---|---|---|---|
| `bg` | `#FFFFFF` | `#0F1115` | page |
| `surface` | `#F6F7F9` | `#171A21` | cards, table headers |
| `border` | `#D9DDE3` | `#2A2F3A` | outlines, dividers |
| `text` | `#1A1D23` | `#E8EAEF` | primary text |
| `text-muted` | `#5A6070` | `#9AA0AC` | secondary text |
| `accent` | `#1F5EFF` | `#5B8CFF` | links, primary buttons, focus |
| `success` | `#0E8345` | `#3DBB6E` | posted, approved |
| `warning` | `#B26A00` | `#E0A24A` | pending approval, holds |
| `danger` | `#C21F2E` | `#F07178` | errors, destructive, locked periods |
| `info` | `#0B6E8A` | `#58B7D4` | informational banners |

Contrast: all text pairs ≥ 4.5:1, large text/UI ≥ 3:1. Focus ring = 2px `accent` + 2px offset, never removed.

## 3. Typography (scale, RTL-aware)
| Token | Size/line | Use |
|---|---|---|
| `display` | 24/32 semibold | page titles |
| `title` | 18/26 semibold | section titles |
| `body` | 14/20 regular | default |
| `body-strong` | 14/20 semibold | table row emphasis |
| `mono` | 13/20 mono | IDs, amounts, codes |
| `caption` | 12/16 | meta, timestamps |

Numbers/amounts always `mono`, always locale-formatted (never `toFixed` by hand). Font: system stack + Noto fallback for CJK/Arabic/Devanagari coverage (**D-07** finalizes catalog).

## 4. Spacing & layout
- 4px grid: `4 8 12 16 24 32 48`. Card padding 16, section gap 24, form field gap 12.
- Table row height 40 (dense), 48 (comfortable toggle). Sticky header + first column.
- Breakpoints: mobile < 768 (tables collapse to cards), tablet 768–1279, desktop ≥ 1280.

## 5. Core component states
| Component | States |
|---|---|
| Button | default / hover / focus / active / disabled / loading (spinner replaces label) |
| Input | empty / filled / focus / error (message below, `aria-describedby`) / disabled / readonly |
| Select/Combobox | same as input + open / no-results |
| Table | loading (skeleton rows) / empty (module-specific empty state + CTA) / error (retry) / loaded / filtered-no-match |
| Badge | draft (muted) / pending (warning) / approved-posted (success) / rejected-locked (danger) |
| Drawer/Modal | open / closing; focus trapped; Esc closes; unsaved-changes guard |
| Toast | success / error / info; auto-dismiss 5s except errors (manual close) |

## 6. Empty/error copy pattern
Empty: what this list is + one CTA ("No suppliers yet — add your first supplier").
Error: what failed + retry/action; never raw codes (code lives in the API envelope, logged, not shown).
