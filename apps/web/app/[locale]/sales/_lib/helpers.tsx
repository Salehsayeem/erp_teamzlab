"use client";

import { useEffect, useState } from "react";
import { fetchMe, type Me } from "@/lib/services/api";
import { localeTag, type Locale } from "@/lib/i18n";
import type { Dict } from "@/lib/i18n";
import { Button, type BadgeTone } from "@/components/uikit";

export type SalesMe = {
  meId: string | null;
  permissions: string[] | null;
  /** True once /me resolved — checker buttons stay disabled until then. */
  meReady: boolean;
  /** True when /me definitively failed (401/network) — pages render a visible
   * failure state with retry instead of silently dead gated buttons. */
  meFailed: boolean;
  /** Re-runs the /me fetch. */
  retryMe: () => void;
};

/** Maker-checker idiom (inventory/counts): resolve /me once per page so the UI
 * can gate checker buttons on permission AND on "not the maker". While
 * unresolved, gated buttons render disabled — never enabled by default.
 * A definitive failure surfaces via `meFailed` (review 1.3/fix round 1):
 * pages render SalesMeError with retry instead of leaving silent dead buttons. */
export function useSalesMe(token: string | null): SalesMe {
  const [me, setMe] = useState<Me | null>(null);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    setFailed(false);
    void fetchMe(token).then((res) => {
      if (cancelled) return;
      if (res.ok) setMe(res.data);
      else setFailed(true);
    });
    return () => { cancelled = true; };
  }, [token, attempt]);
  return {
    meId: me?.user.id ?? null,
    permissions: me?.permissions ?? null,
    meReady: me !== null,
    meFailed: failed,
    retryMe: () => setAttempt((a) => a + 1),
  };
}

/** Visible /me failure state (fix round 1, review 1.3): rendered by every sales
 * screen when permission resolution definitively failed — retry re-runs /me. */
export function SalesMeError({ t, onRetry }: { t: Dict; onRetry: () => void }) {
  return (
    <div className="state state--error" role="alert">
      <p className="state__title">{t.sales.meError}</p>
      <Button variant="secondary" onClick={onRetry}>
        {t.common.retry}
      </Button>
    </div>
  );
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
  // deferred-screens wave (quotes/price-books/360/lifecycle/forecast/returns/rebates/delivery)
  PRICE_OVERLAP: "priceOverlap",
  PRICE_NOT_FOUND: "priceNotFound",
  PRICE_BOOK_NOT_FOUND: "priceBookNotFound",
  QUOTE_EXPIRED: "quoteExpired",
  QUOTE_STATE_ERROR: "quoteState",
  QUOTE_APPROVAL_ERROR: "quoteApproval",
  QUOTE_NOT_FOUND: "quoteNotFound",
  QUOTE_REVISION_NOT_FOUND: "quoteRevisionNotFound",
  DISCOUNT_CAP_EXCEEDED: "discountCap",
  CUSTOMER_HIERARCHY_CYCLE: "hierarchyCycle",
  ONBOARDING_CONTROLS_UNMET: "onboardingUnmet",
  MERGE_BLOCKED: "mergeBlocked",
  MERGE_STATE_ERROR: "mergeState",
  SO_HELD: "soHeld",
  HOLD_ALREADY_CLEARED: "holdAlreadyCleared",
  SO_AMENDMENT_ERROR: "soAmendment",
  SO_CANCEL_ERROR: "soCancel",
  SCHEDULE_PAUSED: "schedulePaused",
  SCHEDULE_STATE_ERROR: "scheduleState",
  RETURN_QTY_EXCEEDED: "returnQtyExceeded",
  RETURN_STATE_ERROR: "returnState",
  RETURN_RECEIPT_EXCEEDED: "returnReceiptExceeded",
  REFUND_STATE_ERROR: "refundState",
  REBATE_EXCEEDS_ACCRUAL: "rebateExceedsAccrual",
  REBATE_STATE_ERROR: "rebateState",
  DELIVERY_STATE_ERROR: "deliveryState",
  DELIVERY_NOT_FOUND: "deliveryNotFound",
  EINVOICE_NOT_ENABLED: "einvoiceNotEnabled",
  EINVOICE_STATE_ERROR: "einvoiceState",
  FORECAST_SNAPSHOT_INCOMPLETE: "forecastSnapshotIncomplete",
  AMENDMENT_NOT_FOUND: "amendmentNotFound",
  HOLD_NOT_FOUND: "holdNotFound",
  MERGE_NOT_FOUND: "mergeNotFound",
  ONBOARDING_CHECK_NOT_FOUND: "onboardingCheckNotFound",
  REBATE_NOT_FOUND: "rebateNotFound",
  RECEIPT_CANDIDATE_NOT_FOUND: "receiptCandidateNotFound",
  REFUND_NOT_FOUND: "refundNotFound",
  RETURN_NOT_FOUND: "returnNotFound",
  SCHEDULE_NOT_FOUND: "scheduleNotFound",
  CUSTOMER_NOT_FOUND: "duplicateCustomer",
  CUSTOMER_MERGE_FORBIDDEN: "forbidden",
};

export function salesActionError(t: Dict, code: string, message: string): string {
  if (code === "NETWORK") return t.common.networkError;
  const key = ERROR_KEYS[code];
  const label = key ? t.sales.errors[key] : null;
  // An empty API message must not leave a dangling ": " (fix round 1, minor b).
  if (label) return message ? `${label}: ${message}` : label;
  return message ? `${t.sales.actionFailed}: ${message}` : t.sales.actionFailed;
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
