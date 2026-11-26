"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  fetchGenealogy, fetchReworkOrders, createReworkOrder, approveReworkOrder,
  reinspectReworkOrder, completeReworkOrder, fetchCloseReconciliation,
  disposeVariance, decideVariance,
  type GenealogyRow, type ReworkRow, type CloseReconciliationRow,
} from "@/lib/services/mfg-deferred";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge, Button, EmptyState, Input, LoadingSkeleton, Modal, Table, Tabs,
  type TabItem, type Column,
} from "@/components/uikit";
import { hasPermission, deferredActionError, mfgStatusLabel, mfgTone, useMfgMe } from "../_lib/helpers";
import { PageShell, useToasts } from "../_lib/screen";

export default function GenealogyPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.mfg.genealogy;
  const { token, ready } = useAuth(locale);
  const { permissions, meId, meReady } = useMfgMe(token);
  const canRework = hasPermission(permissions, "mfg:rework");
  const canWrite = hasPermission(permissions, "mfg:write");
  const { toasts, push, dismiss } = useToasts();

  const [tab, setTab] = useState("trace");
  const [phase, setPhase] = useState<"loading" | "error" | "success">("success");
  const [errorCode] = useState<string | null>(null);
  const [banner, setBanner] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // trace
  const [traceForm, setTraceForm] = useState({ lot: "", sku: "" });
  const [traceErrors, setTraceErrors] = useState<{ q?: string }>({});
  const [trace, setTrace] = useState<GenealogyRow | null>(null);

  // rework
  const [rework, setRework] = useState<ReworkRow[] | null>(null);
  const [reworkForm, setReworkForm] = useState({ sourceOrderId: "", itemSku: "", qty: "", nonconformanceId: "", holdId: "", adjustmentReason: "" });
  const [reworkErrors, setReworkErrors] = useState<{ order?: string; item?: string; qty?: string }>({});
  const [reinspectTarget, setReinspectTarget] = useState<{ row: ReworkRow; inspectionId: string }>({ row: { id: "", sourceOrderId: "", itemSku: "", qty: 0, status: "", reinspectionInspectionId: null, adjustmentReason: null }, inspectionId: "" });
  const [approveReworkId, setApproveReworkId] = useState<string>("");
  const [completeReworkId, setCompleteReworkId] = useState<string>("");

  // reconciliation
  const [reconOrder, setReconOrder] = useState("");
  const [recon, setRecon] = useState<CloseReconciliationRow | null>(null);
  const [varianceForm, setVarianceForm] = useState({ bucketCode: "", qtyVariance: "", isCritical: false, owner: "" });
  const [varianceErrors, setVarianceErrors] = useState<{ bucket?: string; qty?: string }>({});
  const [decideTarget, setDecideTarget] = useState<{ varianceId: string; disposition: string; reason: string } | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    const res = await fetchReworkOrders(token);
    setRework(res.ok ? res.data : []);
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function submitTrace(e: FormEvent) {
    e.preventDefault();
    if (!traceForm.lot.trim() && !traceForm.sku.trim()) { setTraceErrors({ q: tc.errors.traceRequired }); return; }
    setTraceErrors({});
    if (!token) return;
    setBusy(true);
    setBanner(null);
    const res = await fetchGenealogy(token, { lot: traceForm.lot.trim() || undefined, sku: traceForm.sku.trim() || undefined });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    setTrace(res.data);
  }

  async function submitRework(e: FormEvent) {
    e.preventDefault();
    const qty = Number(reworkForm.qty);
    const errs: { order?: string; item?: string; qty?: string } = {};
    if (!reworkForm.sourceOrderId.trim()) errs.order = tc.errors.orderRequired;
    if (!reworkForm.itemSku.trim()) errs.item = tc.errors.itemRequired;
    if (!reworkForm.qty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    setReworkErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await createReworkOrder(token, {
      sourceOrderId: reworkForm.sourceOrderId.trim(), itemSku: reworkForm.itemSku.trim(), qty,
      nonconformanceId: reworkForm.nonconformanceId.trim() || null,
      holdId: reworkForm.holdId.trim() || null,
      adjustmentReason: reworkForm.adjustmentReason.trim() || null,
    });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.reworkCreatedNote + " " + res.data.id);
    setReworkForm({ sourceOrderId: "", itemSku: "", qty: "", nonconformanceId: "", holdId: "", adjustmentReason: "" });
    await load();
  }

  async function confirmApproveRework() {
    if (!approveReworkId || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await approveReworkOrder(token, approveReworkId);
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.reworkApprovedNote);
    setApproveReworkId("");
    await load();
  }

  async function confirmReinspect(e: FormEvent) {
    e.preventDefault();
    if (!reinspectTarget.row.id || !token) return;
    if (!reinspectTarget.inspectionId.trim()) { setBanner(tc.errors.itemRequired); return; }
    setBusy(true);
    setBanner(null);
    const res = await reinspectReworkOrder(token, reinspectTarget.row.id, reinspectTarget.inspectionId.trim());
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.reworkCompletedNote);
    setReinspectTarget({ row: { id: "", sourceOrderId: "", itemSku: "", qty: 0, status: "", reinspectionInspectionId: null, adjustmentReason: null }, inspectionId: "" });
    await load();
  }

  async function confirmCompleteRework() {
    if (!completeReworkId || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await completeReworkOrder(token, completeReworkId);
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.reworkCompletedNote);
    setCompleteReworkId("");
    await load();
  }

  async function loadRecon() {
    if (!reconOrder.trim() || !token) { setBanner(tc.errors.orderRequired); return; }
    setBusy(true);
    setBanner(null);
    const res = await fetchCloseReconciliation(token, reconOrder.trim());
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    setRecon(res.data);
  }

  async function submitVariance(e: FormEvent) {
    e.preventDefault();
    const qty = Number(varianceForm.qtyVariance);
    const errs: { bucket?: string; qty?: string } = {};
    if (!varianceForm.bucketCode.trim()) errs.bucket = tc.errors.bucketRequired;
    if (!varianceForm.qtyVariance.trim() || !Number.isFinite(qty) || qty === 0) errs.qty = tc.errors.qtyInvalid;
    setVarianceErrors(errs);
    if (Object.keys(errs).length > 0 || !token || !reconOrder.trim()) { setBanner(tc.errors.orderRequired); return; }
    setBusy(true);
    setBanner(null);
    const res = await disposeVariance(token, reconOrder.trim(), {
      bucketCode: varianceForm.bucketCode.trim(), qtyVariance: qty,
      isCritical: varianceForm.isCritical, owner: varianceForm.owner.trim() || null,
    });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.varianceRecordedNote);
    setVarianceForm({ bucketCode: "", qtyVariance: "", isCritical: false, owner: "" });
    await loadRecon();
  }

  async function submitDecide(e: FormEvent) {
    e.preventDefault();
    if (!decideTarget || !token) return;
    if (!decideTarget.reason.trim()) { setBanner(tc.errors.reasonRequired); return; }
    setBusy(true);
    setBanner(null);
    const res = await decideVariance(token, decideTarget.varianceId, { disposition: decideTarget.disposition, reason: decideTarget.reason.trim() });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.decidedNote);
    setDecideTarget(null);
    await loadRecon();
  }

  const reworkColumns: Column<ReworkRow>[] = [
    { key: "id", label: tc.reworkTitle, mono: true, render: (r) => r.id },
    { key: "src", label: tc.colSourceOrder, mono: true, render: (r) => r.sourceOrderId },
    { key: "item", label: tc.reworkItem, mono: true, render: (r) => r.itemSku },
    { key: "qty", label: tc.reworkQty, mono: true, render: (r) => fmtNumber(r.qty, locale) },
    { key: "reinsp", label: tc.completeRework, render: (r) => (r.reinspectionInspectionId ? <Badge tone="success">{r.reinspectionInspectionId}</Badge> : <Badge tone="warning">{tc.completeNo}</Badge>) },
    { key: "status", label: tc.colDisposition, render: (r) => <Badge tone={mfgTone(r.status)}>{mfgStatusLabel(t, r.status)}</Badge> },
    { key: "act", label: tc.approveRework, render: (r) => (
      <div className="row-actions">
        {r.status === "Draft" && canRework !== false && meReady ? <Button small onClick={() => setApproveReworkId(r.id)}>{tc.approveRework}</Button> : null}
        {r.status === "Approved" && canRework !== false && meReady ? <Button small variant="secondary" onClick={() => setReinspectTarget({ row: r, inspectionId: r.reinspectionInspectionId ?? "" })}>{tc.completeRework}</Button> : null}
        {r.status === "Approved" && r.reinspectionInspectionId && canWrite !== false && meReady ? <Button small onClick={() => setCompleteReworkId(r.id)}>{tc.completeRework}</Button> : null}
      </div>
    ) },
  ];

  const varianceColumns: Column<CloseReconciliationRow["variances"][number]>[] = [
    { key: "bucket", label: tc.colBucket, mono: true, render: (v) => v.bucketCode },
    { key: "qty", label: tc.colQty, mono: true, render: (v) => fmtNumber(v.qtyVariance, locale) },
    { key: "owner", label: tc.colOwner, mono: true, render: (v) => v.owner || "—" },
    { key: "age", label: tc.colAge, mono: true, render: (v) => v.ageDays },
    { key: "crit", label: tc.colCritical, render: (v) => (v.isCritical ? <Badge tone="danger">{tc.criticalLabel}</Badge> : "—") },
    { key: "disp", label: tc.colDisposition, render: (v) => v.disposition },
    { key: "act", label: tc.decideVariance, render: (v) => (
      canWrite !== false && meReady ? (
        <div className="row-actions">
          <Button small variant="secondary" onClick={() => setDecideTarget({ varianceId: v.id, disposition: "accepted", reason: "" })}>{tc.decisionAccepted}</Button>
          <Button small variant="secondary" onClick={() => setDecideTarget({ varianceId: v.id, disposition: "overridden", reason: "" })}>{tc.decisionOverridden}</Button>
        </div>
      ) : null
    ) },
  ];

  const buckets = (row: CloseReconciliationRow): { label: string; value: number }[] => [
    { label: tc.planned, value: row.buckets.planned },
    { label: tc.issued, value: row.buckets.issued },
    { label: tc.returned, value: row.buckets.returned },
    { label: tc.consumed, value: row.buckets.consumed },
    { label: tc.completed, value: row.buckets.completed },
    { label: tc.scrapped, value: row.buckets.scrapped },
    { label: tc.reworked, value: row.buckets.reworked },
    { label: tc.heldQty, value: row.buckets.heldQty },
    { label: tc.wipRemainder, value: row.buckets.wipRemainder },
  ];

  const tabs: TabItem[] = [
    { id: "trace", label: tc.traceTitle, content: (
      <div className="stack">
        <section className="card">
          <h2 className="t-title">{tc.traceTitle}</h2>
          <form onSubmit={(e) => void submitTrace(e)}>
            <div className="form-grid">
              <Input id="g-lot" label={tc.lotLabel} value={traceForm.lot} onChange={(e) => setTraceForm({ ...traceForm, lot: e.target.value })} />
              <Input id="g-sku" label={tc.skuLabel} value={traceForm.sku} onChange={(e) => setTraceForm({ ...traceForm, sku: e.target.value })} />
            </div>
            {traceErrors.q ? <p className="t-caption" role="alert">{traceErrors.q}</p> : null}
            <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.traceAction}</Button></p>
          </form>
          {trace === null ? null : (
            <div className="stack">
              <p className="t-caption">{tc.completeLabel}: {trace.complete ? <Badge tone="success">{tc.completeYes}</Badge> : <Badge tone="warning">{tc.completeNo}</Badge>}</p>
              {trace.events.length === 0 ? <EmptyState title={tc.emptyTrace} /> : (
                <Table
                  columns={[
                    { key: "event", label: tc.colEvent, mono: true, render: (ev) => ev.id },
                    { key: "order", label: tc.colOrder, mono: true, render: (ev) => ev.orderId },
                    { key: "out", label: tc.colOutput, mono: true, render: (ev) => ev.outputSku },
                    { key: "qty", label: tc.colQty, mono: true, render: (ev) => fmtNumber(ev.outputQty, locale) },
                    { key: "lot", label: tc.colLot, mono: true, render: (ev) => ev.lotSerial ?? "—" },
                    { key: "inputs", label: tc.inputsLabel, render: (ev) => ev.inputs.map((i) => i.itemSku + " × " + fmtNumber(i.qty, locale)).join(", ") || "—" },
                  ]}
                  rows={trace.events}
                  caption={tc.eventsTitle}
                />
              )}
              <h3 className="t-body-strong">{tc.gapsTitle}</h3>
              {trace.gaps.length === 0 ? <p className="t-caption">{tc.noGaps}</p> : (
                <ul>
                  {trace.gaps.map((gap, i) => (
                    <li key={i} className="t-caption"><Badge tone="warning">{gap.itemSku}</Badge> {gap.reason}</li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </section>
      </div>
    ) },
    { id: "rework", label: tc.reworkTitle, content: (
      <section className="card">
        <h2 className="t-title">{tc.reworkTitle}</h2>
        <form onSubmit={(e) => void submitRework(e)}>
          <div className="form-grid">
            <Input id="g-rorder" label={tc.reworkOrderLabel} value={reworkForm.sourceOrderId} onChange={(e) => setReworkForm({ ...reworkForm, sourceOrderId: e.target.value })} error={reworkErrors.order} />
            <Input id="g-ritem" label={tc.reworkItem} value={reworkForm.itemSku} onChange={(e) => setReworkForm({ ...reworkForm, itemSku: e.target.value })} error={reworkErrors.item} />
            <Input id="g-rqty" label={tc.reworkQty} type="number" min="0" step="any" value={reworkForm.qty} onChange={(e) => setReworkForm({ ...reworkForm, qty: e.target.value })} error={reworkErrors.qty} />
            <Input id="g-rnc" label={tc.ncLabel} value={reworkForm.nonconformanceId} onChange={(e) => setReworkForm({ ...reworkForm, nonconformanceId: e.target.value })} />
            <Input id="g-radj" label={tc.decisionReason} value={reworkForm.adjustmentReason} onChange={(e) => setReworkForm({ ...reworkForm, adjustmentReason: e.target.value })} />
          </div>
          <p><Button type="submit" small loading={busy} disabled={!meReady || canRework === false}>{tc.createRework}</Button></p>
        </form>
        {rework === null ? null : rework.length === 0 ? <EmptyState title={tc.emptyRework} /> : <Table columns={reworkColumns} rows={rework} caption={tc.reworkTitle} />}
      </section>
    ) },
    { id: "recon", label: tc.reconTitle, content: (
      <div className="stack">
        <section className="card">
          <h2 className="t-title">{tc.reconTitle}</h2>
          <div className="form-grid">
            <Input id="g-order" label={tc.orderIdLabel} value={reconOrder} onChange={(e) => setReconOrder(e.target.value)} />
          </div>
          <p><Button variant="secondary" small loading={busy} onClick={() => void loadRecon()} disabled={!meReady}>{tc.loadRecon}</Button></p>
          {recon === null ? null : (
            <div className="stack">
              <p className="t-caption">{tc.closePolicyLabel}: <span className="mono">{recon.closePolicy}</span></p>
              <h3 className="t-body-strong">{tc.bucketsTitle}</h3>
              <section className="tiles">
                {buckets(recon).map((tile) => (
                  <div className="kpi" key={tile.label}>
                    <p className="kpi__label">{tile.label}</p>
                    <p className="kpi__value">{fmtNumber(tile.value, locale)}</p>
                  </div>
                ))}
              </section>
              <h3 className="t-body-strong">{tc.variancesTitle}</h3>
              {recon.variances.length === 0 ? <p className="t-caption">{tc.noVariances}</p> : (
                <Table columns={varianceColumns} rows={recon.variances} caption={tc.variancesTitle} />
              )}
              <form onSubmit={(e) => void submitVariance(e)}>
                <h3 className="t-body-strong">{tc.recordVariance}</h3>
                <div className="form-grid">
                  <Input id="g-vbucket" label={tc.varianceBucket} value={varianceForm.bucketCode} onChange={(e) => setVarianceForm({ ...varianceForm, bucketCode: e.target.value })} error={varianceErrors.bucket} />
                  <Input id="g-vqty" label={tc.varianceQty} type="number" step="any" value={varianceForm.qtyVariance} onChange={(e) => setVarianceForm({ ...varianceForm, qtyVariance: e.target.value })} error={varianceErrors.qty} />
                  <Input id="g-vowner" label={tc.colOwner} value={varianceForm.owner} onChange={(e) => setVarianceForm({ ...varianceForm, owner: e.target.value })} />
                </div>
                <p className="t-caption">
                  <label htmlFor="g-vcrit"><input id="g-vcrit" type="checkbox" checked={varianceForm.isCritical} onChange={(e) => setVarianceForm({ ...varianceForm, isCritical: e.target.checked })} /> {tc.criticalLabel}</label>
                </p>
                <p><Button type="submit" small loading={busy} disabled={!meReady || canWrite === false}>{tc.recordVariance}</Button></p>
              </form>
            </div>
          )}
        </section>
      </div>
    ) },
  ];

  if (!ready || phase === "loading") return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    return (
      <PageShell
        title={tc.title} subtitle={tc.subtitle} loading={false}
        error errorCode={errorCode} onRetry={() => setPhase("success")}
        forbidden={errorCode === "FORBIDDEN" || errorCode === "HTTP_403" ? tc.forbidden : tc.loadFailed}
        toasts={toasts} onDismiss={dismiss} banner={null}
        loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
      />
    );
  }
  return (
    <PageShell
      title={tc.title} subtitle={tc.subtitle} loading={false} error={false} errorCode={null}
      onRetry={() => setPhase("success")} forbidden={tc.forbidden}
      toasts={toasts} onDismiss={dismiss} banner={banner}
      loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
    >
      <Tabs tabs={tabs} activeId={tab} onChange={setTab} label={tc.title} />
      <Modal open={approveReworkId !== ""} onClose={() => setApproveReworkId("")} title={tc.approveRework} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-caption mono">{approveReworkId}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setApproveReworkId("")}>{t.common.cancel}</Button>
          <Button loading={busy} onClick={() => void confirmApproveRework()}>{tc.approveRework}</Button>
        </div>
      </Modal>
      <Modal open={completeReworkId !== ""} onClose={() => setCompleteReworkId("")} title={tc.completeRework} closeLabel={t.common.cancel} busy={busy}>
        <p>{tc.reworkCompletedNote}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setCompleteReworkId("")}>{t.common.cancel}</Button>
          <Button loading={busy} onClick={() => void confirmCompleteRework()}>{tc.completeRework}</Button>
        </div>
      </Modal>
      <Modal open={reinspectTarget.row.id !== ""} onClose={() => setReinspectTarget({ row: { id: "", sourceOrderId: "", itemSku: "", qty: 0, status: "", reinspectionInspectionId: null, adjustmentReason: null }, inspectionId: "" })} title={tc.completeRework} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void confirmReinspect(e)}>
          <p className="t-caption mono">{reinspectTarget.row.id}</p>
          <Input id="g-reinsp" label={tc.completeRework} value={reinspectTarget.inspectionId} onChange={(e) => setReinspectTarget({ ...reinspectTarget, inspectionId: e.target.value })} />
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setReinspectTarget({ row: { id: "", sourceOrderId: "", itemSku: "", qty: 0, status: "", reinspectionInspectionId: null, adjustmentReason: null }, inspectionId: "" })}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.completeRework}</Button>
          </div>
        </form>
      </Modal>
      <Modal open={decideTarget !== null} onClose={() => setDecideTarget(null)} title={tc.decideVariance} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void submitDecide(e)}>
          <Input id="g-dec-reason" label={tc.decisionReason} value={decideTarget?.reason ?? ""} onChange={(e) => setDecideTarget((prev) => (prev ? { ...prev, reason: e.target.value } : prev))} />
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setDecideTarget(null)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.decideVariance}</Button>
          </div>
        </form>
      </Modal>
    </PageShell>
  );
}
