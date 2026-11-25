"use client";

import { salesStatusLabel, type SalesMe } from "./helpers";
import type { BadgeTone } from "@/components/uikit";
import type { Dict } from "@/lib/i18n";

/** Status label with a screen-local override map first (statuses the shared
 * sales/common tables miss — Accepted, PartiallyReceived, Cleared, …), then
 * salesStatusLabel's sales.statuses → common.status fallback chain. */
export function localStatus(t: Dict, map: Record<string, string>, status: string): string {
  const local = map[status];
  if (local) return local;
  return salesStatusLabel(t, status);
}

/** Screen-local tone map first, then the shared salesTone chain. */
export function localTone(map: Record<string, BadgeTone>, status: string): BadgeTone {
  return map[status] ?? "muted";
}

export type { SalesMe };
