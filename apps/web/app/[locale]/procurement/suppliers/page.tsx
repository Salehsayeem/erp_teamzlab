"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { createSupplier, fetchSuppliers, type Supplier } from "@/lib/services/api";
import { fmtDate } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Button, EmptyState, ErrorState, Input, LoadingSkeleton, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";

const EMAIL_RE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

type FormState = { name: string; taxId: string; email: string };
type FormErrors = Partial<Record<keyof FormState, string>>;

export default function SuppliersPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [suppliers, setSuppliers] = useState<Supplier[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [form, setForm] = useState<FormState>({ name: "", taxId: "", email: "" });
  const [errors, setErrors] = useState<FormErrors>({});
  const [createError, setCreateError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchSuppliers(token);
    if (res.ok) {
      setSuppliers(res.data);
      setPhase("success");
    } else {
      setSuppliers(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  function validate(): FormErrors {
    const next: FormErrors = {};
    if (!form.name.trim()) next.name = t.suppliers.errors.nameRequired;
    if (!form.taxId.trim()) next.taxId = t.suppliers.errors.taxIdRequired;
    if (!form.email.trim()) next.email = t.suppliers.errors.emailRequired;
    else if (!EMAIL_RE.test(form.email)) next.email = t.suppliers.errors.emailInvalid;
    return next;
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const errs = validate();
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;
    if (!token) return;
    setCreating(true);
    setCreateError(null);
    const res = await createSupplier(token, { name: form.name.trim(), taxId: form.taxId.trim(), email: form.email.trim() });
    setCreating(false);
    if (!res.ok) {
      setCreateError(res.code === "NETWORK" ? t.common.networkError : `${t.suppliers.createFailed} ${res.message}`);
      return;
    }
    setSuppliers((prev) => [res.data, ...(prev ?? [])]);
    setForm({ name: "", taxId: "", email: "" });
  }

  const columns: Column<Supplier>[] = [
    { key: "name", label: t.suppliers.columns.name, render: (s) => s.name },
    { key: "taxId", label: t.suppliers.columns.taxId, mono: true, render: (s) => s.taxId },
    { key: "email", label: t.suppliers.columns.email, render: (s) => s.email },
    { key: "created", label: t.suppliers.columns.created, render: (s) => <span className="t-caption">{fmtDate(s.createdAt, locale)}</span> },
  ];

  if (!ready || (phase === "loading" && suppliers === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={t.suppliers.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  const list = suppliers ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.suppliers.title}</h1>
        <p className="t-caption">{t.suppliers.subtitle}</p>
      </div>
      {createError ? <p className="banner" role="alert">{createError}</p> : null}
      <section className="card">
        <h2 className="t-title">{t.suppliers.addTitle}</h2>
        <form onSubmit={(e) => { void submit(e); }}>
          <div className="form-grid">
            <Input id="sup-name" label={t.suppliers.name} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} error={errors.name} />
            <Input id="sup-tax" label={t.suppliers.taxId} value={form.taxId} onChange={(e) => setForm({ ...form, taxId: e.target.value })} error={errors.taxId} />
            <Input id="sup-email" label={t.suppliers.email} type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} error={errors.email} />
          </div>
          <p><Button type="submit" loading={creating}>{t.suppliers.create}</Button></p>
        </form>
      </section>
      <section className="card">
        {list.length === 0 ? <EmptyState title={t.suppliers.empty} /> : <Table<Supplier> columns={columns} rows={list} caption={t.suppliers.title} />}
      </section>
    </div>
  );
}
