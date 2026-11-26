"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  closePayPeriod, createPayrollAccountMapping, fetchCloseChecklist, fetchFiscalPeriods, fetchPayrollAccountMappings,
  reopenPayPeriod,
  type CloseChecklist, type FiscalPeriodRow, type PayrollMappingRow, type PeriodCloseRow,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Table, ToastRegion, type ToastItem } from "@/components/uikit";
import type { Column, BadgeTone } from "@/components/uikit";
import { hasPermission, peopleActionError, peopleTone, usePeopleMe } from "../_lib/helpers";

/** Checklist item tone: Ok passes, Waived is neutral, everything else is attention. */
function closeTone(status: string): BadgeTone {
  return status === "Ok" ? "success" : status === "Waived" ? "muted" : "danger";
}

export default function PayrollClosePage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.close;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canClose = hasPermission(permissions, "payroll:close");
  const canMap = hasPermission(permissions, "paycodes:write");

  const [periods, setPeriods] = useState<FiscalPeriodRow[] | null>(null);
  const [periodId, setPeriodId] = useState("");
  const [checklist, setChecklist] = useState<CloseChecklist | null>(null);
  const [mappings, setMappings] = useState<PayrollMappingRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [waivers, setWaivers] = useState<{ code: string; reason: string }[]>([]);
  const [acknowledgeLater, setAcknowledgeLater] = useState(false);
  const [confirmingClose, setConfirmingClose] = useState(false);
  const [closed, setClosed] = useState<PeriodCloseRow | null>(null);
  const [reopenOpen, setReopenOpen] = useState(false);
  const [reopenReason, setReopenReason] = useState("");
  const [mappingForm, setMappingForm] = useState({ element: "", accountCode: "", effectiveFrom: "" });

  const push = (tone: ToastItem["tone"], message: string) => setToasts((p) => [...p, { id: crypto.randomUUID(), tone, message }]);

  const loadChecklist = useCallback(async (pid: string) => {
    if (!token || !pid) return;
    const res = await fetchCloseChecklist(token, pid);
    if (res.ok) { setChecklist(res.data); setClosed(null); }
  }, [token]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const p = await fetchFiscalPeriods(token);
    if (!p.ok) { setPhase("error"); return; }
    setPeriods(p.data);
    setPhase("success");
    const last = p.data[p.data.length - 1];
    if (last) void loadChecklist(last.id);
  }, [token, loadChecklist]);
  useEffect(() => { void load(); }, [load]);

  const loadMappings = useCallback(async () => {
    if (!token) return;
    const res = await fetchPayrollAccountMappings(token);
    if (res.ok) setMappings(res.data);
  }, [token]);
  useEffect(() => { void loadMappings(); }, [loadMappings]);

  async function run(fn: () => Promise<{ ok: boolean; code?: string; message?: string }>, label: string) {
    setBusy(true); setFormError(null);
    const res = await fn();
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code ?? "ERROR", res.message ?? "")); return false; }
    push("success", label);
    return true;
  }

  async function submitMapping(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const ok = await run(() => createPayrollAccountMapping(token, {
      element: mappingForm.element.trim(), accountCode: mappingForm.accountCode.trim(), effectiveFrom: mappingForm.effectiveFrom,
    }), mappingForm.element.trim());
    if (ok) { setMappingForm({ element: "", accountCode: "", effectiveFrom: mappingForm.effectiveFrom }); void loadMappings(); }
  }

  async function confirmClose() {
    if (!token || !periodId) return;
    setBusy(true); setFormError(null);
    const res = await closePayPeriod(token, periodId, {
      acknowledgeLater: acknowledgeLater ? true : null,
      waivers: waivers.filter((w) => w.code.trim() !== "" && w.reason.trim() !== ""),
    });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", periodId);
    setConfirmingClose(false);
    setWaivers([]);
    setClosed(res.data);
    void loadChecklist(periodId);
  }

  async function confirmReopen() {
    if (!token || !closed || !periodId) return;
    const ok = await run(() => reopenPayPeriod(token, periodId, reopenReason.trim()), periodId);
    if (ok) {
      setReopenOpen(false);
      setReopenReason("");
      setClosed(null);
      void loadChecklist(periodId);
    }
  }

  const itemColumns: Column<CloseChecklist["items"][number]>[] = useMemo(() => [
    { key: "code", label: tc.itemColumns.code, mono: true, render: (x) => x.code },
    { key: "required", label: tc.itemColumns.required, mono: true, render: (x) => (x.required ? "✓" : "—") },
    { key: "status", label: tc.itemColumns.status, render: (x) => <Badge tone={closeTone(x.status)}>{x.status}</Badge> },
    { key: "message", label: tc.itemColumns.message, render: (x) => x.message },
  ], [tc]);

  const mappingColumns: Column<PayrollMappingRow>[] = useMemo(() => [
    { key: "element", label: tc.mapColumns.element, mono: true, render: (m) => m.element },
    { key: "account", label: tc.mapColumns.account, mono: true, render: (m) => m.accountCode },
    { key: "from", label: tc.mapColumns.from, mono: true, render: (m) => m.effectiveFrom },
    { key: "to", label: tc.mapColumns.to, mono: true, render: (m) => m.effectiveTo ?? "—" },
    { key: "active", label: tc.mapColumns.active, render: (m) => (m.active ? "✓" : "—") },
  ], [tc]);

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
        <h2 className="t-title">{tc.checklistTitle}</h2>
        <div className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
          <div>
            <label htmlFor="pc-period" className="t-caption">{tc.periodId}</label>
            <select id="pc-period" className="input mono" value={periodId} onChange={(e) => { setPeriodId(e.target.value); void loadChecklist(e.target.value); }}>
              {(periods ?? []).map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </div>
        </div>
        {checklist === null ? <p className="t-caption">{tc.empty}</p> : (
          <>
            <p className="banner" aria-live="polite">{checklist.canClose ? tc.canClose : tc.blockedNote}</p>
            <Table columns={itemColumns} rows={checklist.items} caption={tc.checklistTitle} />
          </>
        )}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.closeTitle}</h2>
        <p className="t-caption">{tc.waiverHint}</p>
        {canClose === true ? (
          <>
            {waivers.map((w, i) => (
              <div key={i} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
                <Input id={"pc-w-code-" + i} label={tc.waiverCode} value={w.code} onChange={(e) => setWaivers(waivers.map((x, j) => j === i ? { ...x, code: e.target.value } : x))} />
                <Input id={"pc-w-reason-" + i} label={tc.waiverReason} value={w.reason} onChange={(e) => setWaivers(waivers.map((x, j) => j === i ? { ...x, reason: e.target.value } : x))} />
                <Button variant="secondary" onClick={() => setWaivers(waivers.filter((_, j) => j !== i))}>{t.common.cancel}</Button>
              </div>
            ))}
            <div className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
              <Button variant="secondary" onClick={() => setWaivers([...waivers, { code: "", reason: "" }])}>{tc.addWaiver}</Button>
              <label className="t-caption inline-flex" style={{ gap: "0.25rem" }}>
                <input type="checkbox" checked={acknowledgeLater} onChange={(e) => setAcknowledgeLater(e.target.checked)} /> {tc.acknowledgeLater}
              </label>
            </div>
            <p><Button onClick={() => setConfirmingClose(true)} loading={busy} disabled={!periodId || !meReady}>{tc.close}</Button></p>
          </>
        ) : <p className="t-caption">{t.people.errors.forbidden}</p>}
      </section>

      {closed ? (
        <section className="card">
          <h2 className="t-title">{tc.summaryTitle}</h2>
          <p className="t-caption mono">{closed.id} · {closed.status} · {closed.closedBy}</p>
          {closed.summaryHash ? <p className="t-caption mono">{closed.summaryHash}</p> : null}
          {closed.summary ? (
            <div className="form-grid">
              <p className="t-caption"><span className="t-body-strong">{tc.runIds}: </span><span className="mono">{closed.summary.runIds.join(", ")}</span></p>
              {closed.summary.totals ? <p className="t-caption"><span className="t-body-strong">{tc.totals}: </span><span className="mono">{JSON.stringify(closed.summary.totals)}</span></p> : null}
              {closed.summary.waivers && closed.summary.waivers.length > 0 ? (
                <p className="t-caption"><span className="t-body-strong">{tc.waiverList}: </span>{closed.summary.waivers.map((w) => w.code + " (" + w.approvedBy + ")").join(", ")}</p>
              ) : null}
            </div>
          ) : null}
          <p><Button variant="secondary" onClick={() => setReopenOpen(true)}>{tc.reopen}</Button></p>
        </section>
      ) : null}

      <section className="card">
        <h2 className="t-title">{tc.mappingsTitle}</h2>
        <p className="t-caption">{tc.mappingHint}</p>
        {canMap === true ? (
          <form onSubmit={(e) => void submitMapping(e)}>
            <div className="form-grid">
              <Input id="pc-m-element" label={tc.mapColumns.element} value={mappingForm.element} onChange={(e) => setMappingForm({ ...mappingForm, element: e.target.value })} />
              <Input id="pc-m-account" label={tc.mapColumns.account} value={mappingForm.accountCode} onChange={(e) => setMappingForm({ ...mappingForm, accountCode: e.target.value })} />
              <Input id="pc-m-from" label={tc.mapColumns.from} type="date" value={mappingForm.effectiveFrom} onChange={(e) => setMappingForm({ ...mappingForm, effectiveFrom: e.target.value })} />
            </div>
            <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createMapping}</Button></p>
          </form>
        ) : null}
        {(mappings ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={mappingColumns} rows={mappings ?? []} caption={tc.mappingsTitle} />}
      </section>

      <Modal open={confirmingClose} onClose={() => setConfirmingClose(false)} title={tc.closeTitle + " — " + periodId} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-body-strong">{tc.canClose}</p>
        <p className="t-caption">{tc.closeModalNote}</p>
        <p><Button onClick={() => void confirmClose()} loading={busy}>{tc.close}</Button></p>
      </Modal>

      <Modal open={reopenOpen} onClose={() => setReopenOpen(false)} title={tc.reopenTitle} closeLabel={t.common.cancel} busy={busy}>
        <Input id="pc-reopen-reason" label={tc.reopenReason} value={reopenReason} onChange={(e) => setReopenReason(e.target.value)} />
        <p><Button onClick={() => void confirmReopen()} loading={busy}>{tc.reopen}</Button></p>
      </Modal>
    </div>
  );
}
