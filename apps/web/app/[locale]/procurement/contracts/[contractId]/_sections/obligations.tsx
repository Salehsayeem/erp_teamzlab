"use client";

import { Fragment, useState, type FormEvent } from "react";
import { addObligation, procError, submitEvidence, verifyObligation, waiveObligation, type ContractObligation } from "@/lib/services/procurement";
import { Badge, Button, EmptyState, Input, Modal, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPerm, isoDate, OBLIGATION_STATUS_TONES, statusLabel, type ContractCtx } from "../../../_lib/ui";

type ObligationForm = { obligationType: string; ownerUserId: string; dueDate: string; sourceClause: string };
type RowAction = "evidence" | "verify" | "waive";

const EMPTY_FORM: ObligationForm = { obligationType: "", ownerUserId: "", dueDate: "", sourceClause: "" };

export function ObligationsSection({ ctx }: { ctx: ContractCtx }) {
  const { token, t, contract, obligations, setObligations, meId, perms } = ctx;
  const ob = t.procurement.contracts.workspace.obligations;
  const canWrite = hasPerm(perms, "contracts:write");
  const canApprove = hasPerm(perms, "contracts:approve");

  const [form, setForm] = useState<ObligationForm>(EMPTY_FORM);
  const [formErrors, setFormErrors] = useState<Partial<Record<keyof ObligationForm, string>>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);

  const [openAction, setOpenAction] = useState<{ id: string; action: RowAction } | null>(null);
  const [evidenceRef, setEvidenceRef] = useState("");
  const [waiveReason, setWaiveReason] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const errMap: Record<string, string> = {
    OBLIGATION_APPROVAL_ERROR: ob.makerChecker,
    OBLIGATION_STATE_ERROR: ob.makerChecker,
    FORBIDDEN: t.procurement.common.forbidden,
  };

  async function add(e: FormEvent) {
    e.preventDefault();
    const errs: Partial<Record<keyof ObligationForm, string>> = {};
    if (!form.obligationType.trim()) errs.obligationType = ob.errors.typeRequired;
    if (!form.ownerUserId.trim()) errs.ownerUserId = ob.errors.ownerRequired;
    if (!form.dueDate) errs.dueDate = ob.errors.dueRequired;
    setFormErrors(errs);
    setFormError(null);
    if (Object.keys(errs).length > 0) return;
    setAdding(true);
    const res = await addObligation(token, contract.id, {
      obligationType: form.obligationType.trim(),
      ownerUserId: form.ownerUserId.trim(),
      dueDate: form.dueDate,
      sourceClause: form.sourceClause.trim() || null,
    });
    setAdding(false);
    if (!res.ok) {
      setFormError(procError(res.code, res.message, { FORBIDDEN: t.procurement.common.forbidden }, ob.addFailed));
      return;
    }
    setObligations((prev) => [res.data, ...prev]);
    setForm(EMPTY_FORM);
  }

  async function act(target: ContractObligation, action: RowAction) {
    setBusyId(target.id);
    setActionError(null);
    let res;
    if (action === "evidence") res = await submitEvidence(token, contract.id, target.id, evidenceRef.trim());
    else if (action === "verify") res = await verifyObligation(token, contract.id, target.id);
    else res = await waiveObligation(token, contract.id, target.id, waiveReason.trim());
    setBusyId(null);
    if (!res.ok) {
      setActionError(procError(res.code, res.message, errMap, action === "waive" ? ob.waiveFailed : action === "verify" ? ob.verifyFailed : ob.evidenceFailed));
      return;
    }
    const updated = res.data;
    setObligations((prev) => prev.map((x) => (x.id === updated.id ? updated : x)));
    setOpenAction(null);
    setEvidenceRef("");
    setWaiveReason("");
  }

  const isMakerOf = (o: ContractObligation) => meId !== null && o.createdBy === meId;
  const actionable = (o: ContractObligation) => o.status === "Open" || o.status === "EvidenceSubmitted";

  const columns: Column<ContractObligation>[] = [
    { key: "type", label: ob.columns.type, render: (o) => o.obligationType },
    { key: "owner", label: ob.columns.owner, mono: true, render: (o) => o.ownerUserId },
    { key: "due", label: ob.columns.due, mono: true, render: (o) => isoDate(o.dueDate) },
    {
      key: "status",
      label: ob.columns.status,
      render: (o) => (
        <>
          <Badge tone={OBLIGATION_STATUS_TONES[o.status] ?? "muted"}>{statusLabel(t, o.status)}</Badge>{" "}
          {o.overdue ? <Badge tone="danger">{ob.overdueBadge}</Badge> : null}
        </>
      ),
    },
    { key: "evidence", label: ob.columns.evidence, mono: true, render: (o) => o.evidenceRef ?? "—" },
    {
      key: "actions",
      label: t.procurement.common.actions,
      render: (o) =>
        actionable(o) ? (
          <div className="row-actions">
            {canWrite ? (
              <Button small variant="secondary" onClick={() => { setOpenAction({ id: o.id, action: "evidence" }); setEvidenceRef(o.evidenceRef ?? ""); }}>
                {ob.submitEvidence}
              </Button>
            ) : null}
            {canApprove ? (
              isMakerOf(o) ? (
                <span className="t-caption">{ob.makerChecker}</span>
              ) : (
                <>
                  <Button small disabled={meId === null} onClick={() => { setActionError(null); setOpenAction({ id: o.id, action: "verify" }); }}>{ob.verify}</Button>
                  <Button small variant="danger" disabled={meId === null} onClick={() => { setActionError(null); setWaiveReason(""); setOpenAction({ id: o.id, action: "waive" }); }}>{ob.waive}</Button>
                </>
              )
            ) : null}
          </div>
        ) : null,
    },
  ];

  const openTarget = obligations.find((o) => openAction !== null && o.id === openAction.id) ?? null;

  return (
    <section className="card">
      <h2 className="t-title">{ob.title}</h2>
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}
      {formError ? <p className="banner" role="alert">{formError}</p> : null}
      {obligations.length === 0 ? (
        <EmptyState title={ob.empty} />
      ) : (
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>{columns.map((c) => <th key={c.key} scope="col">{c.label}</th>)}</tr>
            </thead>
            <tbody>
              {obligations.map((o) => (
                <Fragment key={o.id}>
                  <tr>
                    {columns.map((c) => (
                      <td key={c.key} data-label={c.label} className={c.key === "owner" || c.key === "evidence" ? "mono" : undefined}>
                        {c.render(o)}
                      </td>
                    ))}
                  </tr>
                  {openAction?.id === o.id && openAction.action === "evidence" ? (
                    <tr className="sub-row">
                      <td colSpan={columns.length}>
                        <div className="stack">
                          <Input id={`ev-${o.id}`} label={ob.evidenceRef} value={evidenceRef} onChange={(e) => setEvidenceRef(e.target.value)} />
                          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                            <Button small loading={busyId === o.id} disabled={!evidenceRef.trim()} onClick={() => void act(o, "evidence")}>{ob.submitEvidence}</Button>
                            <Button small variant="secondary" onClick={() => setOpenAction(null)}>{t.common.cancel}</Button>
                          </div>
                        </div>
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {canWrite ? (
        <form onSubmit={(e) => void add(e)}>
          <h3 className="t-body-strong">{ob.addTitle}</h3>
          <div className="form-grid">
            <Input id="ob-type" label={ob.obligationType} value={form.obligationType} onChange={(e) => setForm({ ...form, obligationType: e.target.value })} error={formErrors.obligationType} />
            <Input id="ob-owner" label={ob.ownerUserId} value={form.ownerUserId} onChange={(e) => setForm({ ...form, ownerUserId: e.target.value })} error={formErrors.ownerUserId} />
            <Input id="ob-due" label={ob.dueDate} type="date" value={form.dueDate} onChange={(e) => setForm({ ...form, dueDate: e.target.value })} error={formErrors.dueDate} />
            <Input id="ob-clause" label={ob.sourceClause} value={form.sourceClause} onChange={(e) => setForm({ ...form, sourceClause: e.target.value })} />
          </div>
          <p><Button type="submit" loading={adding}>{ob.addTitle}</Button></p>
        </form>
      ) : null}

      <Modal
        open={openAction?.action === "verify"}
        onClose={() => setOpenAction(null)}
        title={ob.verifyTitle}
        closeLabel={t.common.cancel}
        busy={busyId !== null}
      >
        <p>{ob.verifyConsequence}</p>
        {openTarget ? <p className="t-caption mono">{openTarget.obligationType} · {openTarget.ownerUserId}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setOpenAction(null)}>{t.common.cancel}</Button>
          <Button loading={busyId !== null} onClick={() => openTarget && void act(openTarget, "verify")}>{ob.verify}</Button>
        </div>
      </Modal>

      <Modal
        open={openAction?.action === "waive"}
        onClose={() => setOpenAction(null)}
        title={ob.waiveTitle}
        closeLabel={t.common.cancel}
        busy={busyId !== null}
      >
        <p>{ob.waiveConsequence}</p>
        <Input id="waive-reason" label={ob.waiveReason} value={waiveReason} onChange={(e) => setWaiveReason(e.target.value)} />
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setOpenAction(null)}>{t.common.cancel}</Button>
          <Button variant="danger" loading={busyId !== null} disabled={!waiveReason.trim()} onClick={() => openTarget && void act(openTarget, "waive")}>{ob.waive}</Button>
        </div>
      </Modal>
    </section>
  );
}
