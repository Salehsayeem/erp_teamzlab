"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  fetchRequisitionDetail, decideRequisition, flagRequisitionException,
  type RequisitionDetail,
} from "@/lib/services/procurement";
import { fmtDate, fmtMoney } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";

export default function RequisitionDetailPage() {
  const params = useParams<{ locale: string; id: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [detail, setDetail] = useState<RequisitionDetail | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [exCode, setExCode] = useState("");
  const [exNarrative, setExNarrative] = useState("");
  const [exEvidence, setExEvidence] = useState("");
  const [dKind, setDKind] = useState("Approve");
  const [dReason, setDReason] = useState("");
  const [decisionOpen, setDecisionOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchRequisitionDetail(token, params.id);
    if (res.ok) { setDetail(res.data); setPhase("success"); }
    else { setDetail(null); setPhase("error"); }
  }, [token, params.id]);
  useEffect(() => { void load(); }, [load]);

  async function run(fn: () => Promise<{ ok: true } | { ok: false; message: string }>) {
    setBusy(true);
    setErr(null);
    const res = await fn();
    setBusy(false);
    if (!res.ok) { setErr(`${t.procurement.common.actionFailed}: ${res.message}`); return false; }
    await load();
    return true;
  }

  async function onException(e: React.FormEvent) {
    e.preventDefault();
    if (!token || !detail) return;
    const ok = await run(() => flagRequisitionException(token, detail.requisition.id, {
      reasonCode: exCode, narrative: exNarrative, evidenceRef: exEvidence || null,
    }));
    if (ok) { setExCode(""); setExNarrative(""); setExEvidence(""); }
  }

  async function onDecide(e: React.FormEvent) {
    e.preventDefault();
    if (!decisionOpen || !detail) return;
    const ok = await run(() => decideRequisition(token ?? "", detail.requisition.id, {
      decision: dKind, version: detail.step?.version ?? 1, reason: dReason.trim() || null,
    }));
    if (ok) setDecisionOpen(false);
  }

  const statusLabel = (s: string) => (t.common.status as Record<string, string>)[s] ?? s;
  const r = detail?.requisition;
  const step = detail?.step;

  if (!ready || (phase === "loading" && detail === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error" || !detail || !r) return <ErrorState message={t.procurement.reqs.detailLoadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display mono">{r.id}</h1>
        <p className="t-caption">
          <Link href={`/${locale}/procurement/requisitions`} className="mono">{t.procurement.receipts.back.replace(t.procurement.receipts.title, t.procurement.reqs.title)}</Link>
          {" · "}
          {t.procurement.reqs.step}: {step ? `${statusLabel(r.status)} · v${r.version} · ${t.procurement.reqs.ruleVersion} ${step.ruleVersion ?? "—"}` : statusLabel(r.status)}
        </p>
      </div>
      {err ? <p className="banner" role="alert">{err}</p> : null}
      {r.exceptionCode ? (
        <p className="banner"><Badge tone="warning">{t.procurement.reqs.exceptionFlagged}</Badge> {r.exceptionCode}: {r.exceptionNarrative}</p>
      ) : null}

      <section className="card">
        <h2 className="t-title">{t.procurement.reqs.lines}</h2>
        {detail.lines.length === 0 ? <EmptyState title={t.procurement.reqs.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{t.procurement.reqs.line}</th>
                  <th scope="col">{t.procurement.reqs.description}</th>
                  <th scope="col">{t.procurement.reqs.qty}</th>
                  <th scope="col">{t.procurement.reqs.baseQty}</th>
                  <th scope="col">{t.procurement.reqs.unitPrice}</th>
                  <th scope="col">{t.procurement.reqs.needBy}</th>
                </tr>
              </thead>
              <tbody>
                {detail.lines.map((l, i) => (
                  <tr key={l.id}>
                    <td className="mono">{i + 1}</td>
                    <td>{l.description}</td>
                    <td className="mono">{l.qty} {l.unit}</td>
                    <td className="mono">{l.baseQty} {l.baseUnit}</td>
                    <td className="mono">{fmtMoney(l.unitPrice, locale, r.currency ?? "BDT")}</td>
                    <td><span className="t-caption">{fmtDate(l.needBy, locale)}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <p className="t-caption mono">{t.procurement.reqs.unitPrice} {fmtMoney(r.total, locale, r.currency ?? "BDT")} · {t.procurement.reqs.budget}: {detail.budget ? `${t.procurement.reqs.budgetReserved} ${fmtMoney(detail.budget.amount, locale, detail.budget.currency)}` : t.procurement.reqs.noBudget}</p>
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.reqs.history}</h2>
        {detail.decisions.length === 0 ? <EmptyState title={t.procurement.reqs.pending} /> : (
          <ul className="stack">
            {detail.decisions.map((d, i) => (
              <li key={`${d.actorId}-${d.at}-${i}`}>
                <strong className="mono">{d.decision}</strong> · {d.actorId}
                {d.onBehalfOfDelegatorId ? ` ${t.procurement.reqs.onBehalfOf} ${d.onBehalfOfDelegatorId}` : ""}
                {d.reason ? ` — ${d.reason}` : ""}
                <span className="t-caption"> · {fmtDate(d.at, locale)}</span>
              </li>
            ))}
          </ul>
        )}
        {step && step.decision === null ? (
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button small onClick={() => { setDKind("Approve"); setDReason(""); setDecisionOpen(true); }}>{t.procurement.reqs.approve}</Button>
            <Button small variant="danger" onClick={() => { setDKind("Reject"); setDReason(""); setDecisionOpen(true); }}>{t.procurement.reqs.reject}</Button>
            <Button small variant="secondary" onClick={() => { setDKind("RequestInfo"); setDReason(""); setDecisionOpen(true); }}>{t.procurement.reqs.requestInfo}</Button>
          </div>
        ) : null}
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.reqs.exception}</h2>
        <form onSubmit={(e) => void onException(e)} className="stack">
          <div className="form-grid">
            <Input id="ex-code" label={t.procurement.reqs.exceptionCode} value={exCode} onChange={(e) => setExCode(e.target.value)} required />
            <Input id="ex-evi" label={t.procurement.reqs.exceptionEvidence} value={exEvidence} onChange={(e) => setExEvidence(e.target.value)} />
          </div>
          <Input id="ex-narr" label={t.procurement.reqs.exceptionNarrative} value={exNarrative} onChange={(e) => setExNarrative(e.target.value)} required />
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy || detail.requisition.status === "Approved"}>{t.procurement.reqs.exception}</Button>
            <Button type="button" small variant="secondary" onClick={() => { setDKind("Return"); setDReason(""); setDecisionOpen(true); }}>{t.procurement.reqs.return}</Button>
          </div>
        </form>
      </section>

      <Modal open={decisionOpen} onClose={() => setDecisionOpen(false)} title={t.procurement.reqs.decision} closeLabel={t.common.cancel}>
        <form onSubmit={(e) => void onDecide(e)} className="stack">
          <div className="field">
            <label htmlFor="d-kind">{t.procurement.reqs.decision}</label>
            <select id="d-kind" className="input" value={dKind} onChange={(e) => setDKind(e.target.value)}>
              {["Approve", "Reject", "Return", "RequestInfo"].map((k) => <option key={k} value={k}>{k}</option>)}
            </select>
          </div>
          <Input id="d-reason" label={t.procurement.reqs.decisionReason} hint={t.procurement.reqs.decisionReasonHint} value={dReason} onChange={(e) => setDReason(e.target.value)} />
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.reqs.decisionConfirm}</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
}
