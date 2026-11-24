import type { BadgeTone } from "@/components/uikit";
import type { Dict, Locale } from "@/lib/i18n";
import type {
  Contract,
  ContractAmendment,
  ContractObligation,
  EventDetail,
  SourcingEvent,
} from "@/lib/services/procurement";

// Shared workspace context passed to every tab. Tone maps are page-local by design
// (ui-direction: status→tone maps live beside the screens, never in uikit).

export type WorkspaceCtx = {
  token: string;
  locale: Locale;
  t: Dict;
  event: SourcingEvent;
  meId: string | null;
  perms: string[];
  detail: EventDetail;
  applyDetail: (patch: Partial<EventDetail>) => void;
  onEvent: (event: SourcingEvent) => void;
};

export type ContractCtx = {
  token: string;
  locale: Locale;
  t: Dict;
  contract: Contract;
  amendments: ContractAmendment[];
  obligations: ContractObligation[];
  meId: string | null;
  perms: string[];
  setAmendments: (updater: (prev: ContractAmendment[]) => ContractAmendment[]) => void;
  setObligations: (updater: (prev: ContractObligation[]) => ContractObligation[]) => void;
  onContract: (contract: Contract) => void;
};

export const EVENT_STATUS_TONES: Record<string, BadgeTone> = {
  Draft: "muted",
  Published: "success",
  Closed: "muted",
  Awarded: "success",
  NoAward: "warning",
  Cancelled: "danger",
};

export const BID_STATUS_TONES: Record<string, BadgeTone> = {
  Submitted: "warning",
  Opened: "success",
  Disqualified: "danger",
};

export const AWARD_STATUS_TONES: Record<string, BadgeTone> = {
  Recommended: "warning",
  Approved: "success",
  Rejected: "danger",
  Alternate: "muted",
};

export const CONTRACT_STATUS_TONES: Record<string, BadgeTone> = {
  Draft: "muted",
  Active: "success",
  Expired: "warning",
  Terminated: "danger",
};

export const OBLIGATION_STATUS_TONES: Record<string, BadgeTone> = {
  Open: "warning",
  EvidenceSubmitted: "warning",
  Verified: "success",
  Waived: "muted",
};

export const SUPPLIER_STATUS_TONES: Record<string, BadgeTone> = {
  Active: "success",
  Pending: "warning",
  Suspended: "danger",
  Blocked: "danger",
  Inactive: "muted",
};

export const GATE_SEVERITY_TONES: Record<string, BadgeTone> = {
  Hard: "danger",
  Warning: "warning",
};

export const INVITATION_STATUS_TONES: Record<string, BadgeTone> = {
  Sent: "success",
  Failed: "danger",
  Declined: "warning",
};

export function statusLabel(t: Dict, v: string): string {
  return (t.procurement.statuses as Record<string, string>)[v] ?? v;
}

/** /me collapses tenant-admin to "*" — expand it wherever a permission gates UI. */
export function hasPerm(perms: string[], p: string): boolean {
  return perms.includes("*") || perms.includes(p);
}

/** Date-only values (DateOnly over the wire) render as ISO dates — data, never shifted. */
export function isoDate(v: string): string {
  return v.length >= 10 ? v.slice(0, 10) : v;
}
