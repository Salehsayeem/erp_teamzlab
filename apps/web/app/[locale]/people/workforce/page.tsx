"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  approveAllocationSet, createAllocationSet, createWorkforcePlan, fetchAllocationSets, fetchAnalytics,
  fetchWorkforcePlans, promoteWorkforcePlan, reconcileAllocationSet,
  type AllocationSetRow, type AnalyticsBucket, type WorkforcePlanRow,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Table, ToastRegion, type ToastItem } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../_lib/helpers";

type PlanLine = { positionCode: string; title: string; fte: string };
type SetLine = { costCenter: string; pct: string };

export default function WorkforcePage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.workforce;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canWrite = hasPermission(permissions, "workforce:write");
  const canApprove = hasPermission(permissions, "payroll:approve");
  const canAnalytics = hasPermission(permissions, "people:analytics:read");

  const [tab, setTab] = useState<"plans" | "allocation" | "analytics">("plans");
  const [plans, setPlans] = useState<WorkforcePlanRow[] | null>(null);
  const [sets, setSets] = useState<AllocationSetRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [planForm, setPlanForm] = useState({ code: "", name: "", horizonStart: "", horizonEnd: "", assumptions: "" });
  const [planLines, setPlanLines] = useState<PlanLine[]>([{ positionCode: "", title: "", fte: "1" }]);
  const [setForm, setSetForm] = useState({ code: "", name: "", unit: "Pct" });
  const [setLines, setSetLines] = useState<SetLine[]>([{ costCenter: "", pct: "" }]);
  const [promoting, setPromoting] = useState<WorkforcePlanRow | null>(null);
  const [approving, setApproving] = useState<AllocationSetRow | null>(null);
  const [reconRunId, setReconRunId] = useState("");
  const [reconSetId, setReconSetId] = useState<string | null>(null);
  const [recon, setRecon] = useState<{ reconciles: boolean; buckets: { costCenter: string; allocated: number; actual: number; variance: number }[] } | null>(null);
  const [metric, setMetric] = useState("headcount");
  const [buckets, setBuckets] = useState<{ metric: string; threshold: number; buckets: AnalyticsBucket[]; total: { value: number | null; suppressed: boolean } } | null>(null);

  const push = (tone: ToastItem["tone"], message: string) => setToasts((p) => [...p, { id: crypto.randomUUID(), tone, message }]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [p, s] = await Promise.all([fetchWorkforcePlans(token), fetchAllocationSets(token)]);
    if (p.ok && s.ok) { setPlans(p.data); setSets(s.data); setPhase("success"); } else setPhase("error");
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

  async function submitPlan() {
    if (!token) return;
    const ok = await run(() => createWorkforcePlan(token, {
      code: planForm.code.trim(), name: planForm.name.trim(),
      horizonStart: planForm.horizonStart, horizonEnd: planForm.horizonEnd,
      lines: planLines.filter((l) => l.positionCode.trim()).map((l) => ({ positionCode: l.positionCode.trim(), title: l.title.trim(), fte: Number(l.fte) || 0 })),
    }), planForm.code.trim());
    if (ok) { setPlanForm({ code: "", name: "", horizonStart: "", horizonEnd: "", assumptions: "" }); setPlanLines([{ positionCode: "", title: "", fte: "1" }]); }
  }

  async function submitSet() {
    if (!token) return;
    const ok = await run(() => createAllocationSet(token, {
      code: setForm.code.trim(), name: setForm.name.trim(), unit: setForm.unit.trim(),
      lines: setLines.filter((l) => l.costCenter.trim()).map((l) => ({ costCenter: l.costCenter.trim(), pct: Number(l.pct) || 0 })),
    }), setForm.code.trim());
    if (ok) { setSetForm({ code: "", name: "", unit: "Pct" }); setSetLines([{ costCenter: "", pct: "" }]); }
  }

  async function confirmPromote() {
    if (!token || !promoting) return;
    const ok = await run(() => promoteWorkforcePlan(token, promoting.id), promoting.code);
    if (ok) setPromoting(null);
  }

  async function confirmApprove() {
    if (!token || !approving) return;
    const ok = await run(() => approveAllocationSet(token, approving.id), approving.code);
    if (ok) setApproving(null);
  }

  async function submitRecon(e: React.FormEvent) {
    e.preventDefault();
    if (!token || !reconSetId) return;
    setBusy(true); setFormError(null);
    const res = await reconcileAllocationSet(token, reconSetId, reconRunId.trim());
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    setRecon(res.data);
  }

  async function showAnalytics() {
    if (!token) return;
    setBusy(true); setFormError(null);
    const res = await fetchAnalytics(token, metric.trim());
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    setBuckets(res.data);
  }

  function exportCsv() {
    if (!buckets) return;
    const head = "bucket,count,value";
    const rows = buckets.buckets.map((b) => [
      b.key, b.count, b.suppressed ? "" : String(b.value ?? ""),
    ].join(","));
    const total = "total," + String(buckets.buckets.reduce((s, b) => s + b.count, 0)) + "," + (buckets.total.suppressed ? "" : String(buckets.total.value ?? ""));
    const blob = new Blob([[head].concat(rows, [total]).join("\n")], { type: "text/csv" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url; a.download = "people-analytics-" + metric + ".csv";
    a.click();
    URL.revokeObjectURL(url);
  }

  const planColumns: Column<WorkforcePlanRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.plan, mono: true, render: (p) => p.code + " — " + p.name },
    { key: "horizon", label: tc.columns.horizon, mono: true, render: (p) => p.horizonStart + " → " + p.horizonEnd },
    { key: "status", label: tc.columns.status, render: (p) => <Badge tone={peopleTone(p.status)}>{peopleStatusLabel(t, p.status)}</Badge> },
    { key: "lines", label: tc.addLine, mono: true, render: (p) => p.lines.map((l) => l.positionCode + "×" + l.fte).join(", ") },
    { key: "actions", label: "", render: (p) => p.status !== "Promoted" && canWrite === true ? (
        <Button variant="secondary" onClick={() => setPromoting(p)}>{tc.promote}</Button>
      ) : null },
  ], [tc, t, canWrite]);

  const setColumns: Column<AllocationSetRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.plan, mono: true, render: (s) => s.code + " — " + s.name },
    { key: "unit", label: tc.unit, mono: true, render: (s) => s.unit },
    { key: "lines", label: tc.linePosition, mono: true, render: (s) => s.lines.map((l) => l.costCenter + " " + l.pct + (l.heldForReview ? " (HFR)" : "")).join(", ") },
    { key: "status", label: tc.columns.status, render: (s) => <Badge tone={peopleTone(s.status)}>{peopleStatusLabel(t, s.status)}</Badge> },
    { key: "actions", label: "", render: (s) => s.status === "Draft" && canApprove === true ? (
        <span className="inline-flex" style={{ gap: "0.25rem" }}>
          <Button variant="secondary" onClick={() => { setReconSetId(s.id); setRecon(null); }}>{tc.reconcile}</Button>
          <Button variant="secondary" onClick={() => setApproving(s)}>{tc.approveSet}</Button>
        </span>
      ) : null },
  ], [tc, t, canApprove]);

  const bucketColumns: Column<AnalyticsBucket>[] = useMemo(() => [
    { key: "bucket", label: tc.analyticsColumns.bucket, mono: true, render: (b) => b.key },
    { key: "count", label: tc.analyticsColumns.count, mono: true, render: (b) => String(b.count) },
    { key: "value", label: tc.analyticsColumns.value, mono: true, render: (b) => b.suppressed ? "—" : String(b.value ?? "—") },
    { key: "suppressed", label: tc.analyticsColumns.suppressed, render: (b) => (b.suppressed ? <Badge tone="warning">{tc.analyticsColumns.suppressed}</Badge> : "—") },
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

      <nav className="inline-flex" style={{ gap: "0.5rem" }} aria-label={tc.title}>
        {(["plans", "allocation", "analytics"] as const).map((k) => (
          <Button key={k} variant={tab === k ? "primary" : "secondary"} onClick={() => setTab(k)}>
            {k === "plans" ? tc.tabPlans : k === "allocation" ? tc.tabAllocation : tc.tabAnalytics}
          </Button>
        ))}
      </nav>

      {tab === "plans" ? (
        <>
          <section className="card">
            <h2 className="t-title">{tc.planFormTitle}</h2>
            {canWrite === true ? (
              <form onSubmit={(e) => { e.preventDefault(); void submitPlan(); }}>
                <div className="form-grid">
                  <Input id="wf-p-code" label={tc.planCode} value={planForm.code} onChange={(e) => setPlanForm({ ...planForm, code: e.target.value })} />
                  <Input id="wf-p-name" label={tc.planName} value={planForm.name} onChange={(e) => setPlanForm({ ...planForm, name: e.target.value })} />
                  <Input id="wf-p-from" label={tc.horizonStart} type="date" value={planForm.horizonStart} onChange={(e) => setPlanForm({ ...planForm, horizonStart: e.target.value })} />
                  <Input id="wf-p-to" label={tc.horizonEnd} type="date" value={planForm.horizonEnd} onChange={(e) => setPlanForm({ ...planForm, horizonEnd: e.target.value })} />
                </div>
                {planLines.map((l, i) => (
                  <div key={i} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
                    <Input id={"wf-pl-pos-" + i} label={tc.linePosition} value={l.positionCode} onChange={(e) => setPlanLines(planLines.map((x, j) => j === i ? { ...x, positionCode: e.target.value } : x))} />
                    <Input id={"wf-pl-title-" + i} label={tc.lineTitle} value={l.title} onChange={(e) => setPlanLines(planLines.map((x, j) => j === i ? { ...x, title: e.target.value } : x))} />
                    <Input id={"wf-pl-fte-" + i} label={tc.lineFte} type="number" step="0.5" value={l.fte} onChange={(e) => setPlanLines(planLines.map((x, j) => j === i ? { ...x, fte: e.target.value } : x))} />
                    <Button variant="secondary" onClick={() => setPlanLines(planLines.filter((_, j) => j !== i))}>{tc.removeLine}</Button>
                  </div>
                ))}
                <div className="inline-flex" style={{ gap: "0.5rem" }}>
                  <Button type="button" variant="secondary" onClick={() => setPlanLines([...planLines, { positionCode: "", title: "", fte: "1" }])}>{tc.addLine}</Button>
                  <Button type="submit" loading={busy} disabled={!meReady}>{tc.createPlan}</Button>
                </div>
              </form>
            ) : <p className="t-caption">{t.people.errors.forbidden}</p>}
          </section>
          <section className="card">
            {(plans ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={planColumns} rows={plans ?? []} caption={tc.tabPlans} />}
          </section>
        </>
      ) : null}

      {tab === "allocation" ? (
        <>
          <section className="card">
            <h2 className="t-title">{tc.allocationFormTitle}</h2>
            <p className="t-caption">{tc.allocationHint}</p>
            {canWrite === true ? (
              <form onSubmit={(e) => { e.preventDefault(); void submitSet(); }}>
                <div className="form-grid">
                  <Input id="wf-s-code" label={tc.planCode} value={setForm.code} onChange={(e) => setSetForm({ ...setForm, code: e.target.value })} />
                  <Input id="wf-s-name" label={tc.planName} value={setForm.name} onChange={(e) => setSetForm({ ...setForm, name: e.target.value })} />
                  <Input id="wf-s-unit" label={tc.unit} value={setForm.unit} onChange={(e) => setSetForm({ ...setForm, unit: e.target.value })} />
                </div>
                {setLines.map((l, i) => (
                  <div key={i} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
                    <Input id={"wf-sl-cc-" + i} label={tc.costCenter} value={l.costCenter} onChange={(e) => setSetLines(setLines.map((x, j) => j === i ? { ...x, costCenter: e.target.value } : x))} />
                    <Input id={"wf-sl-pct-" + i} label={tc.pct} type="number" step="0.01" value={l.pct} onChange={(e) => setSetLines(setLines.map((x, j) => j === i ? { ...x, pct: e.target.value } : x))} />
                    <Button variant="secondary" onClick={() => setSetLines(setLines.filter((_, j) => j !== i))}>{tc.removeLine}</Button>
                  </div>
                ))}
                <div className="inline-flex" style={{ gap: "0.5rem" }}>
                  <Button type="button" variant="secondary" onClick={() => setSetLines([...setLines, { costCenter: "", pct: "" }])}>{tc.addLine}</Button>
                  <Button type="submit" loading={busy} disabled={!meReady}>{tc.createSet}</Button>
                </div>
              </form>
            ) : <p className="t-caption">{t.people.errors.forbidden}</p>}
          </section>
          <section className="card">
            {(sets ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={setColumns} rows={sets ?? []} caption={tc.tabAllocation} />}
            {reconSetId ? (
              <form onSubmit={(e) => void submitRecon(e)} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end", marginTop: "1rem" }}>
                <Input id="wf-recon-run" label={tc.runId} value={reconRunId} onChange={(e) => setReconRunId(e.target.value)} />
                <Button type="submit" loading={busy}>{tc.reconcile}</Button>
                <Button type="button" variant="secondary" onClick={() => { setReconSetId(null); setRecon(null); }}>{t.common.cancel}</Button>
              </form>
            ) : null}
            {recon ? (
              <div style={{ marginTop: "1rem" }}>
                <p className="banner" aria-live="polite">{recon.reconciles ? tc.reconciles : tc.outOfSync}</p>
                <Table caption={tc.reconcileTitle} columns={[
                  { key: "cc", label: tc.reconColumns.center, mono: true, render: (b: { costCenter: string }) => b.costCenter },
                  { key: "alloc", label: tc.reconColumns.allocated, mono: true, render: (b: { allocated: number }) => String(b.allocated) },
                  { key: "actual", label: tc.reconColumns.actual, mono: true, render: (b: { actual: number }) => String(b.actual) },
                  { key: "var", label: tc.reconColumns.variance, mono: true, render: (b: { variance: number }) => String(b.variance) },
                ]} rows={recon.buckets} />
              </div>
            ) : null}
          </section>
        </>
      ) : null}

      {tab === "analytics" ? (
        <section className="card">
          <h2 className="t-title">{tc.analyticsTitle}</h2>
          <p className="t-caption">{tc.suppressedNote}</p>
          {canAnalytics === true ? (
            <form onSubmit={(e) => { e.preventDefault(); void showAnalytics(); }} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
              <div>
                <label htmlFor="wf-metric" className="t-caption">{tc.metricHeadcount}</label>
                <select id="wf-metric" className="input" value={metric} onChange={(e) => setMetric(e.target.value)}>
                  <option value="headcount">{tc.metricHeadcount}</option>
                  <option value="fte">{tc.metricFte}</option>
                </select>
              </div>
              <Button type="submit" loading={busy}>{tc.show}</Button>
            </form>
          ) : <p className="t-caption">{t.people.errors.forbidden}</p>}
          {buckets ? (
            <div style={{ marginTop: "1rem" }}>
              <Table columns={bucketColumns} rows={buckets.buckets} caption={tc.analyticsTitle} />
              <p><Button variant="secondary" onClick={exportCsv}>{tc.exportCsv}</Button></p>
            </div>
          ) : <EmptyState title={tc.emptyAnalytics} />}
        </section>
      ) : null}

      <Modal open={promoting !== null} onClose={() => setPromoting(null)} title={tc.promote + " — " + (promoting?.code ?? "")} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-caption">{tc.promoteHint}</p>
        <p><Button onClick={() => void confirmPromote()} loading={busy}>{tc.promote}</Button></p>
      </Modal>

      <Modal open={approving !== null} onClose={() => setApproving(null)} title={tc.approveSet + " — " + (approving?.code ?? "")} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-caption">{tc.approveHint}</p>
        <p><Button onClick={() => void confirmApprove()} loading={busy}>{tc.approveSet}</Button></p>
      </Modal>
    </div>
  );
}
