"use client";

import { useState, type FormEvent } from "react";
import {
  addCriterion,
  addLot,
  procError,
  publishEvent,
  type CriterionKind,
  type EventLot,
  type EvalCriterion,
} from "@/lib/services/procurement";
import { fmtNumber } from "@/lib/format";
import { Badge, Button, EmptyState, Input, Modal, Select, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { statusLabel, type WorkspaceCtx } from "../../../_lib/ui";

type LotForm = { lotNo: string; description: string; itemSku: string; targetQty: string; ceilingValue: string };
type CriterionForm = { code: string; name: string; kind: CriterionKind; weightPct: string; scaleMax: string };

const EMPTY_LOT: LotForm = { lotNo: "", description: "", itemSku: "", targetQty: "", ceilingValue: "" };
const EMPTY_CRITERION: CriterionForm = { code: "", name: "", kind: "Scored", weightPct: "", scaleMax: "" };

export function SetupTab({ ctx }: { ctx: WorkspaceCtx }) {
  const { token, t, locale, event, detail, applyDetail, onEvent } = ctx;
  const st = t.procurement.sourcing.workspace.setup;
  const [lotForm, setLotForm] = useState<LotForm>(EMPTY_LOT);
  const [lotErrors, setLotErrors] = useState<Partial<Record<keyof LotForm, string>>>({});
  const [criterionForm, setCriterionForm] = useState<CriterionForm>(EMPTY_CRITERION);
  const [criterionErrors, setCriterionErrors] = useState<Partial<Record<keyof CriterionForm, string>>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busyLot, setBusyLot] = useState(false);
  const [busyCriterion, setBusyCriterion] = useState(false);
  const [publishOpen, setPublishOpen] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const [publishError, setPublishError] = useState<string | null>(null);

  const errMap: Record<string, string> = {
    EVENT_PUBLISHED: st.freezeNote,
    FORBIDDEN: t.procurement.common.forbidden,
  };
  const publishErrMap: Record<string, string> = {
    EVENT_PUBLISH_BLOCKED: st.publishNeedsWeights,
    EVENT_STATE_ERROR: st.freezeNote,
    FORBIDDEN: t.procurement.common.forbidden,
  };

  const weightTotal = detail.criteria.reduce((sum, c) => sum + c.weightPct, 0);
  const weightsOk = weightTotal === 100;
  const invitationsCount = detail.invitations.length;

  async function submitLot(e: FormEvent) {
    e.preventDefault();
    const errs: Partial<Record<keyof LotForm, string>> = {};
    if (!lotForm.lotNo.trim()) errs.lotNo = st.errors.lotNoRequired;
    if (!lotForm.description.trim()) errs.description = st.errors.lotDescriptionRequired;
    if (lotForm.targetQty && !(Number(lotForm.targetQty) > 0)) errs.targetQty = st.errors.targetQtyInvalid;
    if (lotForm.ceilingValue && !(Number(lotForm.ceilingValue) >= 0)) errs.ceilingValue = st.errors.ceilingInvalid;
    setLotErrors(errs);
    setFormError(null);
    if (Object.keys(errs).length > 0) return;
    setBusyLot(true);
    const res = await addLot(token, event.id, {
      lotNo: lotForm.lotNo.trim(),
      description: lotForm.description.trim(),
      itemSku: lotForm.itemSku.trim() || null,
      targetQty: lotForm.targetQty ? Number(lotForm.targetQty) : null,
      ceilingValue: lotForm.ceilingValue ? Number(lotForm.ceilingValue) : null,
    });
    setBusyLot(false);
    if (!res.ok) {
      setFormError(procError(res.code, res.message, errMap, st.addLotFailed));
      return;
    }
    applyDetail({ lots: [...detail.lots, res.data] });
    setLotForm(EMPTY_LOT);
  }

  async function submitCriterion(e: FormEvent) {
    e.preventDefault();
    const errs: Partial<Record<keyof CriterionForm, string>> = {};
    if (!criterionForm.code.trim()) errs.code = st.errors.codeRequired;
    if (!criterionForm.name.trim()) errs.name = st.errors.nameRequired;
    if (!(Number(criterionForm.weightPct) > 0)) errs.weightPct = st.errors.weightInvalid;
    if (criterionForm.kind === "Scored" && !(Number(criterionForm.scaleMax) > 0)) errs.scaleMax = st.errors.scaleMaxInvalid;
    setCriterionErrors(errs);
    setFormError(null);
    if (Object.keys(errs).length > 0) return;
    setBusyCriterion(true);
    const res = await addCriterion(token, event.id, {
      code: criterionForm.code.trim(),
      name: criterionForm.name.trim(),
      kind: criterionForm.kind,
      weightPct: Number(criterionForm.weightPct),
      scaleMax: criterionForm.kind === "Scored" ? Number(criterionForm.scaleMax) : 0,
    });
    setBusyCriterion(false);
    if (!res.ok) {
      setFormError(procError(res.code, res.message, errMap, st.addCriterionFailed));
      return;
    }
    applyDetail({ criteria: [...detail.criteria, res.data] });
    setCriterionForm(EMPTY_CRITERION);
  }

  async function publish() {
    setPublishing(true);
    setPublishError(null);
    const res = await publishEvent(token, event.id);
    setPublishing(false);
    if (!res.ok) {
      setPublishError(procError(res.code, res.message, publishErrMap, st.publishFailed));
      return;
    }
    setPublishOpen(false);
    onEvent(res.data);
  }

  const lotColumns: Column<EventLot>[] = [
    { key: "lotNo", label: st.lotColumns.lotNo, mono: true, render: (l) => l.lotNo },
    { key: "description", label: st.lotColumns.description, render: (l) => l.description },
    { key: "itemSku", label: st.lotColumns.itemSku, mono: true, render: (l) => l.itemSku ?? "—" },
    { key: "targetQty", label: st.lotColumns.targetQty, mono: true, render: (l) => (l.targetQty === null ? "—" : fmtNumber(l.targetQty, locale)) },
    { key: "ceilingValue", label: st.lotColumns.ceilingValue, mono: true, render: (l) => (l.ceilingValue === null ? "—" : fmtNumber(l.ceilingValue, locale)) },
  ];
  const criterionColumns: Column<EvalCriterion>[] = [
    { key: "code", label: st.criterionColumns.code, mono: true, render: (c) => c.code },
    { key: "name", label: st.criterionColumns.name, render: (c) => c.name },
    { key: "kind", label: st.criterionColumns.kind, render: (c) => <Badge tone="muted">{statusLabel(t, c.kind)}</Badge> },
    { key: "weightPct", label: st.criterionColumns.weightPct, mono: true, render: (c) => fmtNumber(c.weightPct, locale) },
    { key: "scaleMax", label: st.criterionColumns.scaleMax, mono: true, render: (c) => (c.kind === "Scored" ? fmtNumber(c.scaleMax, locale) : "—") },
  ];

  return (
    <div className="stack">
      {formError ? <p className="banner" role="alert">{formError}</p> : null}
      {publishError ? <p className="banner" role="alert">{publishError}</p> : null}

      <section className="card">
        <h3 className="t-title">{st.lotsTitle}</h3>
        {detail.lots.length === 0 ? <EmptyState title={st.emptyLots} /> : <Table<EventLot> columns={lotColumns} rows={detail.lots} caption={st.lotsTitle} />}
        <form onSubmit={(e) => void submitLot(e)}>
          <h4 className="t-body-strong">{st.addLotTitle}</h4>
          <div className="form-grid">
            <Input id="lot-no" label={st.lotNo} value={lotForm.lotNo} onChange={(e) => setLotForm({ ...lotForm, lotNo: e.target.value })} error={lotErrors.lotNo} />
            <Input id="lot-desc" label={st.lotDescription} value={lotForm.description} onChange={(e) => setLotForm({ ...lotForm, description: e.target.value })} error={lotErrors.description} />
            <Input id="lot-sku" label={st.itemSku} value={lotForm.itemSku} onChange={(e) => setLotForm({ ...lotForm, itemSku: e.target.value })} />
            <Input id="lot-qty" label={st.targetQty} type="number" min="0" step="any" value={lotForm.targetQty} onChange={(e) => setLotForm({ ...lotForm, targetQty: e.target.value })} error={lotErrors.targetQty} />
            <Input id="lot-ceiling" label={st.ceilingValue} type="number" min="0" step="any" value={lotForm.ceilingValue} onChange={(e) => setLotForm({ ...lotForm, ceilingValue: e.target.value })} error={lotErrors.ceilingValue} />
          </div>
          <p><Button type="submit" variant="secondary" loading={busyLot}>{st.addLotTitle}</Button></p>
        </form>
      </section>

      <section className="card">
        <h3 className="t-title">{st.criteriaTitle}</h3>
        {detail.criteria.length === 0 ? (
          <EmptyState title={st.emptyCriteria} />
        ) : (
          <Table<EvalCriterion> columns={criterionColumns} rows={detail.criteria} caption={st.criteriaTitle} />
        )}
        <p className="t-caption">
          {st.weightSumLabel}: <span className="mono">{fmtNumber(weightTotal, locale)}</span>{" "}
          {weightsOk ? <Badge tone="success">{st.weightSumOk}</Badge> : <Badge tone="warning">{st.weightSumBad}</Badge>}
        </p>
        <form onSubmit={(e) => void submitCriterion(e)}>
          <h4 className="t-body-strong">{st.addCriterionTitle}</h4>
          <div className="form-grid">
            <Input id="crit-code" label={st.code} value={criterionForm.code} onChange={(e) => setCriterionForm({ ...criterionForm, code: e.target.value })} error={criterionErrors.code} />
            <Input id="crit-name" label={st.name} value={criterionForm.name} onChange={(e) => setCriterionForm({ ...criterionForm, name: e.target.value })} error={criterionErrors.name} />
            <Select
              id="crit-kind"
              label={st.kind}
              options={(["Scored", "PassFail"] as CriterionKind[]).map((v) => ({ value: v, label: v === "Scored" ? st.kindScored : st.kindPassFail }))}
              value={criterionForm.kind}
              onChange={(e) => setCriterionForm({ ...criterionForm, kind: e.target.value as CriterionKind })}
            />
            <Input id="crit-weight" label={st.weightPct} type="number" min="0" step="any" value={criterionForm.weightPct} onChange={(e) => setCriterionForm({ ...criterionForm, weightPct: e.target.value })} error={criterionErrors.weightPct} />
            {criterionForm.kind === "Scored" ? (
              <Input id="crit-scale" label={st.scaleMax} hint={st.scaleMaxHint} type="number" min="1" step="1" value={criterionForm.scaleMax} onChange={(e) => setCriterionForm({ ...criterionForm, scaleMax: e.target.value })} error={criterionErrors.scaleMax} />
            ) : null}
          </div>
          <p><Button type="submit" variant="secondary" loading={busyCriterion}>{st.addCriterionTitle}</Button></p>
        </form>
      </section>

      <section className="card">
        <h3 className="t-title">{st.publish}</h3>
        <p className="t-caption">{st.freezeNote}</p>
        {invitationsCount === 0 ? <p className="banner" role="status">{st.publishNeedsInvites}</p> : null}
        {!weightsOk ? <p className="banner" role="status">{st.publishNeedsWeights}</p> : null}
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          <Button disabled={!weightsOk || invitationsCount === 0} onClick={() => { setPublishError(null); setPublishOpen(true); }}>
            {st.publish}
          </Button>
        </div>
      </section>

      <Modal open={publishOpen} onClose={() => setPublishOpen(false)} title={st.publishTitle} closeLabel={t.common.cancel} busy={publishing}>
        <p>{st.publishConsequence}</p>
        {publishError ? <p className="banner" role="alert">{publishError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setPublishOpen(false)}>{t.common.cancel}</Button>
          <Button loading={publishing} onClick={() => void publish()}>{st.publish}</Button>
        </div>
      </Modal>
    </div>
  );
}
