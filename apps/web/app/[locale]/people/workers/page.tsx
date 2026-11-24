"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { createWorker, fetchWorkers, type Worker } from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
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
import { fmtDay, peopleActionError, peopleStatusLabel, peopleTone } from "../_lib/helpers";

const WORKER_STATUSES = ["Draft", "Prehire", "Active", "OnLeave", "Suspended", "Terminated"];

type WorkerForm = {
  fullName: string; preferredName: string; nationalId: string; email: string; phone: string;
  categoryCode: string; hireDate: string; weeklyHours: string; locationId: string; managerUserId: string;
};
const EMPTY_FORM: WorkerForm = {
  fullName: "", preferredName: "", nationalId: "", email: "", phone: "",
  categoryCode: "", hireDate: "", weeklyHours: "40", locationId: "", managerUserId: "",
};

export default function WorkersPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.workers;
  const { token, ready } = useAuth(locale);

  const [workers, setWorkers] = useState<Worker[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState("");
  const [search, setSearch] = useState("");
  const [searchDebounced, setSearchDebounced] = useState("");

  const [form, setForm] = useState<WorkerForm>(EMPTY_FORM);
  const [errors, setErrors] = useState<Partial<Record<keyof WorkerForm, string>>>({});
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
    setErrorCode(null);
    const res = await fetchWorkers(token, statusFilter || undefined, searchDebounced || undefined);
    if (res.ok) {
      setWorkers(res.data);
      setPhase("success");
    } else {
      setWorkers(null);
      setErrorCode(res.code);
      setPhase("error");
    }
  }, [token, statusFilter, searchDebounced]);
  useEffect(() => { void load(); }, [load]);

  // Debounce the search box — the API runs it against name/id/national id.
  useEffect(() => {
    const timer = window.setTimeout(() => setSearchDebounced(search.trim()), 350);
    return () => window.clearTimeout(timer);
  }, [search]);

  const isForbidden = errorCode === "FORBIDDEN" || errorCode === "HTTP_403";

  async function submit(e: FormEvent) {
    e.preventDefault();
    const errs: Partial<Record<keyof WorkerForm, string>> = {};
    if (!form.fullName.trim()) errs.fullName = tc.errors.nameRequired;
    if (!form.categoryCode.trim()) errs.categoryCode = tc.errors.categoryRequired;
    if (!form.hireDate) errs.hireDate = tc.errors.hireDateRequired;
    const hours = Number(form.weeklyHours);
    if (!form.weeklyHours.trim() || !Number.isFinite(hours) || hours <= 0) errs.weeklyHours = tc.errors.hoursInvalid;
    if (form.email.trim() && !form.email.includes("@")) errs.email = tc.errors.emailInvalid;
    setErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;

    setCreating(true);
    setCreateError(null);
    const res = await createWorker(token, {
      fullName: form.fullName.trim(),
      preferredName: form.preferredName.trim() || null,
      nationalId: form.nationalId.trim() || null,
      email: form.email.trim() || null,
      phone: form.phone.trim() || null,
      categoryCode: form.categoryCode.trim(),
      hireDate: form.hireDate,
      weeklyPlannedHours: hours,
      locationId: form.locationId.trim() || null,
      managerUserId: form.managerUserId.trim() || null,
    });
    setCreating(false);
    if (!res.ok) {
      setCreateError(peopleActionError(t, res.code, res.message));
      if (res.code === "PERSON_DUPLICATE") pushToast("error", tc.duplicateNote);
      return;
    }
    pushToast("success", `${tc.registered} ${res.data.id}`);
    setWorkers((prev) => (prev ? [res.data, ...prev] : prev));
    setForm(EMPTY_FORM);
  }

  const columns: Column<Worker>[] = useMemo(() => [
    {
      key: "worker", label: tc.columns.worker, mono: true,
      render: (w) => (
        <Link href={`/${locale}/people/workers/${w.id}`}>
          {w.fullName}
          <span className="t-caption"> · {w.workerNo}</span>
        </Link>
      ),
    },
    { key: "status", label: tc.columns.status, render: (w) => <Badge tone={peopleTone(w.status)}>{peopleStatusLabel(t, w.status)}</Badge> },
    { key: "category", label: tc.columns.category, render: (w) => w.categoryCode },
    { key: "hireDate", label: tc.columns.hireDate, render: (w) => <span className="t-caption">{fmtDay(w.hireDate, locale)}</span> },
    { key: "weeklyHours", label: tc.columns.weeklyHours, mono: true, render: (w) => fmtNumber(w.weeklyPlannedHours, locale) },
    { key: "details", label: "", render: (w) => <Link href={`/${locale}/people/workers/${w.id}`} className="btn btn--secondary btn--sm">{tc.details}</Link> },
  ], [t, tc, locale]);

  if (!ready || (phase === "loading" && workers === null)) return <LoadingSkeleton rows={7} />;
  if (phase === "error") {
    if (isForbidden) {
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

  const list = workers ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {createError ? <p className="banner" role="alert">{createError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.registerTitle}</h2>
        <p className="t-caption">{tc.registerHint}</p>
        <form onSubmit={(e) => void submit(e)}>
          <div className="form-grid">
            <Input id="w-fullname" label={tc.fullName} value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} error={errors.fullName} />
            <Input id="w-category" label={tc.categoryCode} value={form.categoryCode} onChange={(e) => setForm({ ...form, categoryCode: e.target.value })} error={errors.categoryCode} />
            <Input id="w-hiredate" label={tc.hireDate} type="date" value={form.hireDate} onChange={(e) => setForm({ ...form, hireDate: e.target.value })} error={errors.hireDate} />
            <Input id="w-hours" label={tc.weeklyHours} type="number" min="1" step="any" value={form.weeklyHours} onChange={(e) => setForm({ ...form, weeklyHours: e.target.value })} error={errors.weeklyHours} />
            <Input id="w-preferred" label={tc.preferredName} value={form.preferredName} onChange={(e) => setForm({ ...form, preferredName: e.target.value })} />
            <Input id="w-nationalid" label={tc.nationalId} value={form.nationalId} onChange={(e) => setForm({ ...form, nationalId: e.target.value })} />
            <Input id="w-email" label={tc.email} type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} error={errors.email} />
            <Input id="w-phone" label={tc.phone} value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
            <Input id="w-location" label={tc.locationId} value={form.locationId} onChange={(e) => setForm({ ...form, locationId: e.target.value })} />
            <Input id="w-manager" label={tc.managerUserId} value={form.managerUserId} onChange={(e) => setForm({ ...form, managerUserId: e.target.value })} hint={tc.managerHint} />
          </div>
          <p><Button type="submit" loading={creating}>{tc.register}</Button></p>
        </form>
      </section>

      <section className="card">
        <div className="form-grid">
          <Select
            id="w-status-filter"
            label={tc.statusFilter}
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            options={WORKER_STATUSES.map((s) => ({ value: s, label: peopleStatusLabel(t, s) }))}
            placeholder={t.people.statusFilterAll}
          />
          <Input id="w-search" label={tc.searchLabel} value={search} onChange={(e) => setSearch(e.target.value)} hint={tc.searchPlaceholder} />
        </div>
        {list.length === 0 ? (
          <EmptyState title={statusFilter || searchDebounced ? tc.noMatch : tc.empty} />
        ) : (
          <Table<Worker> columns={columns} rows={list} caption={tc.title} />
        )}
      </section>
    </div>
  );
}
