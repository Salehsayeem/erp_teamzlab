"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { createCredential, fetchCredentials, waiveCredential, type CredentialRow } from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtDay, hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../_lib/helpers";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Table, ToastRegion, type ToastItem } from "@/components/uikit";
import type { Column } from "@/components/uikit";

export default function TrainingPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.training;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canWrite = hasPermission(permissions, "credentials:write");

  const [creds, setCreds] = useState<CredentialRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({ employmentId: "", code: "", name: "", issuedBy: "", issuedOn: "", expiresOn: "", evidenceRef: "" });
  const [waiving, setWaiving] = useState<CredentialRow | null>(null);
  const [waiveReason, setWaiveReason] = useState("");

  const push = (tone: ToastItem["tone"], message: string) => setToasts((p) => [...p, { id: crypto.randomUUID(), tone, message }]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchCredentials(token);
    if (res.ok) { setCreds(res.data); setPhase("success"); } else setPhase("error");
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function run(fn: () => Promise<{ ok: boolean; code?: string; message?: string }>, label: string) {
    setBusy(true); setFormError(null);
    const res = await fn();
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code ?? "ERROR", res.message ?? "")); return false; }
    push("success", label);
    void load();
    return true;
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const ok = await run(() => createCredential(token, {
      employmentId: form.employmentId.trim(), code: form.code.trim(), name: form.name.trim(),
      issuedBy: form.issuedBy.trim() || null, issuedOn: form.issuedOn || null, expiresOn: form.expiresOn || null,
      evidenceRef: form.evidenceRef.trim() || null,
    }), form.code.trim());
    if (ok) setForm({ ...form, code: "", name: "", issuedBy: "", issuedOn: "", expiresOn: "", evidenceRef: "" });
  }

  async function confirmWaive() {
    if (!token || !waiving || !waiveReason.trim()) return;
    const ok = await run(() => waiveCredential(token, waiving.id, waiveReason.trim()), waiving.code);
    if (ok) { setWaiving(null); setWaiveReason(""); }
  }

  const columns: Column<CredentialRow>[] = useMemo(() => [
    { key: "worker", label: tc.columns.worker, mono: true, render: (c) => c.employmentId },
    { key: "code", label: tc.columns.code, mono: true, render: (c) => c.code },
    { key: "name", label: tc.columns.name, render: (c) => c.name },
    { key: "issued", label: tc.columns.issued, mono: true, render: (c) => (c.issuedOn ? fmtDay(c.issuedOn, locale) : "—") },
    { key: "expires", label: tc.columns.expires, mono: true, render: (c) => (c.expiresOn ? fmtDay(c.expiresOn, locale) : "—") },
    { key: "effective", label: tc.columns.effective, render: (c) => <Badge tone={peopleTone(c.effectiveStatus)}>{peopleStatusLabel(t, c.effectiveStatus)}</Badge> },
    { key: "actions", label: "", render: (c) => canWrite === true && c.effectiveStatus !== "Waived" ? (
        <Button variant="secondary" onClick={() => setWaiving(c)}>{tc.waive}</Button>
      ) : null },
  ], [tc, t, locale, canWrite]);

  if (!ready || phase === "loading") return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={(id) => setToasts((p) => p.filter((x) => x.id !== id))} closeLabel={t.common.cancel} />
      {formError ? <p className="banner" role="alert">{formError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.formTitle}</h2>
        {canWrite === true ? (
          <form onSubmit={(e) => void submit(e)}>
            <div className="form-grid">
              <Input id="tr-worker" label={tc.workerId} value={form.employmentId} onChange={(e) => setForm({ ...form, employmentId: e.target.value })} />
              <Input id="tr-code" label={tc.code} value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} />
              <Input id="tr-name" label={tc.name} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
              <Input id="tr-issuedby" label={tc.issuedBy} value={form.issuedBy} onChange={(e) => setForm({ ...form, issuedBy: e.target.value })} />
              <Input id="tr-issuedon" label={tc.issuedOn} type="date" value={form.issuedOn} onChange={(e) => setForm({ ...form, issuedOn: e.target.value })} />
              <Input id="tr-expires" label={tc.expiresOn} type="date" value={form.expiresOn} onChange={(e) => setForm({ ...form, expiresOn: e.target.value })} />
              <Input id="tr-evidence" label={tc.evidence} value={form.evidenceRef} onChange={(e) => setForm({ ...form, evidenceRef: e.target.value })} />
            </div>
            <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.create}</Button></p>
          </form>
        ) : <p className="t-caption">{t.people.errors.forbidden}</p>}
      </section>

      <section className="card">
        {(creds ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={columns} rows={creds ?? []} caption={tc.title} />}
      </section>

      <Modal open={waiving !== null} onClose={() => { setWaiving(null); setWaiveReason(""); }} title={tc.waiveTitle + " — " + (waiving?.code ?? "")} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-caption">{tc.waiveHint}</p>
        <Input id="tr-waive-reason" label={tc.waiveReason} value={waiveReason} onChange={(e) => setWaiveReason(e.target.value)} />
        <p><Button onClick={() => void confirmWaive()} loading={busy}>{tc.waive}</Button></p>
      </Modal>
    </div>
  );
}
