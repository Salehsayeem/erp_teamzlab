"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createPaymentFile, fetchPayRuns, fetchPaymentFiles, releasePaymentFile, rejectPaymentFileItems,
  type PaymentFileRow, type PayRunRow,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtMoney } from "@/lib/format";
import {
  Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Table, ToastRegion, type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../_lib/helpers";

export default function PaymentFilesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.payfile;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canRelease = hasPermission(permissions, "payroll:release");
  const canCreate = hasPermission(permissions, "payroll:calculate");

  const [files, setFiles] = useState<PaymentFileRow[] | null>(null);
  const [runs, setRuns] = useState<PayRunRow[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [createForm, setCreateForm] = useState({ runId: "", provider: "BANK-01" });
  const [releasing, setReleasing] = useState<PaymentFileRow | null>(null);
  const [rejecting, setRejecting] = useState<{ file: PaymentFileRow; employmentId: string } | null>(null);
  const [rejectReason, setRejectReason] = useState("");

  const push = (tone: ToastItem["tone"], message: string) => setToasts((p) => [...p, { id: crypto.randomUUID(), tone, message }]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [f, r] = await Promise.all([fetchPaymentFiles(token), fetchPayRuns(token, "Approved")]);
    if (f.ok && r.ok) {
      setFiles(f.data); setRuns(r.data); setPhase("success");
    } else setPhase("error");
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function run(fn: () => Promise<{ ok: boolean; code?: string; message?: string }>, label: string, refresh = true) {
    setBusy(true); setFormError(null);
    const res = await fn();
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code ?? "ERROR", res.message ?? "")); return false; }
    push("success", label);
    if (refresh) void load();
    return true;
  }

  async function submitCreate(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const ok = await run(() => createPaymentFile(token, createForm.runId.trim(), createForm.provider.trim()), createForm.runId.trim());
    if (ok) setCreateForm({ runId: "", provider: createForm.provider });
  }

  async function confirmRelease() {
    if (!token || !releasing) return;
    const ok = await run(() => releasePaymentFile(token, releasing.id), releasing.id);
    if (ok) setReleasing(null);
  }

  async function submitReject(e: FormEvent) {
    e.preventDefault();
    if (!token || !rejecting || !rejectReason.trim()) return;
    const ok = await run(
      () => rejectPaymentFileItems(token, rejecting.file.id, [{ employmentId: rejecting.employmentId, reason: rejectReason.trim() }]),
      rejecting.employmentId,
    );
    if (ok) { setRejecting(null); setRejectReason(""); }
  }

  const fileColumns: Column<PaymentFileRow>[] = useMemo(() => [
    { key: "id", label: tc.columns.file, mono: true, render: (x) => (
        <button type="button" className="t-body-strong mono" style={{ background: "none", border: "none", padding: 0, cursor: "pointer", textDecoration: "underline" }} onClick={() => setSelected(selected === x.id ? null : x.id)}>{x.id}</button>
      ) },
    { key: "run", label: tc.columns.run, mono: true, render: (x) => x.runId },
    { key: "status", label: tc.columns.status, render: (x) => <Badge tone={peopleTone(x.status)}>{peopleStatusLabel(t, x.status)}</Badge> },
    { key: "provider", label: tc.columns.provider, mono: true, render: (x) => x.provider },
    { key: "items", label: tc.columns.items, mono: true, render: (x) => String(x.itemCount) },
    { key: "total", label: tc.columns.total, mono: true, render: (x) => fmtMoney(x.totalNet, locale, x.currency) },
    { key: "maker", label: tc.columns.maker, mono: true, render: (x) => x.createdBy },
    { key: "checker", label: tc.columns.checker, mono: true, render: (x) => x.releasedBy ?? "—" },
    { key: "hash", label: tc.columns.hash, mono: true, render: (x) => <span className="t-caption mono">{x.contentHash.slice(0, 12)}…</span> },
    { key: "actions", label: "", render: (x) => x.status === "Prepared" && canRelease === true ? (
        <Button variant="secondary" onClick={() => setReleasing(x)}>{tc.release}</Button>
      ) : null },
  ], [tc, t, locale, canRelease, selected]);

  const selectedFile = (files ?? []).find((f) => f.id === selected) ?? null;
  const itemColumns: Column<PaymentFileRow["items"][number]>[] = useMemo(() => [
    { key: "worker", label: tc.columns.worker, mono: true, render: (i) => i.employmentId },
    { key: "net", label: tc.columns.net, mono: true, render: (i) => fmtMoney(i.netAmount, locale, selectedFile?.currency ?? "BDT") },
    { key: "status", label: tc.columns.status, render: (i) => <Badge tone={peopleTone(i.status)}>{peopleStatusLabel(t, i.status)}</Badge> },
    { key: "reason", label: tc.columns.reason, render: (i) => i.rejectReason ?? "—" },
    { key: "actions", label: "", render: (i) => i.status === "Accepted" && canRelease === true ? (
        <Button variant="secondary" onClick={() => setRejecting({ file: selectedFile!, employmentId: i.employmentId })}>{tc.reject}</Button>
      ) : null },
  ], [tc, t, locale, canRelease, selectedFile]);

  if (!ready || phase === "loading") return <LoadingSkeleton rows={6} />;
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
        <h2 className="t-title">{tc.createTitle}</h2>
        {canCreate === true ? (
          <form onSubmit={(e) => void submitCreate(e)} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
            <div>
              <label htmlFor="pf-run" className="t-caption">{tc.runId}</label>
              <select id="pf-run" className="input mono" value={createForm.runId} onChange={(e) => setCreateForm({ ...createForm, runId: e.target.value })} required>
                <option value="">—</option>
                {(runs ?? []).map((r) => <option key={r.id} value={r.id}>{r.id}</option>)}
              </select>
            </div>
            <Input id="pf-provider" label={tc.provider} value={createForm.provider} onChange={(e) => setCreateForm({ ...createForm, provider: e.target.value })} />
            <Button type="submit" loading={busy} disabled={!meReady || !createForm.runId}>{tc.create}</Button>
          </form>
        ) : <p className="t-caption">{t.people.errors.forbidden}</p>}
      </section>

      <section className="card">
        <p className="t-caption">{tc.makerCheckerNote}</p>
        {(files ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={fileColumns} rows={files ?? []} caption={tc.title} />}
      </section>

      {selectedFile ? (
        <section className="card">
          <h2 className="t-title">{selectedFile.id} — {tc.columns.items} ({selectedFile.itemCount})</h2>
          {selectedFile.items.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={itemColumns} rows={selectedFile.items} caption={tc.columns.items} />}
        </section>
      ) : null}

      <Modal open={releasing !== null} onClose={() => setReleasing(null)} title={tc.release + " — " + (releasing?.id ?? "")} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-caption">{tc.releaseHint}</p>
        <p className="t-body-strong mono">{releasing?.itemCount} × {releasing ? fmtMoney(releasing.totalNet, locale, releasing.currency) : ""}</p>
        <p><Button onClick={() => void confirmRelease()} loading={busy}>{tc.release}</Button></p>
      </Modal>

      <Modal open={rejecting !== null} onClose={() => { setRejecting(null); setRejectReason(""); }} title={tc.rejectTitle + " — " + (rejecting?.employmentId ?? "")} closeLabel={t.common.cancel} busy={busy}>
        <Input id="pf-reject-reason" label={tc.rejectReason} value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} />
        <p><Button type="submit" onClick={(e) => void submitReject(e)} loading={busy}>{tc.reject}</Button></p>
      </Modal>
    </div>
  );
}
