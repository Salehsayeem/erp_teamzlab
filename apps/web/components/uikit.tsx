"use client";

import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from "react";

// Plain typed components; all styling lives in app/globals.css tokens/classes.

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "danger";
  small?: boolean;
  loading?: boolean;
};

export function Button({ variant = "primary", small, loading, children, className, ...rest }: ButtonProps) {
  const cls = ["btn", `btn--${variant}`, small ? "btn--sm" : "", className ?? ""].filter(Boolean).join(" ");
  return (
    <button className={cls} aria-busy={loading || undefined} disabled={rest.disabled || loading} {...rest}>
      {loading ? (
        <>
          <span className="btn__spinner" aria-hidden="true" />
          <span className="sr-only">{children}</span>
        </>
      ) : (
        children
      )}
    </button>
  );
}

type InputProps = InputHTMLAttributes<HTMLInputElement> & {
  label: string;
  error?: string;
};

export function Input({ label, error, id, className, ...rest }: InputProps) {
  const errorId = error ? `${id}-error` : undefined;
  return (
    <div className={error ? "field field--error" : "field"}>
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        className={className ?? "input"}
        aria-invalid={error ? true : undefined}
        aria-describedby={errorId}
        {...rest}
      />
      {error ? (
        <p className="field__error" id={errorId}>
          {error}
        </p>
      ) : null}
    </div>
  );
}

export type BadgeTone = "muted" | "warning" | "success" | "danger";

/** Badge is always color + text label (status color is reserved, never color alone). */
export function Badge({ tone, children }: { tone: BadgeTone; children: ReactNode }) {
  return <span className={`badge badge--${tone}`}>{children}</span>;
}

const STATUS_TONES: Record<string, BadgeTone> = {
  Draft: "muted",
  PendingApproval: "warning",
  Pending: "warning",
  Approved: "success",
  Sent: "success",
  Received: "success",
  Posted: "success",
  Rejected: "danger",
};

export function statusTone(status: string): BadgeTone {
  return STATUS_TONES[status] ?? "muted";
}

export type Column<T> = {
  key: string;
  label: string;
  mono?: boolean;
  render: (row: T) => ReactNode;
};

export function Table<T>({ columns, rows, caption }: { columns: Column<T>[]; rows: T[]; caption?: string }) {
  return (
    <div className="table-scroll">
      <table className="table">
        {caption ? <caption className="sr-only">{caption}</caption> : null}
        <thead>
          <tr>
            {columns.map((col) => (
              <th key={col.key} scope="col">
                {col.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>
              {columns.map((col) => (
                <td key={col.key} data-label={col.label} className={col.mono ? "mono" : undefined}>
                  {col.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function EmptyState({ title, action }: { title: string; action?: { label: string; onClick: () => void } }) {
  return (
    <div className="state">
      <p className="state__title">{title}</p>
      {action ? (
        <Button onClick={action.onClick}>{action.label}</Button>
      ) : null}
    </div>
  );
}

export function ErrorState({ message, retryLabel, onRetry }: { message: string; retryLabel: string; onRetry: () => void }) {
  return (
    <div className="state state--error" role="alert">
      <p className="state__title">{message}</p>
      <Button variant="secondary" onClick={onRetry}>
        {retryLabel}
      </Button>
    </div>
  );
}

export function LoadingSkeleton({ rows = 4 }: { rows?: number }) {
  return (
    <div className="skeleton" role="status" aria-label="loading">
      {Array.from({ length: rows }, (_, i) => (
        <span key={i} className="skeleton__row" />
      ))}
    </div>
  );
}
