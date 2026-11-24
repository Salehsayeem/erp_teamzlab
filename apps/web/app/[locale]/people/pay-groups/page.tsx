"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { createPayGroup, fetchPayGroups, type PayGroupRow } from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
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
import { peopleActionError, usePeopleMe } from "../_lib/helpers";

type GroupForm = { code: string; name: string; frequency: string };
const EMPTY_GROUP: GroupForm = { code: "", name: "", frequency: "Monthly" };

export default function PayGroupsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.payGroups;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);

  const [groups, setGroups] = useState<PayGroupRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);

  const [form, setForm] = useState<GroupForm>(EMPTY_GROUP);
  const [errors, setErrors] = useState<Partial<Record<keyof GroupForm, string>>>({});
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
    const res = await fetchPayGroups(token);
    if (res.ok) {
      setGroups(res.data);
      setPhase("success");
    } else {
      setGroups(null);
      setErrorCode(res.code);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const errs: Partial<Record<keyof GroupForm, string>> = {};
    if (!form.code.trim()) errs.code = tc.errors.codeRequired;
    if (!form.name.trim()) errs.name = tc.errors.nameRequired;
    if (!form.frequency.trim()) errs.frequency = tc.errors.frequencyRequired;
    setErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setCreating(true);
    setCreateError(null);
    const res = await createPayGroup(token, {
      code: form.code.trim(), name: form.name.trim(), frequency: form.frequency.trim(),
    });
    setCreating(false);
    if (!res.ok) {
      setCreateError(peopleActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.created + " " + res.data.code);
    setGroups((prev) => (prev ? [res.data, ...prev] : prev));
    setForm(EMPTY_GROUP);
  }

  const columns: Column<PayGroupRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.code, mono: true, render: (g) => g.code },
    { key: "name", label: tc.columns.name, render: (g) => g.name },
    { key: "frequency", label: tc.columns.frequency, render: (g) => g.frequency },
    { key: "members", label: tc.columns.members, mono: true, render: (g) => fmtNumber(g.members.length, locale) },
    { key: "status", label: "", render: (g) => <Badge tone={g.active ? "success" : "muted"}>{g.active ? t.people.payCodes.activeBadge : t.people.payCodes.inactiveBadge}</Badge> },
  ], [t, tc, locale]);

  if (!ready || (phase === "loading" && groups === null)) return <LoadingSkeleton rows={5} />;
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

  const list = groups ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {createError ? <p className="banner" role="alert">{createError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.formTitle}</h2>
        <form onSubmit={(e) => void submit(e)}>
          <div className="form-grid">
            <Input id="pgp-code" label={tc.code} value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} error={errors.code} />
            <Input id="pgp-name" label={tc.name} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} error={errors.name} />
            <Input id="pgp-freq" label={tc.frequency} value={form.frequency} onChange={(e) => setForm({ ...form, frequency: e.target.value })} error={errors.frequency} hint={tc.frequencyHint} />
          </div>
          <p><Button type="submit" loading={creating} disabled={!meReady}>{tc.create}</Button></p>
        </form>
      </section>

      <section className="card">
        {list.length === 0 ? <EmptyState title={tc.empty} /> : (
          <Table columns={columns} rows={list} caption={tc.title} />
        )}
      </section>
    </div>
  );
}
