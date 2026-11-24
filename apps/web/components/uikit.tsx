"use client";

import {
  cloneElement,
  forwardRef,
  useEffect,
  useId,
  useRef,
  type ButtonHTMLAttributes,
  type HTMLAttributes,
  type InputHTMLAttributes,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent,
  type ReactElement,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from "react";

// Plain typed components; all styling lives in app/globals.css tokens/classes.
// The library never hardcodes user-facing copy — every label arrives via props
// from the page dictionaries (apps/web/lib/i18n). New CSS uses logical
// properties only, so every component mirrors under dir="rtl".

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

// ── Form primitives ─────────────────────────────────────────────────────────
// Field owns the label/hint/error wiring: htmlFor + aria-describedby +
// aria-invalid are injected onto the single control child, so Input, Textarea
// and Select stay dumb wrappers around native elements.

type FieldControlProps = {
  id?: string;
  "aria-describedby"?: string;
  "aria-invalid"?: boolean | "true" | "false";
};

type FieldProps = {
  id: string;
  label: string;
  hint?: string;
  error?: string;
  children: ReactElement<FieldControlProps>;
};

export function Field({ id, label, hint, error, children }: FieldProps) {
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(" ") || undefined;
  const control = cloneElement<FieldControlProps>(children, {
    id,
    "aria-describedby": describedBy,
    "aria-invalid": error ? true : undefined,
  });
  return (
    <div className={error ? "field field--error" : "field"}>
      <label htmlFor={id}>{label}</label>
      {control}
      {hint ? (
        <p className="field__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      {error ? (
        <p className="field__error" id={errorId}>
          {error}
        </p>
      ) : null}
    </div>
  );
}

type InputProps = Omit<InputHTMLAttributes<HTMLInputElement>, "id"> & {
  id: string;
  label: string;
  hint?: string;
  error?: string;
};

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { label, hint, error, id, className, ...rest },
  ref,
) {
  return (
    <Field id={id} label={label} hint={hint} error={error}>
      <input ref={ref} className={className ?? "input"} {...rest} />
    </Field>
  );
});

type TextareaProps = Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, "id"> & {
  id: string;
  label: string;
  hint?: string;
  error?: string;
};

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { label, hint, error, id, className, rows = 3, ...rest },
  ref,
) {
  return (
    <Field id={id} label={label} hint={hint} error={error}>
      <textarea ref={ref} rows={rows} className={className ?? "input textarea"} {...rest} />
    </Field>
  );
});

export type SelectOption = { value: string; label: string };

type SelectProps = Omit<SelectHTMLAttributes<HTMLSelectElement>, "id"> & {
  id: string;
  label: string;
  hint?: string;
  error?: string;
  options: SelectOption[];
  placeholder?: string;
};

export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select(
  { label, hint, error, id, options, placeholder, className, ...rest },
  ref,
) {
  return (
    <Field id={id} label={label} hint={hint} error={error}>
      <select ref={ref} className={className ?? "input"} {...rest}>
        {placeholder ? <option value="">{placeholder}</option> : null}
        {options.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>
    </Field>
  );
});

// ── Card ────────────────────────────────────────────────────────────────────

type CardProps = HTMLAttributes<HTMLElement> & { title?: ReactNode };

export function Card({ title, children, className, ...rest }: CardProps) {
  return (
    <section className={className ? `card ${className}` : "card"} {...rest}>
      {title ? <h2 className="t-title">{title}</h2> : null}
      {children}
    </section>
  );
}

// ── Badge ───────────────────────────────────────────────────────────────────

export type BadgeTone = "muted" | "warning" | "success" | "danger";

/** Badge is always a text label plus colored dot/border. The status color is
 * never the text color — warning #B26A00 is 4.24:1 on white (backlog finding
 * 12) — the label uses --text and the tone rides the dot + border (≥3:1). */
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

// ── Table ───────────────────────────────────────────────────────────────────

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

// ── List states ─────────────────────────────────────────────────────────────

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: { label: string; onClick: () => void };
}) {
  return (
    <div className="state">
      <p className="state__title">{title}</p>
      {description ? <p className="state__description">{description}</p> : null}
      {action ? <Button onClick={action.onClick}>{action.label}</Button> : null}
    </div>
  );
}

export function ErrorState({
  message,
  retryLabel,
  onRetry,
}: {
  message: string;
  retryLabel: string;
  onRetry: () => void;
}) {
  return (
    <div className="state state--error" role="alert">
      <p className="state__title">{message}</p>
      <Button variant="secondary" onClick={onRetry}>
        {retryLabel}
      </Button>
    </div>
  );
}

export function LoadingSkeleton({ rows = 4, label }: { rows?: number; label?: string }) {
  return (
    <div className="skeleton" role="status" aria-label={label}>
      {Array.from({ length: rows }, (_, i) => (
        <span key={i} className="skeleton__row" aria-hidden="true" />
      ))}
    </div>
  );
}

// ── Modal ───────────────────────────────────────────────────────────────────
// role="dialog" + aria-modal, focus trapped (Tab cycles inside), Esc and scrim
// click close, focus starts on the title and returns to the trigger on close.
// `busy` (submission in flight) suppresses every close path. All positioning
// is logical (inset-inline-start, translate) so dir="rtl" mirrors for free.

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

type ModalProps = {
  open: boolean;
  onClose: () => void;
  title: string;
  closeLabel: string;
  busy?: boolean;
  children: ReactNode;
};

export function Modal({ open, onClose, title, closeLabel, busy = false, children }: ModalProps) {
  const titleId = useId();
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const titleRef = useRef<HTMLHeadingElement | null>(null);
  const restoreRef = useRef<HTMLElement | null>(null);

  // Focus the title on open; hand focus back to the invoking element on close.
  useEffect(() => {
    if (!open) return;
    restoreRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    titleRef.current?.focus();
    return () => {
      restoreRef.current?.focus();
      restoreRef.current = null;
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        if (!busy) {
          e.preventDefault();
          onClose();
        }
        return;
      }
      if (e.key !== "Tab") return;
      const dialog = dialogRef.current;
      if (!dialog) return;
      const focusables = dialog.querySelectorAll<HTMLElement>(FOCUSABLE);
      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      if (!first || !last) {
        e.preventDefault();
        return;
      }
      const active = document.activeElement;
      const inside = active instanceof Node && dialog.contains(active);
      if (!inside) {
        e.preventDefault();
        (e.shiftKey ? last : first).focus();
      } else if (e.shiftKey && active === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && active === last) {
        e.preventDefault();
        first.focus();
      }
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [open, busy, onClose]);

  if (!open) return null;

  function handleScrimClick(e: ReactMouseEvent<HTMLDivElement>) {
    if (!busy && e.target === e.currentTarget) onClose();
  }

  return (
    <>
      <div className="modal-scrim" aria-hidden="true" onClick={handleScrimClick} />
      <div ref={dialogRef} className="modal" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <div className="modal__head">
          <h2 ref={titleRef} id={titleId} className="t-title" tabIndex={-1}>
            {title}
          </h2>
          <button
            type="button"
            className="modal__close"
            aria-label={closeLabel}
            onClick={() => {
              if (!busy) onClose();
            }}
          >
            <span aria-hidden="true">✕</span>
          </button>
        </div>
        {children}
      </div>
    </>
  );
}

// ── Toasts ──────────────────────────────────────────────────────────────────
// One aria-live region (polite default, assertive option). Success/info
// auto-dismiss after 5 s — paused while hovered or focused (WCAG 2.2.1);
// errors are manual-close only (ui-direction §5.4). Stacked bottom inline-end.

export type ToastTone = "success" | "info" | "error";
export type ToastItem = { id: string; tone: ToastTone; message: string };

const TOAST_ROLE: Record<ToastTone, "status" | "alert"> = {
  success: "status",
  info: "status",
  error: "alert",
};

type ToastRegionProps = {
  toasts: ToastItem[];
  onDismiss: (id: string) => void;
  closeLabel: string;
  live?: "polite" | "assertive";
  autoDismissMs?: number;
};

export function ToastRegion({ toasts, onDismiss, closeLabel, live = "polite", autoDismissMs = 5000 }: ToastRegionProps) {
  return (
    <div className="toast-region" aria-live={live} aria-atomic="false">
      {toasts.map((toast) => (
        <ToastCard key={toast.id} toast={toast} closeLabel={closeLabel} autoDismissMs={autoDismissMs} onDismiss={onDismiss} />
      ))}
    </div>
  );
}

function ToastCard({
  toast,
  closeLabel,
  autoDismissMs,
  onDismiss,
}: {
  toast: ToastItem;
  closeLabel: string;
  autoDismissMs: number;
  onDismiss: (id: string) => void;
}) {
  const timerRef = useRef<number | null>(null);
  const remainingRef = useRef(autoDismissMs);
  const startedRef = useRef(0);
  const manual = toast.tone === "error";

  function pause() {
    if (timerRef.current === null) return;
    window.clearTimeout(timerRef.current);
    timerRef.current = null;
    remainingRef.current -= Date.now() - startedRef.current;
  }

  function resume() {
    if (manual || timerRef.current !== null) return;
    startedRef.current = Date.now();
    timerRef.current = window.setTimeout(() => onDismiss(toast.id), remainingRef.current);
  }

  useEffect(() => {
    if (manual) return;
    resume();
    return () => {
      if (timerRef.current !== null) window.clearTimeout(timerRef.current);
      timerRef.current = null;
    };
  }, [manual, toast.id, onDismiss, autoDismissMs]);

  return (
    <div
      className={`toast toast--${toast.tone}`}
      role={TOAST_ROLE[toast.tone]}
      onPointerEnter={pause}
      onPointerLeave={resume}
      onFocus={pause}
      onBlur={resume}
    >
      <p className="toast__message">{toast.message}</p>
      <button type="button" className="toast__close" aria-label={closeLabel} onClick={() => onDismiss(toast.id)}>
        <span aria-hidden="true">✕</span>
      </button>
    </div>
  );
}

// ── Tabs ────────────────────────────────────────────────────────────────────
// Automatic-activation tabs: roving tabindex, arrow keys + Home/End move,
// aria-selected/aria-controls/aria-labelledby wired. Arrow direction is read
// from the tablist's computed direction, so Left/Right flip under dir="rtl".

export type TabItem = { id: string; label: string; content: ReactNode };

type TabsProps = {
  tabs: TabItem[];
  activeId: string;
  onChange: (id: string) => void;
  /** Accessible name for the tablist, e.g. the section title from the dictionary. */
  label: string;
};

export function Tabs({ tabs, activeId, onChange, label }: TabsProps) {
  const listRef = useRef<HTMLDivElement | null>(null);

  function move(nextIndex: number) {
    const target = tabs[nextIndex];
    if (target) onChange(target.id);
  }

  function onKeyDown(e: ReactKeyboardEvent<HTMLDivElement>) {
    const forwardKey = e.key === "ArrowRight";
    const backwardKey = e.key === "ArrowLeft";
    if (!forwardKey && !backwardKey && e.key !== "Home" && e.key !== "End") return;
    e.preventDefault();
    const count = tabs.length;
    if (count === 0) return;
    const current = tabs.findIndex((tab) => tab.id === activeId);
    if (e.key === "Home") return move(0);
    if (e.key === "End") return move(count - 1);
    if (current < 0) return move(0);
    // Flexbox rows follow the writing direction: in RTL, visual "next" is Left.
    const rtl = listRef.current ? getComputedStyle(listRef.current).direction === "rtl" : false;
    const forward = rtl ? backwardKey : forwardKey;
    move(forward ? (current + 1) % count : (current - 1 + count) % count);
  }

  return (
    <div className="tabs">
      <div ref={listRef} role="tablist" aria-label={label} className="tabs__list" onKeyDown={onKeyDown}>
        {tabs.map((tab) => {
          const active = tab.id === activeId;
          return (
            <button
              key={tab.id}
              type="button"
              role="tab"
              id={`${tab.id}-tab`}
              aria-selected={active}
              aria-controls={`${tab.id}-panel`}
              tabIndex={active ? 0 : -1}
              onClick={() => onChange(tab.id)}
            >
              {tab.label}
            </button>
          );
        })}
      </div>
      {tabs.map((tab) => (
        <div
          key={tab.id}
          role="tabpanel"
          id={`${tab.id}-panel`}
          aria-labelledby={`${tab.id}-tab`}
          hidden={tab.id !== activeId}
          className="tabs__panel"
        >
          {tab.content}
        </div>
      ))}
    </div>
  );
}

// ── Pagination ──────────────────────────────────────────────────────────────

export type PaginationLabels = {
  /** Accessible name for the <nav> region. */
  region: string;
  prev: string;
  next: string;
  /** aria-label prefix for numbered items, e.g. "Page" → "Page 2". */
  page: string;
};

type PaginationProps = {
  page: number;
  pageCount: number;
  onChange: (page: number) => void;
  labels: PaginationLabels;
};

export function Pagination({ page, pageCount, onChange, labels }: PaginationProps) {
  if (pageCount <= 0) return null;
  const pages = Array.from({ length: pageCount }, (_, i) => i + 1);
  return (
    <nav className="pagination" aria-label={labels.region}>
      <Button variant="secondary" small disabled={page <= 1} onClick={() => onChange(page - 1)}>
        {labels.prev}
      </Button>
      {pages.map((n) =>
        n === page ? (
          <span key={n} className="pagination__current" aria-current="page" aria-label={`${labels.page} ${n}`}>
            {n}
          </span>
        ) : (
          <Button key={n} variant="secondary" small onClick={() => onChange(n)} aria-label={`${labels.page} ${n}`}>
            {n}
          </Button>
        ),
      )}
      <Button variant="secondary" small disabled={page >= pageCount} onClick={() => onChange(page + 1)}>
        {labels.next}
      </Button>
    </nav>
  );
}
