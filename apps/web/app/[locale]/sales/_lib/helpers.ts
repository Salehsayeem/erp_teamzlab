"use client";

import { useEffect, useState } from "react";
import { fetchMe, type Me } from "@/lib/services/api";
import { localeTag, type Locale } from "@/lib/i18n";
import type { Dict } from "@/lib/i18n";
import type { BadgeTone } from "@/components/uikit";

export type SalesMe = {
  meId: string | null;
  permissions: string[] | null;
  /** True once /me resolved — checker buttons stay disabled until then. */
  meReady: boolean;
};

/** Maker-checker idiom (inventory/counts): resolve /me once per page so the UI
 * can gate checker buttons on permission AND on "not the maker". While
 * unresolved, gated buttons render disabled — never enabled by default. */
export function useSalesMe(token: string | null): SalesMe {
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

// API error codes → sales.errors.* messages. The API's English message rides
// along for specifics; unknown codes fall back to `${actionFailed}: message`.
const ERROR_KEYS: Record<string, keyof Dict["sales"]["errors"]> = {
  LEAD_TRANSITION_ERROR: "leadTransition",
  LEAD_CONVERSION_ERROR: "leadConversion",
  CUSTOMER_DUPLICATE: "duplicateCustomer",
  CRM_CASE_TRANSITION_ERROR: "caseTransition",
  OPPORTUNITY_WON_REQUIRES_ORDER: "wonRequiresOrder",
  OPPORTUNITY_STATE_ERROR: "opportunityState",
  OPPORTUNITY_TRANSITION_ERROR: "opportunityState",
  INVOICE_APPROVAL_ERROR: "invoiceApproval",
  INVOICE_ALREADY_POSTED: "invoiceAlreadyPosted",
  INVOICE_DISCARDED: "invoiceDiscarded",
  INVOICE_STATE_ERROR: "invoiceState",
  SO_NOT_BILLABLE: "soNotBillable",
  ALREADY_BILLED: "alreadyBilled",
  ADJUSTMENT_OVER_OPEN: "adjustmentOverOpen",
  ADJUSTMENT_APPROVAL_ERROR: "adjustmentApproval",
  ADJUSTMENT_ALREADY_APPROVED: "adjustmentAlreadyApproved",
  ADJUSTMENT_STATE_ERROR: "adjustmentState",
  REMINDER_SUPPRESSED: "reminderSuppressed",
  SO_APPROVAL_ERROR: "soApproval",
  SO_STATE_ERROR: "soState",
  SO_NOT_SUBMITTABLE: "soState",
  SHIPMENT_ERROR: "shipment",
  INSUFFICIENT_STOCK: "insufficientStock",
  DISPUTE_STATE_ERROR: "disputeState",
  FORBIDDEN: "forbidden",
  VALIDATION_ERROR: "validation",
};

export function salesActionError(t: Dict, code: string, message: string): string {
  if (code === "NETWORK") return t.common.networkError;
  const key = ERROR_KEYS[code];
  const label = key ? t.sales.errors[key] : null;
  return label ? `${label}: ${message}` : `${t.sales.actionFailed}: ${message}`;
}

/** Module-local status labels first, then the shared common.status entries. */
export function salesStatusLabel(t: Dict, status: string): string {
  const local = (t.sales.statuses as Record<string, string>)[status];
  if (local) return local;
  const shared = (t.common.status as Record<string, string>)[status];
  return shared ?? status;
}

// Status → pill tone. The label text always stays readable; the tone rides
// the pill fill (docs/design/ui-direction.md principle 5).
const SALES_TONES: Record<string, BadgeTone> = {
  New: "muted",
  Working: "warning",
  Qualified: "success",
  Disqualified: "danger",
  Converted: "success",
  Recycled: "warning",
  InProgress: "warning",
  Won: "success",
  Lost: "danger",
  CreditHold: "warning",
  Shipped: "success",
  Invoiced: "success",
  Confirmed: "success",
  Discarded: "danger",
  Promised: "warning",
  Resolved: "success",
  Low: "muted",
  Medium: "warning",
  High: "danger",
  Credit: "warning",
  Debit: "muted",
};

export function salesTone(status: string): BadgeTone {
  return SALES_TONES[status] ?? "muted";
}

/** Date-only rendering (dueDate, expectedCloseDate, promise dates) — no time noise. */
export function fmtDay(iso: string, locale: Locale): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(localeTag(locale), { dateStyle: "medium" }).format(date);
}
