"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchMe } from "@/lib/services/api";
import { createContract, fetchContracts, fetchSupplierRows, procError, type Contract, type ContractStatus } from "@/lib/services/procurement";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Select, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { CONTRACT_STATUS_TONES, hasPerm, isoDate, statusLabel } from "../_lib/ui";

type Phase = "loading" | "error" | "success";
type FormState = {
  supplierId: string;
  awardId: string;
  currency: string;
  effectiveFrom: string;
  effectiveTo: string;
  renewalNoticeDays: string;
  optionPeriodsTotal: string;
};

const EMPTY_FORM: FormState = { supplierId: "", awardId: "", currency: "BDT", effectiveFrom: "", effectiveTo: "", renewalNoticeDays: "", optionPeriodsTotal: "" };

export default function ContractsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const c = t.procurement.contracts;
  const { token, ready } = useAuth(locale);

  const [status, setStatus] = useState("");
  const [expiringDays, setExpiringDays] = useState("");
  const [contracts, setContracts] = useState<Contract[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);

  const [suppliers, setSuppliers] = useState<{ value: string; label: string }[]>([]);
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [errors, setErrors] = useState<Partial<Record<keyof FormState, string>>>({});
  const [createError, setCreateError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [meId, setMeId] = useState<string | null>(null);
  const [canWrite, setCanWrite] = useState<boolean | null>(null);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchMe(token).then((res) => {
      if (cancelled) return;
      if (res.ok) {
        setMeId(res.data.user.id);
        setCanWrite(hasPerm(res.data.permissions, "contracts:write"));
      } else setCanWrite(false);
    });
    return () => { cancelled = true; };
  }, [token]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchSupplierRows(token).then((res) => {
      if (!cancelled && res.ok) setSuppliers(res.data.map((s) => ({ value: s.id, label: `${s.name} (${s.taxId})` })));
    });
    return () => { cancelled = true; };
  }, [token]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const days = expiringDays.trim() === "" ? undefined : Number(expiringDays);
    const res = await fetchContracts(token, {
      status: status || undefined,
      expiringWithinDays: days !== undefined && Number.isFinite(days) && days >= 0 ? days : undefined,
    });
    if (res.ok) {
      setContracts(res.data);
      setPhase("success");
    } else {
      setContracts(null);
      setErrorCode(res.code);
      setPhase("error");
    }
  }, [token, status, expiringDays]);
  useEffect(() => { void load(); }, [load]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const errs: Partial<Record<keyof FormState, string>> = {};
    if (!form.supplierId) errs.supplierId = c.errors.supplierRequired;
    if (!form.currency.trim()) errs.currency = c.errors.currencyRequired;
    if (!form.effectiveFrom || !form.effectiveTo || form.effectiveFrom > form.effectiveTo) errs.effectiveTo = c.errors.datesInvalid;
    setErrors(errs);
    setCreateError(null);
    if (Object.keys(errs).length > 0 || !token) return;
    setCreating(true);
    const res = await createContract(token, {
      supplierId: form.supplierId,
      awardId: form.awardId.trim() || null,
      currency: form.currency.trim().toUpperCase(),
      effectiveFrom: form.effectiveFrom,
      effectiveTo: form.effectiveTo,
      renewalNoticeDays: form.renewalNoticeDays ? Number(form.renewalNoticeDays) : null,
      optionPeriodsTotal: form.optionPeriodsTotal ? Number(form.optionPeriodsTotal) : null,
    });
    setCreating(false);
    if (!res.ok) {
      const mapped: Record<string, string> = {
        AWARD_NOT_APPROVED: c.form.awardIdHint,
        AWARD_SUPPLIER_MISMATCH: c.form.awardIdHint,
        AWARD_ALREADY_CONTRACTED: c.form.awardIdHint,
        FORBIDDEN: t.procurement.common.forbidden,
      };
      setCreateError(procError(res.code, res.message, mapped, c.createFailed));
      return;
    }
    setContracts((prev) => [res.data, ...(prev ?? [])]);
    setForm(EMPTY_FORM);
  }

  const label = (v: string) => statusLabel(t, v);
  const columns: Column<Contract>[] = [
    { key: "id", label: c.columns.id, mono: true, render: (x) => x.id },
    { key: "supplier", label: c.columns.supplier, mono: true, render: (x) => x.supplierId },
    { key: "status", label: c.columns.status, render: (x) => <Badge tone={CONTRACT_STATUS_TONES[x.status] ?? "muted"}>{label(x.status)}</Badge> },
    { key: "currency", label: c.columns.currency, mono: true, render: (x) => x.currency },
    { key: "validFrom", label: c.columns.validFrom, mono: true, render: (x) => isoDate(x.effectiveFrom) },
    { key: "validTo", label: c.columns.validTo, mono: true, render: (x) => isoDate(x.effectiveTo) },
    { key: "options", label: c.columns.options, mono: true, render: (x) => `${x.optionsExercised}/${x.optionPeriodsTotal ?? 0}` },
    { key: "amendments", label: c.columns.amendments, mono: true, render: (x) => x.amendmentCount },
    {
      key: "overdue",
      label: c.columns.overdue,
      render: (x) => (x.overdueObligations > 0 ? <Badge tone="danger">{x.overdueObligations}</Badge> : <span className="t-caption">0</span>),
    },
    {
      key: "noticeDue",
      label: c.columns.noticeDue,
      render: (x) => (x.noticeDue ? <Badge tone="warning">{c.noticeDueBadge}</Badge> : "—"),
    },
    {
      key: "actions",
      label: c.columns.actions,
      render: (x) => (
        <div className="row-actions">
          <Link className="btn btn--secondary btn--sm" href={`/${locale}/procurement/contracts/${x.id}`}>{c.openWorkspace}</Link>
        </div>
      ),
    },
  ];

  if (!ready || (phase === "loading" && contracts === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    const forbidden = errorCode === "FORBIDDEN" || errorCode === "HTTP_403";
    return (
      <div className="stack">
        <div className="page-header">
          <h1 className="t-display">{c.title}</h1>
          <p className="t-caption">{c.subtitle}</p>
        </div>
        <div className="state state--error" role="alert">
          <p className="state__title">{forbidden ? c.forbidden : c.loadFailed}</p>
          {!forbidden ? <Button variant="secondary" onClick={() => void load()}>{t.common.retry}</Button> : null}
        </div>
      </div>
    );
  }
  const list = contracts ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{c.title}</h1>
        <p className="t-caption">{c.subtitle}</p>
      </div>
      {createError ? <p className="banner" role="alert">{createError}</p> : null}

      {canWrite === false ? null : (
        <section className="card">
          <h2 className="t-title">{c.newTitle}</h2>
          <form onSubmit={(e) => void submit(e)}>
            <div className="form-grid">
              <Select
                id="ct-supplier"
                label={c.form.supplier}
                placeholder={c.form.supplier}
                options={suppliers}
                value={form.supplierId}
                onChange={(e) => setForm({ ...form, supplierId: e.target.value })}
                error={errors.supplierId}
              />
              <Input id="ct-award" label={c.form.awardId} hint={c.form.awardIdHint} value={form.awardId} onChange={(e) => setForm({ ...form, awardId: e.target.value })} />
              <Input id="ct-currency" label={c.form.currency} value={form.currency} onChange={(e) => setForm({ ...form, currency: e.target.value })} error={errors.currency} />
              <Input id="ct-from" label={c.form.effectiveFrom} type="date" value={form.effectiveFrom} onChange={(e) => setForm({ ...form, effectiveFrom: e.target.value })} />
              <Input id="ct-to" label={c.form.effectiveTo} type="date" value={form.effectiveTo} onChange={(e) => setForm({ ...form, effectiveTo: e.target.value })} error={errors.effectiveTo} />
              <Input id="ct-notice" label={c.form.renewalNoticeDays} type="number" min="0" step="1" value={form.renewalNoticeDays} onChange={(e) => setForm({ ...form, renewalNoticeDays: e.target.value })} />
              <Input id="ct-options" label={c.form.optionPeriodsTotal} type="number" min="0" step="1" value={form.optionPeriodsTotal} onChange={(e) => setForm({ ...form, optionPeriodsTotal: e.target.value })} />
            </div>
            <p><Button type="submit" loading={creating} disabled={meId === null}>{c.newTitle}</Button></p>
          </form>
        </section>
      )}

      <section className="card">
        <div className="form-grid">
          <Select
            id="ct-filter-status"
            label={c.filters.status}
            placeholder={c.filters.all}
            options={(["Draft", "Active", "Expired", "Terminated"] as ContractStatus[]).map((v) => ({ value: v, label: label(v) }))}
            value={status}
            onChange={(e) => setStatus(e.target.value)}
          />
          <Input id="ct-filter-exp" label={c.filters.expiringWithinDays} type="number" min="0" step="1" value={expiringDays} onChange={(e) => setExpiringDays(e.target.value)} />
        </div>
        {list.length === 0 ? <EmptyState title={c.empty} /> : <Table<Contract> columns={columns} rows={list} caption={c.title} />}
      </section>
    </div>
  );
}
