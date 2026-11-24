"use client";

import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { createCustomer, fetchCustomers, type CustomerRow } from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { fmtMoney } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Table,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { salesActionError, SalesMeError, useSalesMe } from "../_lib/helpers";

const EMAIL_RE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

type FormState = { name: string; taxId: string; email: string; creditLimit: string; termDays: string; suppressed: boolean };
const EMPTY_FORM: FormState = { name: "", taxId: "", email: "", creditLimit: "", termDays: "30", suppressed: false };

export default function CustomersPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.customers;
  const { token, ready } = useAuth(locale);
  const { meFailed, retryMe } = useSalesMe(token);

  const [customers, setCustomers] = useState<CustomerRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [errors, setErrors] = useState<Partial<Record<keyof FormState, string>>>({});
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [toasts, setToasts] = useState<ToastItem[]>([]);

  function pushToast(tone: ToastItem["tone"], message: string) {
    setToasts((prev) => [...prev, { id: crypto.randomUUID(), tone, message }]);
  }
  function dismissToast(id: string) {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchCustomers(token);
    if (res.ok) {
      setCustomers(res.data);
      setPhase("success");
    } else {
      setCustomers(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  function validate(): boolean {
    const next: typeof errors = {};
    if (!form.name.trim()) next.name = tc.errors.nameRequired;
    if (!form.taxId.trim()) next.taxId = tc.errors.taxIdRequired;
    if (!form.email.trim()) next.email = tc.errors.emailRequired;
    else if (!EMAIL_RE.test(form.email)) next.email = tc.errors.emailInvalid;
    if (form.creditLimit.trim() && (!Number.isFinite(Number(form.creditLimit)) || Number(form.creditLimit) < 0)) {
      next.creditLimit = tc.errors.limitInvalid;
    }
    if (!form.termDays.trim() || !Number.isFinite(Number(form.termDays)) || Number(form.termDays) < 1) {
      next.termDays = tc.errors.termInvalid;
    }
    setErrors(next);
    return Object.keys(next).length === 0;
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    setCreateError(null);
    if (!validate() || !token) return;
    setCreating(true);
    const res = await createCustomer(token, {
      name: form.name.trim(),
      taxId: form.taxId.trim(),
      email: form.email.trim(),
      creditLimit: form.creditLimit.trim() ? Number(form.creditLimit) : undefined,
      paymentTermNetDays: Number(form.termDays),
      remindersSuppressed: form.suppressed,
    });
    setCreating(false);
    if (!res.ok) {
      setCreateError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", `${tc.created} ${res.data.id}`);
    setForm(EMPTY_FORM);
    await load();
  }

  const columns: Column<CustomerRow>[] = [
    { key: "name", label: tc.columns.name, render: (c) => <span>{c.name}<span className="mono"> · {c.id}</span></span> },
    { key: "taxId", label: tc.columns.taxId, mono: true, render: (c) => c.taxId },
    { key: "email", label: tc.columns.email, render: (c) => c.email },
    { key: "limit", label: tc.columns.limit, mono: true, render: (c) => (c.creditLimit === null ? "—" : fmtMoney(c.creditLimit, locale, "BDT")) },
    { key: "terms", label: tc.columns.terms, render: (c) => <span>NET{c.paymentTermNetDays}{c.remindersSuppressed ? <> <Badge tone="muted">{tc.suppressedBadge}</Badge></> : null}</span> },
    { key: "exposure", label: t.sales.collections.exposureTitle, render: (c) => <Link href={`/${locale}/sales/collections?customerId=${c.id}`}>{tc.exposureLink}</Link> },
  ];

  if (!ready || (phase === "loading" && customers === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const list = customers ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}
      {createError ? <p className="banner" role="alert">{createError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.createTitle}</h2>
        <form onSubmit={(e) => void submit(e)}>
          <div className="form-grid">
            <Input id="cus-name" label={tc.name} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} error={errors.name} />
            <Input id="cus-tax" label={tc.taxId} value={form.taxId} onChange={(e) => setForm({ ...form, taxId: e.target.value })} error={errors.taxId} />
            <Input id="cus-email" label={tc.email} type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} error={errors.email} />
            <Input id="cus-limit" label={tc.creditLimit} type="number" min="0" step="any" value={form.creditLimit} onChange={(e) => setForm({ ...form, creditLimit: e.target.value })} error={errors.creditLimit} />
            <Input id="cus-term" label={tc.termDays} type="number" min="1" step="1" value={form.termDays} onChange={(e) => setForm({ ...form, termDays: e.target.value })} error={errors.termDays} />
          </div>
          <p>
            <label htmlFor="cus-suppressed" style={{ fontWeight: 600 }}>
              <input id="cus-suppressed" type="checkbox" checked={form.suppressed} onChange={(e) => setForm({ ...form, suppressed: e.target.checked })} /> {tc.suppressed}
            </label>
          </p>
          <p><Button type="submit" loading={creating}>{tc.create}</Button></p>
        </form>
      </section>

      <section className="card">
        {list.length === 0 ? <EmptyState title={tc.empty} /> : <Table<CustomerRow> columns={columns} rows={list} caption={tc.title} />}
      </section>
    </div>
  );
}
