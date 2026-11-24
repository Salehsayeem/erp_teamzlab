"use client";

import { useEffect, useState } from "react";
import { fetchMe, type Me } from "@/lib/services/api";
import { localeTag, type Dict, type Locale } from "@/lib/i18n";
import type { BadgeTone } from "@/components/uikit";

export type MfgMe = {
  meId: string | null;
  permissions: string[] | null;
  /** True once /me resolved — gated buttons stay disabled until then. */
  meReady: boolean;
};

/** Maker-checker idiom: resolve /me once per page so the UI can gate
 * boms:approve / mfg:release / mfg:close on permission AND on "not the maker".
 * While unresolved, gated controls render disabled — never enabled by default. */
export function useMfgMe(token: string | null): MfgMe {
  const [me, setMe] = useState<Me | null>(null);
  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchMe(token).then((res) => {
      if (!cancelled && res.ok) setMe(res.data);
    });
    return () => { cancelled = true; };
  }, [token]);
  return { meId: me?.user.id ?? null, permissions: me?.permissions ?? null, meReady: me !== null };
}

/** Permission gate: null permissions (me unresolved) means "unknown" → callers
 * render the control disabled; false means the user lacks it → hide. */
export function hasPermission(permissions: string[] | null, perm: string): boolean | null {
  if (permissions === null) return null;
  return permissions.includes(perm) || permissions.includes("*");
}

// API error codes → mfg.errors.* messages. The API's English message rides
// along for specifics; unknown codes fall back to `${actionFailed}: message`.
const ERROR_KEYS: Record<string, keyof Dict["mfg"]["errors"]> = {
  BOM_NOT_FOUND: "bomNotFound",
  BOM_STATE_ERROR: "bomState",
  BOM_LINE_DUPLICATE: "bomLineDuplicate",
  BOM_APPROVAL_ERROR: "bomApproval",
  BOM_EFFECTIVE_OVERLAP: "bomOverlap",
  BOM_NOT_EFFECTIVE: "bomNotEffective",
  BOM_NO_LINES: "bomNoLines",
  MFG_ORDER_NOT_FOUND: "orderNotFound",
  MFG_TRANSITION_ERROR: "orderTransition",
  MFG_STATE_ERROR: "orderState",
  ORDER_CLOSED: "orderClosed",
  MFG_RELEASE_ERROR: "releaseError",
  MFG_RELEASE_BLOCKED: "releaseBlocked",
  MFG_CLOSE_ERROR: "closeError",
  MFG_CLOSE_BLOCKED: "closeBlocked",
  MFG_CLOSE_WIP_ERROR: "closeWip",
  AMENDMENT_CONFLICT: "amendmentConflict",
  MFG_OVER_TOLERANCE: "overTolerance",
  INSUFFICIENT_STOCK: "insufficientStock",
  ISSUE_NOT_FOUND: "issueNotFound",
  ISSUE_RETURN_OVER: "issueReturnOver",
  MFG_COMPONENT_ERROR: "componentError",
  MFG_CONCURRENCY_ERROR: "concurrency",
  FORBIDDEN: "forbidden",
  VALIDATION_ERROR: "validation",
};

export function mfgActionError(t: Dict, code: string, message: string): string {
  if (code === "NETWORK") return t.common.networkError;
  const key = ERROR_KEYS[code];
  const label = key ? t.mfg.errors[key] : null;
  return label ? `${label}: ${message}` : `${t.mfg.actionFailed}: ${message}`;
}

/** Module-local status labels first, then the shared common.status entries. */
export function mfgStatusLabel(t: Dict, status: string): string {
  const local = (t.mfg.statuses as Record<string, string>)[status];
  if (local) return local;
  const shared = (t.common.status as Record<string, string>)[status];
  return shared ?? status;
}

// Status → solid-pill tone. The label text always stays readable; the tone
// rides the pill fill (docs/design/ui-direction.md principle 5).
const MFG_TONES: Record<string, BadgeTone> = {
  Draft: "muted",
  Approved: "success",
  Retired: "danger",
  Planned: "warning",
  Released: "success",
  InProcess: "warning",
  Completed: "success",
  Closed: "muted",
  Cancelled: "danger",
  Manual: "muted",
  Backflush: "success",
  Disposal: "danger",
  ReturnToStock: "success",
};

export function mfgTone(status: string): BadgeTone {
  return MFG_TONES[status] ?? "muted";
}

/** Date-only rendering (effectiveFrom, closedAt…). */
export function fmtDay(iso: string, locale: Locale): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(localeTag(locale), { dateStyle: "medium" }).format(date);
}
