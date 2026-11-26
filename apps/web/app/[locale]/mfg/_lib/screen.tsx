"use client";

import { useState } from "react";
import { Badge, Button, EmptyState, ErrorState, LoadingSkeleton, ToastRegion, type ToastItem } from "@/components/uikit";
import type { Dict } from "@/lib/i18n";

// Shared scaffolding for the MFG deferred screens (MF1-MF10): the four explicit
// states, the toast region, and the inline banner every screen uses for API errors.
// Pages stay to their domain forms/tables only.

export type PageShellProps = {
  title: string;
  subtitle: string;
  loading: boolean;
  error: boolean;
  errorCode: string | null;
  onRetry: () => void;
  forbidden: string;
  toasts: ToastItem[];
  onDismiss: (id: string) => void;
  banner: string | null;
  loadFailed: string;
  retryLabel: string;
  cancelLabel: string;
  children?: React.ReactNode;
};

export function useToasts() {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  function push(tone: ToastItem["tone"], message: string) {
    setToasts((prev) => [...prev, { id: crypto.randomUUID(), tone, message }]);
  }
  function dismiss(id: string) {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }
  return { toasts, push, dismiss };
}


export function PageShell(props: PageShellProps) {
  if (props.loading) return <LoadingSkeleton rows={6} />;
  if (props.error && (props.errorCode === "FORBIDDEN" || props.errorCode === "HTTP_403")) {
    return (
      <div className="stack">
        <div className="page-header">
          <h1 className="t-display">{props.title}</h1>
          <p className="t-caption">{props.subtitle}</p>
        </div>
        <div className="state state--error" role="alert"><p className="state__title">{props.forbidden}</p></div>
      </div>
    );
  }
  if (props.error) {
    return <ErrorState message={props.loadFailed} retryLabel={props.retryLabel} onRetry={props.onRetry} />;
  }
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{props.title}</h1>
        <p className="t-caption">{props.subtitle}</p>
      </div>
      <ToastRegion toasts={props.toasts} onDismiss={props.onDismiss} closeLabel={props.cancelLabel} />
      {props.banner ? <p className="banner" role="alert">{props.banner}</p> : null}
      {props.children}
    </div>
  );
}
