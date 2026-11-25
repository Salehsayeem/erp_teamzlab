---
description: Next.js App Router UI — RSC boundaries, tenant-safe reads, i18n/RTL, WCAG, confirmation UX
paths:
  - "apps/web/**"
---

# Frontend (ERP-NEXTJS-001..021 / ERP-PLATFORM-058..063 / ERP-I18N-*)

- Server Components by default; `"use client"` only on interactive leaves
  (ERP-NEXTJS-002/003); heavy widgets behind dynamic import (ERP-NEXTJS-021).
- Tenant/permission checks happen server-side only (ERP-NEXTJS-010..012); cache keys
  are tenant-scoped; never trust ids from the client.
- Every route has error.tsx + not-found.tsx fallbacks (ERP-NEXTJS-005); every screen
  renders loading / empty / error / success explicitly.
- Consequential actions (post / approve / reverse / dispose) confirm through a modal
  showing exact ids and totals before the call fires (ERP-NEXTJS-020).
- API calls go through the services/lib layer — no raw `fetch()` in components.
- All user-facing copy through the dict layer with en+bn key parity — no hardcoded
  strings; use logical CSS properties (`margin-inline-*`, `inset-inline`) so RTL mirrors.
- Visible focus, logical tab order, `aria-live` for async feedback, labels wired with
  `htmlFor` (ERP-PLATFORM-062/063; warehouse flows per ERP-INVENTORY-070).
