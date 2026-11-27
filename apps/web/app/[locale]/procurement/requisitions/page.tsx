"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  fetchRequisitions, createRequisition, submitRequisition, decideRequisition,
  fetchCategories, fetchUnits,
  type Requisition,
} from "@/lib/services/procurement";
import { fmtDate, fmtMoney } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";
type DecisionForm = { id: string; version: number };

export default function RequisitionsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);

  const [rows, setRows] = useState<Requisition[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [cats, setCats] = useState<{ id: string; name: string }[]>([]);
  const [units, setUnits] = useState<string[]>([]);
  const [purpose, setPurpose] = useState("");
  const [cat, setCat] = useState("");
  const [desc, setDesc] = useState("");
  const [qty, setQty] = useState("");
  const [unit, setUnit] = useState("EA");
  const [price, setPrice] = useState("");
  const [needBy, setNeedBy] = useState("");
  const [decision, setDecision] = useState<DecisionForm | null>(null);
  const [dKind, setDKind] = useState("Approve");
  const [dReason, setDReason] = useState("");
  const [submitFor, setSubmitFor] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchRequisitions(token);
    if (res.ok) { setRows(res.data); setPhase("success"); }
    else { setRows(null); setPhase("error"); }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    void (async () => {
      const c = await fetchCategories(token);
      if (c.ok) setCats(c.data.filter((x) => x.status === "Active").map((x) => ({ id: x.id, name: x.name })));
      const u = await fetchUnits(token);
      if (u.ok) setUnits(u.data.map((x) => x.code));
    })();
  }, [token]);

  async function run(id: string, fn: () => Promise<{ ok: true } | { ok: false; message: string }>) {
    setBusyId(id);
    setErr(null);
    const res = await fn();
    setBusyId(null);
    if (!res.ok) { setErr(`${t.procurement.common.actionFailed}: ${res.message}`); return false; }
    await load();
    return true;
  }

  async function onCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!token) return;
    const q = Number(qty); const p = Number(price);
    if (!purpose.trim() || !cat || !desc.trim() || !needBy || !Number.isFinite(q) || q <= 0 || !Number.isFinite(p) || p < 0) return;
    const ok = await run("new", () => createRequisition(token, {
      purpose: purpose.trim(), currency: "BDT",
      lines: [{ categoryId: cat, description: desc.trim(), qty: q, unitPrice: p, unit, needBy }],
    }));
    if (ok) { setPurpose(""); setDesc(""); setQty(""); setPrice(""); }
  }

  async function onSubmit(id: string) {
    const ok = await run(id, () => submitRequisition(token ?? "", id, null));
    if (ok) setSubmitFor(null); // confirmed submit — close the dialog once the row flips
  }

  async function onDecide(e: React.FormEvent) {
    e.preventDefault();
    if (!decision) return;
    const ok = await run(decision.id, () => decideRequisition(token ?? "", decision.id, {
      decision: dKind, version: decision.version, reason: dReason.trim() || null,
    }));
    if (ok) setDecision(null);
  }

  const statusLabel = (s: string) => (t.common.status as Record<string, string>)[s] ?? s;
  const list = rows ?? [];
  const queue = list.filter((r) => r.status === "Submitted");

  if (!ready || (phase === "loading" && rows === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={t.procurement.reqs.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const table = (rowsToRender: Requisition[]) => (
    <div className="table-scroll">
      <table className="table">
        <thead>
          <tr>
            <th scope="col">{t.procurement.reqs.title}</th>
            <th scope="col">{t.procurement.common.status}</th>
            <th scope="col">{t.procurement.reqs.qty}</th>
            <th scope="col">{t.procurement.reqs.unitPrice}</th>
            <th scope="col">{t.journals.actions}</th>
          </tr>
        </thead>
        <tbody>
          {rowsToRender.map((r) => (
            <tr key={r.id}>
              <td data-label={t.procurement.reqs.title}>
                <Link className="mono" href={`/procurement/requisitions/${encodeURIComponent(r.id)}`}>{r.id}</Link> — {r.purpose}
              </td>
              <td data-label={t.procurement.common.status}><Badge tone={statusTone(r.status)}>{statusLabel(r.status)}</Badge></td>
              <td className="mono">{r.totalQty ?? "—"}</td>
              <td className="mono">{fmtMoney(r.total, locale, r.currency ?? "BDT")}</td>
              <td>
                <div className="row-actions">
                  {r.status === "Draft" ? (
                    <Button small disabled={busyId !== null} onClick={() => setSubmitFor(r.id)}>
                      {t.procurement.reqs.submit}
                    </Button>
                  ) : null}
                  {r.status === "Submitted" ? (
                    <Button small onClick={() => { setDecision({ id: r.id, version: r.stepVersion ?? r.version }); setDKind("Approve"); setDReason(""); }}>
                      {t.procurement.reqs.decision}
                    </Button>
                  ) : null}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );

  const intakeForm = (
    <form onSubmit={(e) => void onCreate(e)} className="stack">
      <p className="t-caption">{t.procurement.reqs.draftNote}</p>
      <Input id="req-purpose" label={t.procurement.reqs.purpose} value={purpose} onChange={(e) => setPurpose(e.target.value)} required />
      <div className="form-grid">
        <div className="field">
          <label htmlFor="req-cat">{t.procurement.reqs.category}</label>
          <select id="req-cat" className="input" value={cat} onChange={(e) => setCat(e.target.value)} required>
            <option value="">—</option>
            {cats.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="req-unit">{t.procurement.reqs.unit}</label>
          <select id="req-unit" className="input" value={unit} onChange={(e) => setUnit(e.target.value)}>
            {units.map((u) => <option key={u} value={u}>{u}</option>)}
            <option value="EA">EA</option>
          </select>
        </div>
      </div>
      <Input id="req-desc" label={t.procurement.reqs.description} value={desc} onChange={(e) => setDesc(e.target.value)} required />
      <div className="form-grid">
        <Input id="req-qty" label={t.procurement.reqs.qty} type="number" min="0" step="any" value={qty} onChange={(e) => setQty(e.target.value)} required />
        <Input id="req-price" label={t.procurement.reqs.unitPrice} type="number" min="0" step="any" value={price} onChange={(e) => setPrice(e.target.value)} required />
        <Input id="req-needby" label={t.procurement.reqs.needBy} type="date" value={needBy} onChange={(e) => setNeedBy(e.target.value)} required />
      </div>
      <div className="row-actions" style={{ justifyContent: "flex-start" }}>
        <Button type="submit" small loading={busyId === "new"} disabled={busyId !== null}>{t.procurement.reqs.create}</Button>
      </div>
    </form>
  );

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.procurement.reqs.title}</h1>
        <p className="t-caption">{t.procurement.reqs.subtitle}</p>
      </div>
      {err ? <p className="banner" role="alert">{err}</p> : null}
      <section className="card">
        <h2 className="t-title">{t.procurement.reqs.queueTab}</h2>
        {queue.length === 0 ? <EmptyState title={t.procurement.reqs.queueEmpty} /> : table(queue)}
      </section>
      <section className="card">
        <h2 className="t-title">{t.procurement.reqs.listTab}</h2>
        {list.length === 0 ? <EmptyState title={t.procurement.reqs.empty} /> : table(list)}
      </section>
      <section className="card">
        <h2 className="t-title">{t.procurement.reqs.newTitle}</h2>
        {intakeForm}
      </section>
      <Modal open={submitFor !== null} onClose={() => setSubmitFor(null)} title={t.procurement.reqs.submit} closeLabel={t.common.cancel}>
        <div className="stack">
          <p className="t-caption">{t.procurement.reqs.submitConfirm}</p>
          <p className="t-caption mono">{submitFor}</p>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button small loading={busyId === submitFor} disabled={busyId !== null} onClick={() => void onSubmit(submitFor!)}>
              {t.procurement.reqs.submit}
            </Button>
          </div>
        </div>
      </Modal>
      <Modal open={decision !== null} onClose={() => setDecision(null)} title={t.procurement.reqs.decision} closeLabel={t.common.cancel}>
        <form onSubmit={(e) => void onDecide(e)} className="stack">
          <div className="field">
            <label htmlFor="dec-kind">{t.procurement.reqs.decision}</label>
            <select id="dec-kind" className="input" value={dKind} onChange={(e) => setDKind(e.target.value)}>
              {["Approve", "Reject", "Return", "RequestInfo"].map((k) => <option key={k} value={k}>{k}</option>)}
            </select>
          </div>
          <Input id="dec-reason" label={t.procurement.reqs.decisionReason} hint={t.procurement.reqs.decisionReasonHint} value={dReason} onChange={(e) => setDReason(e.target.value)} />
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busyId !== null && decision !== null && busyId === decision.id} disabled={busyId !== null}>{t.procurement.reqs.decisionConfirm}</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
}
