"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  decideAward,
  fetchBids,
  fetchEventAwards,
  procError,
  recommendAward,
  type Award,
  type AwardLotAllocation,
} from "@/lib/services/procurement";
import { fmtMoney, fmtNumber } from "@/lib/format";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Select, Table, Textarea } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { AWARD_STATUS_TONES, hasPerm, statusLabel, type WorkspaceCtx } from "../../../_lib/ui";

type RecommendForm = { lotId: string; supplierId: string; qty: string; unitPrice: string; recommendation: string };
type Decision = { kind: "approve" | "reject"; award: Award };

export function AwardsTab({ ctx, active }: { ctx: WorkspaceCtx; active: boolean }) {
  const { token, t, locale, event, detail, meId, perms, onEvent } = ctx;
  const aw = t.procurement.sourcing.workspace.awards;
  const canWrite = hasPerm(perms, "sourcing:write");
  const canApprove = hasPerm(perms, "sourcing:approve");

  const [awards, setAwards] = useState<Award[]>([]);
  const [awardsFromApi, setAwardsFromApi] = useState(false);
  const [bidsLoaded, setBidsLoaded] = useState(false);
  const [bidSuppliers, setBidSuppliers] = useState<string[]>([]);
  const [bidLotIds, setBidLotIds] = useState<string[]>([]);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");

  const [form, setForm] = useState<RecommendForm>({ lotId: "", supplierId: "", qty: "", unitPrice: "", recommendation: "" });
  const [formErrors, setFormErrors] = useState<Partial<Record<keyof RecommendForm, string>>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [recommending, setRecommending] = useState(false);

  const [decision, setDecision] = useState<Decision | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [decisionError, setDecisionError] = useState<string | null>(null);
  const [deciding, setDeciding] = useState(false);
  const [allocations, setAllocations] = useState<AwardLotAllocation[]>([]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const awardsRes = await fetchEventAwards(token, event.id);
    const bidsRes = await fetchBids(token, event.id);
    if (awardsRes.ok) {
      setAwards(awardsRes.data);
      setAwardsFromApi(true);
      setPhase("success");
    } else if (bidsRes.ok || awardsRes.code === "HTTP_404" || awardsRes.code === "SHAPE") {
      // Awards read endpoint not merged yet — the tab stays usable in-session.
      setAwards([]);
      setAwardsFromApi(false);
      setPhase("success");
    } else {
      setPhase("error");
      return;
    }
    if (bidsRes.ok) {
      setBidSuppliers([...new Set(bidsRes.data.map((x) => x.supplierId))]);
      // Sealed bid lines still carry lotId — the lot fallback when the event read
      // endpoint (with lots) has not landed yet.
      setBidLotIds([...new Set(bidsRes.data.flatMap((x) => x.lines.map((l) => l.lotId)))]);
      setBidsLoaded(true);
    }
  }, [token, event.id]);
  useEffect(() => { if (active) void load(); }, [active, load]);

  const errMap: Record<string, string> = {
    SUPPLIER_INELIGIBLE: t.procurement.supplierDetail.lifecycle.blocking,
    AWARD_OVER_CEILING: aw.errors.qtyInvalid,
    EVENT_NOT_AWARDABLE: t.procurement.sourcing.workspace.noAwardIntent,
    EVENT_STILL_OPEN: aw.stillOpenNote,
    BID_DISQUALIFIED: t.procurement.sourcing.workspace.evaluation.disqualified,
    AWARD_APPROVAL_ERROR: aw.makerChecker,
    AWARD_STATE_ERROR: aw.makerChecker,
    FORBIDDEN: t.procurement.common.forbidden,
  };

  async function recommend(e: FormEvent) {
    e.preventDefault();
    const errs: Partial<Record<keyof RecommendForm, string>> = {};
    if (!form.lotId) errs.lotId = aw.errors.lotRequired;
    if (!form.supplierId) errs.supplierId = aw.errors.supplierRequired;
    if (!(Number(form.qty) > 0)) errs.qty = aw.errors.qtyInvalid;
    if (!(Number(form.unitPrice) >= 0) || form.unitPrice === "") errs.unitPrice = aw.errors.priceInvalid;
    if (!form.recommendation.trim()) errs.recommendation = aw.errors.recommendationRequired;
    setFormErrors(errs);
    setFormError(null);
    if (Object.keys(errs).length > 0) return;
    setRecommending(true);
    const res = await recommendAward(token, {
      eventId: event.id,
      lotId: form.lotId,
      supplierId: form.supplierId,
      qty: Number(form.qty),
      unitPrice: Number(form.unitPrice),
      recommendation: form.recommendation.trim(),
    });
    setRecommending(false);
    if (!res.ok) {
      setFormError(procError(res.code, res.message, errMap, aw.recommendFailed));
      return;
    }
    setAwards((prev) => [res.data, ...prev]);
    setForm({ lotId: "", supplierId: "", qty: "", unitPrice: "", recommendation: "" });
  }

  async function runDecision() {
    if (decision === null) return;
    if (decision.kind === "reject" && !rejectReason.trim()) {
      setDecisionError(aw.reasonRequired);
      return;
    }
    setDeciding(true);
    setDecisionError(null);
    const res = await decideAward(token, decision.award.id, decision.kind === "reject" ? rejectReason.trim() : null);
    setDeciding(false);
    if (!res.ok) {
      setDecisionError(procError(res.code, res.message, errMap, aw.decideFailed));
      return;
    }
    setAwards((prev) => prev.map((a) => (a.id === res.data.award.id ? res.data.award : a)));
    const lot = res.data.lot;
    if (lot) setAllocations((prev) => [...prev.filter((x) => x.lotId !== lot.lotId), lot]);
    const movedEvent = res.data.event;
    if (movedEvent) onEvent(movedEvent);
    setDecision(null);
    setRejectReason("");
  }

  const makerOf = (award: Award) => meId !== null && award.createdBy === meId;
  const windowClosed = Date.now() > new Date(event.closesAt).getTime();

  const awardColumns: Column<Award>[] = [
    { key: "id", label: aw.awardColumns.id, mono: true, render: (a) => a.id },
    { key: "lot", label: aw.awardColumns.lot, mono: true, render: (a) => a.lotId },
    { key: "supplier", label: aw.awardColumns.supplier, mono: true, render: (a) => a.supplierId },
    { key: "qty", label: aw.awardColumns.qty, mono: true, render: (a) => fmtNumber(a.qty, locale) },
    { key: "unitPrice", label: aw.awardColumns.unitPrice, mono: true, render: (a) => (a.unitPrice === undefined ? "—" : fmtMoney(a.unitPrice, locale, event.currency)) },
    { key: "status", label: aw.awardColumns.status, render: (a) => <Badge tone={AWARD_STATUS_TONES[a.status] ?? "muted"}>{statusLabel(t, a.status)}</Badge> },
    { key: "basis", label: aw.awardColumns.basis, mono: true, render: (a) => `v${a.basisEventVersion}` },
    { key: "recommendation", label: aw.awardColumns.recommendation, render: (a) => a.recommendation ?? "—" },
    {
      key: "actions",
      label: aw.awardColumns.actions,
      render: (a) =>
        a.status === "Recommended" && canApprove ? (
          makerOf(a) ? (
            <p className="t-caption">{aw.makerChecker}</p>
          ) : (
            <div className="row-actions">
              <Button small disabled={meId === null} onClick={() => { setDecisionError(null); setDecision({ kind: "approve", award: a }); }}>
                {aw.approve}
              </Button>
              <Button small variant="danger" disabled={meId === null} onClick={() => { setDecisionError(null); setRejectReason(""); setDecision({ kind: "reject", award: a }); }}>
                {aw.reject}
              </Button>
            </div>
          )
        ) : null,
    },
  ];

  if (phase === "loading") return <LoadingSkeleton rows={4} />;
  if (phase === "error") return <ErrorState message={aw.decideFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      {formError ? <p className="banner" role="alert">{formError}</p> : null}
      {decisionError ? <p className="banner" role="alert">{decisionError}</p> : null}
      {!windowClosed ? <p className="banner" role="status">{aw.stillOpenNote}</p> : null}
      {!awardsFromApi ? <p className="t-caption">{aw.sessionOnly}</p> : null}

      {canWrite ? (
        <section className="card">
          <h3 className="t-title">{aw.recommendTitle}</h3>
          {detail.lots.length === 0 && bidLotIds.length === 0 ? (
            <EmptyState title={aw.noLots} />
          ) : !bidsLoaded || bidSuppliers.length === 0 ? (
            <EmptyState title={aw.noBids} />
          ) : (
            <form onSubmit={(e) => void recommend(e)}>
              <div className="form-grid">
                <Select
                  id="aw-lot"
                  label={aw.lot}
                  placeholder={aw.lot}
                  options={
                    detail.lots.length > 0
                      ? detail.lots.map((l) => ({ value: l.id, label: `${l.lotNo} — ${l.description}` }))
                      : bidLotIds.map((id) => ({ value: id, label: id }))
                  }
                  value={form.lotId}
                  onChange={(e) => {
                    const lot = detail.lots.find((l) => l.id === e.target.value);
                    setForm({ ...form, lotId: e.target.value, qty: lot?.targetQty !== null && lot?.targetQty !== undefined ? String(lot.targetQty) : form.qty });
                  }}
                  error={formErrors.lotId}
                />
                <Select
                  id="aw-supplier"
                  label={aw.supplier}
                  placeholder={aw.supplier}
                  options={bidSuppliers.map((id) => ({ value: id, label: id }))}
                  value={form.supplierId}
                  onChange={(e) => setForm({ ...form, supplierId: e.target.value })}
                  error={formErrors.supplierId}
                />
                <Input id="aw-qty" label={aw.qty} type="number" min="0" step="any" value={form.qty} onChange={(e) => setForm({ ...form, qty: e.target.value })} error={formErrors.qty} />
                <Input id="aw-price" label={aw.unitPrice} type="number" min="0" step="any" value={form.unitPrice} onChange={(e) => setForm({ ...form, unitPrice: e.target.value })} error={formErrors.unitPrice} />
              </div>
              <div style={{ marginBlockStart: 12 }}>
                <Textarea id="aw-recommendation" label={aw.recommendation} value={form.recommendation} onChange={(e) => setForm({ ...form, recommendation: e.target.value })} error={formErrors.recommendation} />
              </div>
              <p><Button type="submit" loading={recommending}>{aw.recommend}</Button></p>
            </form>
          )}
        </section>
      ) : null}

      <section className="card">
        <h3 className="t-title">{aw.title}</h3>
        {awards.length === 0 ? <EmptyState title={aw.empty} /> : <Table<Award> columns={awardColumns} rows={awards} caption={aw.title} />}
      </section>

      {allocations.length > 0 ? (
        <section className="card">
          <h3 className="t-title">{aw.allocationTitle}</h3>
          <Table
            columns={[
              { key: "lot", label: aw.allocationColumns.lot, mono: true, render: (x: AwardLotAllocation) => x.lotId },
              { key: "target", label: aw.allocationColumns.target, mono: true, render: (x: AwardLotAllocation) => (x.targetQty === null ? "—" : fmtNumber(x.targetQty, locale)) },
              { key: "awarded", label: aw.allocationColumns.awarded, mono: true, render: (x: AwardLotAllocation) => fmtNumber(x.awardedQty, locale) },
              { key: "remaining", label: aw.allocationColumns.remaining, mono: true, render: (x: AwardLotAllocation) => (x.remainingTargetQty === null ? "—" : fmtNumber(x.remainingTargetQty, locale)) },
            ]}
            rows={allocations}
            caption={aw.allocationTitle}
          />
        </section>
      ) : null}

      <Modal
        open={decision !== null}
        onClose={() => setDecision(null)}
        title={decision?.kind === "reject" ? aw.rejectTitle : aw.approveTitle}
        closeLabel={t.common.cancel}
        busy={deciding}
      >
        <p>{decision?.kind === "reject" ? aw.rejectConsequence : aw.approveConsequence}</p>
        {decision?.kind === "reject" ? (
          <Input id="aw-reject-reason" label={aw.rejectReason} value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} />
        ) : null}
        {decisionError ? <p className="banner" role="alert">{decisionError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setDecision(null)}>{t.common.cancel}</Button>
          <Button
            variant={decision?.kind === "reject" ? "danger" : "primary"}
            loading={deciding}
            onClick={() => void runDecision()}
          >
            {decision?.kind === "reject" ? aw.reject : aw.approve}
          </Button>
        </div>
      </Modal>
    </div>
  );
}
