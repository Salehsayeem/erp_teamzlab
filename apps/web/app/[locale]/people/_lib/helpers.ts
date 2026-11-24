"use client";

import { useEffect, useState } from "react";
import { fetchMe, type Me } from "@/lib/services/api";
import { localeTag, type Dict, type Locale } from "@/lib/i18n";
import type { BadgeTone } from "@/components/uikit";

export type PeopleMe = {
  meId: string | null;
  permissions: string[] | null;
  /** True once /me resolved — gated buttons stay disabled until then. */
  meReady: boolean;
};

/** Maker-checker idiom (sales/invoices): resolve /me once per page so the UI
 * can gate checker actions on permission AND on "not the maker". While
 * unresolved, gated controls render disabled — never enabled by default. */
export function usePeopleMe(token: string | null): PeopleMe {
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

// API error codes → people.errors.* messages. The API's English message rides
// along for specifics; unknown codes fall back to `${actionFailed}: message`.
const ERROR_KEYS: Record<string, keyof Dict["people"]["errors"]> = {
  PERSON_DUPLICATE: "personDuplicate",
  WORKER_NOT_FOUND: "workerNotFound",
  WORKER_TRANSITION_ERROR: "workerTransition",
  WORKER_NO_COMPENSATION: "workerNoCompensation",
  WORKER_TERMINATED: "workerTerminated",
  WORKER_NOT_ACTIVE: "workerNotActive",
  COMPENSATION_OVERLAP: "compensationOverlap",
  PAY_GROUP_NOT_FOUND: "payGroupNotFound",
  PAY_GROUP_INACTIVE: "payGroupInactive",
  PAY_GROUP_OVERLAP: "payGroupOverlap",
  PAY_GROUP_EXISTS: "payGroupExists",
  TIME_ENTRY_NOT_FOUND: "timeEntryNotFound",
  TIME_ENTRY_STATE_ERROR: "timeEntryState",
  TIME_ENTRY_ALREADY_DECIDED: "timeEntryDecided",
  TIME_CORRECTION_ALREADY_DECIDED: "timeCorrectionDecided",
  TIME_APPROVAL_ERROR: "timeApproval",
  TIME_PERIOD_POSTED: "timePeriodPosted",
  TIME_CORRECTION_NOT_FOUND: "timeCorrectionNotFound",
  TIME_CORRECTION_STATE_ERROR: "timeCorrectionState",
  PAY_CODE_EXISTS: "payCodeExists",
  PAY_CODE_NOT_FOUND: "payCodeNotFound",
  PAY_CODE_INACTIVE: "payCodeInactive",
  PAY_CODE_ALREADY_INACTIVE: "payCodeAlreadyInactive",
  ACCOUNT_NOT_FOUND: "accountNotFound",
  ACCOUNT_NOT_POSTABLE: "accountNotPostable",
  PERIOD_NOT_FOUND: "periodNotFound",
  PERIOD_LOCKED: "periodLocked",
  PAYRUN_NOT_FOUND: "payrunNotFound",
  PAYRUN_NOT_CALCULATED: "payrunNotCalculated",
  PAYRUN_NOT_APPROVED: "payrunNotApproved",
  PAYRUN_STATE_ERROR: "payrunState",
  PAYRUN_ALREADY_APPROVED: "payrunAlreadyApproved",
  PAYRUN_ALREADY_POSTED: "payrunAlreadyPosted",
  PAYRUN_ALREADY_REVERSED: "payrunAlreadyReversed",
  PAYROLL_APPROVAL_ERROR: "payrollApproval",
  PAYROLL_VALIDATION_BLOCKED: "payrollValidationBlocked",
  PAYROLL_RECONCILIATION_BROKEN: "payrollReconciliation",
  PAYSLIP_NOT_FOUND: "payslipNotFound",
  FORBIDDEN: "forbidden",
  VALIDATION_ERROR: "validation",
};

export function peopleActionError(t: Dict, code: string, message: string): string {
  if (code === "NETWORK") return t.common.networkError;
  const key = ERROR_KEYS[code];
  const label = key ? t.people.errors[key] : null;
  return label ? `${label}: ${message}` : `${t.people.actionFailed}: ${message}`;
}

/** Module-local status labels first, then the shared common.status entries. */
export function peopleStatusLabel(t: Dict, status: string): string {
  const local = (t.people.statuses as Record<string, string>)[status];
  if (local) return local;
  const shared = (t.common.status as Record<string, string>)[status];
  return shared ?? status;
}

// Status → solid-pill tone. The label text always stays readable; the tone
// rides the pill fill (docs/design/ui-direction.md principle 5).
const PEOPLE_TONES: Record<string, BadgeTone> = {
  Draft: "muted",
  Prehire: "warning",
  Active: "success",
  OnLeave: "warning",
  Suspended: "danger",
  Terminated: "danger",
  Pending: "warning",
  Approved: "success",
  Rejected: "danger",
  Open: "muted",
  Calculated: "warning",
  Posted: "success",
  Reversed: "danger",
  Earning: "success",
  Deduction: "danger",
  Self: "muted",
  Manager: "success",
};

export function peopleTone(status: string): BadgeTone {
  return PEOPLE_TONES[status] ?? "muted";
}

/** Date-only rendering (hireDate, workDate, payDate, effectiveFrom…). */
export function fmtDay(iso: string, locale: Locale): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(localeTag(locale), { dateStyle: "medium" }).format(date);
}
