"use client";

import { useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  recordCustody, fetchCustodies, receiveSubcontract, fetchSubcontractReceipts,
  type CustodyRow, type SubcontractReceiptRow,
} from "@/lib/services/mfg-deferred";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge, Button, EmptyState, Input, LoadingSkeleton, Modal, Select, Table,
  type Column,
} from "@/components/uikit";
import { hasPermission, deferredActionError, useMfgMe } from "../_lib/helpers";
import { PageShell, useToasts } from "../_lib/screen";

export default function SubcontractPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.mfg.subcontract;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = useMfgMe(token);
  const canWrite = hasPermission(permissions, "mfg:write");
  const { toasts, push, dismiss } = useToasts();

  const [phase, setPhase] = useState<"loading" | "error" | "success">("success");
  const [errorCode] = useState<string | null>(null);
  const [banner, setBanner] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [orderId, setOrderId] = useState("");

  const [custodies, setCustodies] = useState<CustodyRow[] | null>(null);
  const [reconciliation, setReconciliation] = useState<{ sent: number; returned: number; outstanding: number } | null>(null);
  const [custodyForm, setCustodyForm] = useState({ routingOperationSeq: "", kind: "Send", qty: "", inventoryMovementId: "" });
  const [custodyErrors, setCustodyErrors] = useState<{ step?: string; qty?: string; movement?: string; order?: string }>({});
  const [receipts, setReceipts] = useState<SubcontractReceiptRow[] | null>(null);
  const [receiveForm, setReceiveForm] = useState({ routingOperationSeq: "", qty: "", note: "" });
  const [receiveErrors, setReceiveErrors] = useState<{ step?: string; qty?: string }>({});
  const [receiveTarget, setReceiveTarget] = useState<string>("");

  async function loadOrder() {
    if (!token) return;
    const id = orderId.trim();
    if (!id) { setBanner(tc.errors.orderRequired); return; }
    setBusy(true);
    const [c, r] = await Promise.all([fetchCustodies(token, id), fetchSubcontractReceipts(token, id)]);
    setBusy(false);
    setCustodies(c.ok ? c.data.rows : null);
    setReconciliation(c.ok ? c.data.reconciliation : null);
    setReceipts(r.ok ? r.data : null);
  }

  async function submitCustody(e: FormEvent) {
    e.preventDefault();
    const id = orderId.trim();
    if (!id || !token) { setCustodyErrors({ order: tc.errors.orderRequired }); return; }
    const qty = Number(custodyForm.qty);
    const step = Number(custodyForm.routingOperationSeq);
    const errs: { step?: string; qty?: string; movement?: string } = {};
    if (!custodyForm.routingOperationSeq.trim() || !Number.isInteger(step) || step < 1) errs.step = tc.errors.stepInvalid;
    if (!custodyForm.qty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    if (!custodyForm.inventoryMovementId.trim()) errs.movement = tc.errors.movementRequired;
    setCustodyErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setBanner(null);
    const res = await recordCustody(token, { orderId: id, routingOperationSeq: step, kind: custodyForm.kind, qty, inventoryMovementId: custodyForm.inventoryMovementId.trim() });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.custodyRecordedNote);
    setCustodyForm({ routingOperationSeq: custodyForm.routingOperationSeq, kind: custodyForm.kind, qty: "", inventoryMovementId: "" });
    await loadOrder();
  }

  async function confirmReceive(e: FormEvent) {
    e.preventDefault();
    const id = orderId.trim();
    if (!id || !token || receiveTarget === "") return;
    const qty = Number(receiveForm.qty);
    const step = Number(receiveForm.routingOperationSeq);
    const errs: { step?: string; qty?: string } = {};
    if (!receiveForm.routingOperationSeq.trim() || !Number.isInteger(step) || step < 1) errs.step = tc.errors.stepInvalid;
    if (!receiveForm.qty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    setReceiveErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setBanner(null);
    const res = await receiveSubcontract(token, id, { routingOperationSeq: step, qty, note: receiveForm.note.trim() || null });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.receivedNote);
    setReceiveTarget("");
    setReceiveForm({ routingOperationSeq: "", qty: "", note: "" });
    await loadOrder();
  }

  const custodyColumns: Column<CustodyRow>[] = [
    { key: "step", label: tc.colStep, mono: true, render: (c) => c.routingOperationSeq },
    { key: "kind", label: tc.colKind, render: (c) => <Badge tone={c.kind === "Send" ? "warning" : "success"}>{c.kind === "Send" ? tc.kindSend : tc.kindReturn}</Badge> },
    { key: "qty", label: tc.colQty, mono: true, render: (c) => fmtNumber(c.qty, locale) },
    { key: "movement", label: tc.colMovement, mono: true, render: (c) => c.inventoryMovementId },
  ];

  const receiptColumns: Column<SubcontractReceiptRow>[] = [
    { key: "step", label: tc.colStep, mono: true, render: (r) => r.routingOperationSeq },
    { key: "qty", label: tc.colQty, mono: true, render: (r) => fmtNumber(r.qty, locale) },
    { key: "accepted", label: tc.statusAccepted, mono: true, render: (r) => fmtNumber(r.acceptedQty, locale) },
    { key: "status", label: tc.colKind, render: (r) => (r.status === "Accepted"
      ? <Badge tone="success">{tc.statusAccepted}</Badge>
      : <Badge tone="warning">{tc.statusPendingQuality}{r.qualityHoldId ? " (" + r.qualityHoldId + ")" : ""}</Badge>) },
  ];

  const stepOptions = Array.from(new Set((custodies ?? []).map((c) => c.routingOperationSeq))).map((s) => ({ value: String(s), label: "#" + s }));

  return (
    <PageShell
      title={tc.title} subtitle={tc.subtitle} loading={false} error={phase === "error"} errorCode={errorCode}
      onRetry={() => setPhase("success")} forbidden={tc.forbidden}
      toasts={toasts} onDismiss={dismiss} banner={banner}
      loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
    >
      <section className="card">
        <div className="form-grid">
          <Input id="sub-order" label={tc.orderIdLabel} value={orderId} onChange={(e) => setOrderId(e.target.value)} />
        </div>
        <p><Button variant="secondary" small loading={busy} onClick={() => void loadOrder()} disabled={!meReady}>{tc.reconciliationTitle}</Button></p>
        {reconciliation ? (
          <div className="tiles">
            {[
              { label: tc.sent, value: reconciliation.sent },
              { label: tc.returned, value: reconciliation.returned },
              { label: tc.outstanding, value: reconciliation.outstanding },
            ].map((tile) => (
              <div className="kpi" key={tile.label}>
                <p className="kpi__label">{tile.label}</p>
                <p className="kpi__value">{fmtNumber(tile.value, locale)}</p>
              </div>
            ))}
          </div>
        ) : null}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.custodyTitle}</h2>
        <form onSubmit={(e) => void submitCustody(e)}>
          <div className="form-grid">
            <Input id="sub-step" label={tc.stepLabel} type="number" min="1" step="1" value={custodyForm.routingOperationSeq} onChange={(e) => setCustodyForm({ ...custodyForm, routingOperationSeq: e.target.value })} error={custodyErrors.step} />
            <Select id="sub-kind" label={tc.colKind} value={custodyForm.kind} onChange={(e) => setCustodyForm({ ...custodyForm, kind: e.target.value })} options={[{ value: "Send", label: tc.kindSend }, { value: "Return", label: tc.kindReturn }]} />
            <Input id="sub-qty" label={tc.qtyLabel} type="number" min="0" step="any" value={custodyForm.qty} onChange={(e) => setCustodyForm({ ...custodyForm, qty: e.target.value })} error={custodyErrors.qty} />
            <Input id="sub-movement" label={tc.movementLabel} value={custodyForm.inventoryMovementId} onChange={(e) => setCustodyForm({ ...custodyForm, inventoryMovementId: e.target.value })} error={custodyErrors.movement} />
          </div>
          <p><Button type="submit" small loading={busy} disabled={!meReady || canWrite === false}>{tc.recordCustody}</Button></p>
        </form>
        {custodies === null ? null : custodies.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={custodyColumns} rows={custodies} caption={tc.custodyTitle} />}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.receiptsTitle}</h2>
        {receipts === null ? null : receipts.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={receiptColumns} rows={receipts} caption={tc.receiptsTitle} />}
        <p><Button small onClick={() => { setReceiveTarget("open"); setReceiveForm({ routingOperationSeq: receiveForm.routingOperationSeq, qty: "", note: "" }); }} disabled={!meReady || canWrite === false}>{tc.receive}</Button></p>
        <Modal open={receiveTarget !== ""} onClose={() => setReceiveTarget("")} title={tc.receiptsTitle} closeLabel={t.common.cancel} busy={busy}>
          <form onSubmit={(e) => void confirmReceive(e)}>
            <div className="form-grid">
              <Input id="sub-rstep" label={tc.stepLabel} type="number" min="1" step="1" value={receiveForm.routingOperationSeq} onChange={(e) => setReceiveForm({ ...receiveForm, routingOperationSeq: e.target.value })} error={receiveErrors.step} hint={stepOptions.length > 0 ? stepOptions.map((o) => o.label).join(", ") : undefined} />
              <Input id="sub-rqty" label={tc.qtyLabel} type="number" min="0" step="any" value={receiveForm.qty} onChange={(e) => setReceiveForm({ ...receiveForm, qty: e.target.value })} error={receiveErrors.qty} />
              <Input id="sub-rnote" label={tc.receivedNote} value={receiveForm.note} onChange={(e) => setReceiveForm({ ...receiveForm, note: e.target.value })} />
            </div>
            <div className="modal__actions">
              <Button type="button" variant="secondary" onClick={() => setReceiveTarget("")}>{t.common.cancel}</Button>
              <Button type="submit" loading={busy}>{tc.receive}</Button>
            </div>
          </form>
        </Modal>
      </section>
    </PageShell>
  );
}
