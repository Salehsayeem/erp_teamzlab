"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { createPayRun, fetchFiscalPeriods, fetchPayGroups, fetchPayRuns, type FiscalPeriodRow, type PayGroupRow, type PayRunRow } from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtMoney } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Select,
  Table,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { fmtDay, hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../_lib/helpers";

const RUN_STATUSES = ["Open", "Calculated", "Approved", "Posted", "Reversed"];

export default function PayRunsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.payRuns;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canOpen = hasPermission(permissions, "payroll:calculate");

  const [runs, setRuns] = useState<PayRunRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState("");

  const [groups, setGroups] = useState<PayGroupRow[]>([]);
  const [periods, setPeriods] = useState<FiscalPeriodRow[] | null>(null);
  const [periodsForbidden, setPeriodsForbidden] = useState(false);

  const [form, setForm] = useState({ payGroupId: "", periodId: "", payDate: "" });
  const [errors, setErrors] = useState<{ group?: string; period?: string; date?: string }>({});
  const [opening, setOpening] = useState(false);
  const [openError, setOpenError] = useState<string | null>(null);
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
    setErrorCode(null);
    const res = await fetchPayRuns(token, statusFilter || undefined);
    if (res.ok) {
      setRuns(res.data);
      setPhase("success");
    } else {
      setRuns(null);
      setErrorCode(res.code);
      setPhase("error");
    }
  }, [token, statusFilter]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchPayGroups(token).then((res) => { if (!cancelled && res.ok) setGroups(res.data); });
    // periods:read sits on R-FIN, not R-HR — the picker degrades to a text input on 403.
    void fetchFiscalPeriods(token, "Open").then((res) => {
      if (cancelled) return;
      if (res.ok) setPeriods(res.data);
      else setPeriodsForbidden(true);
    });
    return () => { cancelled = true; };
  }, [token]);

  // keep PayDate inside the chosen period (the API re-checks R-PEO5)
  function pickPeriod(id: string) {
    setForm((prev) => {
      const period = (periods ?? []).find((p) => p.id === id);
      const today = new Date().toISOString().slice(0, 10);
      const payDate = period && today >= period.startDate && today <= period.endDate ? today : period?.startDate ?? prev.payDate;
      return { ...prev, periodId: id, payDate };
    });
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const errs: { group?: string; period?: string; date?: string } = {};
    if (!form.payGroupId) errs.group = tc.errors.groupRequired;
    if (!form.periodId.trim()) errs.period = tc.errors.periodRequired;
    if (!form.payDate) errs.date = tc.errors.dateRequired;
    setErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setOpening(true);
    setOpenError(null);
    const res = await createPayRun(token, {
      payGroupId: form.payGroupId,
      periodId: form.periodId.trim(),
      payDate: form.payDate,
    });
    setOpening(false);
    if (!res.ok) {
      setOpenError(peopleActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.opened + " " + res.data.id);
    setRuns((prev) => (prev ? [res.data, ...prev] : prev));
    setForm({ payGroupId: "", periodId: "", payDate: "" });
  }

  const columns: Column<PayRunRow>[] = useMemo(() => [
    {
      key: "run", label: tc.columns.run, mono: true,
      render: (r) => <Link href={`/${locale}/people/pay-runs/${r.id}`}>{r.id}</Link>,
    },
    { key: "group", label: tc.columns.group, mono: true, render: (r) => r.payGroupId },
    { key: "period", label: tc.columns.period, mono: true, render: (r) => r.periodId },
    { key: "status", label: tc.columns.status, render: (r) => <Badge tone={peopleTone(r.status)}>{peopleStatusLabel(t, r.status)}</Badge> },
    { key: "payDate", label: tc.columns.payDate, render: (r) => <span className="t-caption">{fmtDay(r.payDate, locale)}</span> },
    { key: "gross", label: tc.columns.gross, mono: true, render: (r) => fmtMoney(r.grossTotal, locale, r.currency) },
    { key: "deductions", label: tc.columns.deductions, mono: true, render: (r) => fmtMoney(r.deductionTotal, locale, r.currency) },
    { key: "net", label: tc.columns.net, mono: true, render: (r) => fmtMoney(r.netTotal, locale, r.currency) },
    { key: "lines", label: tc.columns.lines, mono: true, render: (r) => (r.lineCount === 0 ? "—" : String(r.lineCount)) },
  ], [t, tc, locale]);

  if (!ready || (phase === "loading" && runs === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    if (errorCode === "FORBIDDEN" || errorCode === "HTTP_403") {
      return (
        <div className="stack">
          <div className="page-header">
            <h1 className="t-display">{tc.title}</h1>
            <p className="t-caption">{tc.subtitle}</p>
          </div>
          <div className="state state--error" role="alert"><p className="state__title">{tc.forbidden}</p></div>
        </div>
      );
    }
    return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  }

  const list = runs ?? [];
  const periodOptions = (periods ?? []).map((p) => ({ value: p.id, label: p.name + " (" + p.startDate + " … " + p.endDate + ")" }));
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {openError ? <p className="banner" role="alert">{openError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.openFormTitle}</h2>
        <p className="t-caption">{tc.openHint}</p>
        {groups.length === 0 ? <p className="t-caption">{tc.noGroups}</p> : canOpen === false ? <p className="t-caption">{t.people.errors.forbidden}</p> : (
          <form onSubmit={(e) => void submit(e)}>
            <div className="form-grid">
              <Select
                id="pr-group" label={tc.payGroup} value={form.payGroupId}
                onChange={(e) => setForm({ ...form, payGroupId: e.target.value })}
                options={groups.filter((g) => g.active).map((g) => ({ value: g.id, label: g.code + " — " + g.name }))}
                placeholder={tc.payGroup} error={errors.group}
              />
              {periods === null && !periodsForbidden ? (
                <Input id="pr-period" label={tc.period} value={form.periodId} onChange={(e) => setForm({ ...form, periodId: e.target.value })} />
              ) : periodsForbidden ? (
                <Input id="pr-period-fallback" label={tc.periodFallback} value={form.periodId} onChange={(e) => setForm({ ...form, periodId: e.target.value })} hint={tc.periodFallbackHint} error={errors.period} />
              ) : (
                <Select id="pr-period-select" label={tc.period} value={form.periodId} onChange={(e) => pickPeriod(e.target.value)} options={periodOptions} placeholder={tc.period} error={errors.period} />
              )}
              <Input id="pr-date" label={tc.payDate} type="date" value={form.payDate} onChange={(e) => setForm({ ...form, payDate: e.target.value })} hint={tc.payDateHint} error={errors.date} />
            </div>
            <p><Button type="submit" loading={opening} disabled={!meReady}>{tc.open}</Button></p>
          </form>
        )}
      </section>

      <section className="card">
        <div className="form-grid">
          <Select id="pr-status-filter" label={tc.statusFilter} value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} options={RUN_STATUSES.map((s) => ({ value: s, label: peopleStatusLabel(t, s) }))} placeholder={t.people.statusFilterAll} />
        </div>
        {list.length === 0 ? <EmptyState title={statusFilter ? tc.noMatch : tc.empty} /> : (
          <Table columns={columns} rows={list} caption={tc.title} />
        )}
      </section>
    </div>
  );
}
