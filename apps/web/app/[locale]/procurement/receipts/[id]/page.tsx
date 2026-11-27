"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  fetchReceiptDetail, createInspection, disposeInspection, createSupplierReturn, createSupplierClaim,
  type ReceiptDetail, type InspectionStatus, type ClaimKind,
} from "@/lib/services/procurement";
import { fmtDate, fmtMoney } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";
type DispositionForm = { id: string };

export default function ReceiptDetailPage() {
  const params = useParams<{ locale: string; id: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [detail, setDetail] = useState<ReceiptDetail | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [insQty, setInsQty] = useState("");
  const [dispFor, setDispFor] = useState<DispositionForm | null>(null);
  const [dStatus, setDStatus] = useState("Partial");
  const [dAcc, setDAcc] = useState("");
  const [dRej, setDRej] = useState("");
  const [dDefect, setDDefect] = useState("");
  const [dEvidence, setDEvidence] = useState("");
  const [retQty, setRetQty] = useState("");
  const [retReason, setRetReason] = useState("");
  const [claimKind, setClaimKind] = useState("Replacement");
  const [claimAmount, setClaimAmount] = useState("");
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchReceiptDetail(token, params.id);
    if (res.ok) { setDetail(res.data); setPhase("success"); }
    else { setDetail(null); setPhase("error"); }
  }, [token, params.id]);
  useEffect(() => { void load(); }, [load]);

  async function run(fn: () => Promise<{ ok: true } | { ok: false; message: string }>) {
    setBusy(true); setErr(null);
    const res = await fn();
    setBusy(false);
    if (!res.ok) { setErr(`${t.procurement.common.actionFailed}: ${res.message}`); return false; }
    await load();
    return true;
  }

  async function onInspect(e: React.FormEvent) {
    e.preventDefault();
    if (!token || !detail) return;
    const q = Number(insQty);
    if (!Number.isFinite(q) || q <= 0) return;
    const ok = await run(() => createInspection(token ?? "", detail.receipt.id, {
      itemSku: detail.receipt.itemSku, qtyReceived: q,
    }));
    if (ok) setInsQty("");
  }

  async function onDispose(e: React.FormEvent) {
    e.preventDefault();
    if (!dispFor || !detail) return;
    const acc = Number(dAcc); const rej = Number(dRej);
    if (!Number.isFinite(acc) || acc < 0 || !Number.isFinite(rej) || rej < 0) return;
    const ok = await run(() => disposeInspection(token ?? "", detail.receipt.id, dispFor.id, {
      status: dStatus as InspectionStatus, qtyAccepted: acc, qtyRejected: rej,
      defectCode: dDefect || null, evidenceRef: dEvidence || null,
    }));
    if (ok) { setDispFor(null); setDAcc(""); setDRej(""); setDDefect(""); setDEvidence(""); }
  }

  async function onReturn(e: React.FormEvent) {
    e.preventDefault();
    if (!detail) return;
    const q = Number(retQty);
    if (!Number.isFinite(q) || q <= 0 || !retReason.trim()) return;
    const ok = await run(() => createSupplierReturn(token ?? "", {
      receiptId: detail.receipt.id, poId: detail.receipt.purchaseOrderId, itemSku: detail.receipt.itemSku,
      qty: q, reason: retReason.trim(), kind: claimKind as ClaimKind,
    }));
    if (ok) { setRetQty(""); setRetReason(""); }
  }

  async function onClaim(e: React.FormEvent) {
    e.preventDefault();
    if (!detail) return;
    const amount = claimAmount ? Number(claimAmount) : null;
    const ok = await run(() => createSupplierClaim(token ?? "", {
      receiptId: detail.receipt.id, poId: detail.receipt.purchaseOrderId,
      kind: claimKind as ClaimKind, amount,
    }));
    if (ok) setClaimAmount("");
  }

  const statusLabel = (s: string) => (t.common.status as Record<string, string>)[s] ?? s;

  if (!ready || (phase === "loading" && detail === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error" || !detail) return <ErrorState message={t.procurement.receipts.detailLoadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  const r = detail.receipt;
  const b = detail.balances;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display mono">{r.id}</h1>
        <p className="t-caption">
          <Link href={`/${locale}/procurement/receipts`}>{t.procurement.receipts.back}</Link>
          {" · "}{r.purchaseOrderId} · {r.itemSku} · {r.qty} · {t.procurement.receipts.receivedBy} {r.receivedBy}
        </p>
      </div>
      {err ? <p className="banner" role="alert">{err}</p> : null}

      <section className="card">
        <h2 className="t-title">{t.procurement.receipts.balances}</h2>
        {b ? (
          <div className="form-grid">
            <p className="t-caption mono">{t.procurement.receipts.ordered}: {b.ordered}</p>
            <p className="t-caption mono">{t.procurement.receipts.receivedTotal}: {b.received}</p>
            <p className="t-caption mono">{t.procurement.receipts.rejected}: {b.rejected}</p>
            <p className="t-caption mono">{t.procurement.receipts.accepted}: {b.accepted}</p>
            <p className="t-caption mono">{t.procurement.receipts.remaining}: {b.remaining}</p>
            <p className="t-caption mono">{t.procurement.receipts.calcAt}: {fmtDate(b.calculatedAt, locale)}</p>
          </div>
        ) : <EmptyState title={t.procurement.receipts.empty} />}
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.receipts.inspections}</h2>
        {detail.inspections.length === 0 ? <EmptyState title={t.procurement.receipts.inspectionsEmpty} /> : (
          <ul className="stack">
            {detail.inspections.map((i) => (
              <li key={i.id}>
                <strong className="mono">{i.id}</strong> · {i.status} · {i.qtyReceived} ({i.qtyAccepted} / {i.qtyRejected})
                {i.defectCode ? ` · ${i.defectCode}` : ""} · {i.dispositionedBy ?? t.procurement.receipts.sodNote}
                {i.status === "Pending" ? (
                  <span className="row-actions" style={{ justifyContent: "flex-start" }}>
                    <Button small onClick={() => { setDispFor({ id: i.id }); setDStatus("Partial"); setDAcc(""); setDRej(""); setErr(null); }}>
                      {t.procurement.receipts.dispose}
                    </Button>
                  </span>
                ) : null}
              </li>
            ))}
          </ul>
        )}
        <form onSubmit={(e) => void onInspect(e)} className="stack" style={{ marginTop: "1rem" }}>
          <Input id="ins-qty" label={t.procurement.receipts.qtyReceived} type="number" min="0" step="any" value={insQty} onChange={(e) => setInsQty(e.target.value)} required />
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.receipts.newInspection}</Button>
          </div>
        </form>
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.receipts.returns}</h2>
        {detail.returns.length === 0 ? <EmptyState title={t.procurement.receipts.returnsEmpty} /> : (
          <ul className="stack">
            {detail.returns.map((ret) => (
              <li key={ret.id}>
                <strong className="mono">{ret.id}</strong> · {ret.kind} · {ret.qty} — {ret.reason}
              </li>
            ))}
          </ul>
        )}
        <form onSubmit={(e) => void onReturn(e)} className="stack" style={{ marginTop: "1rem" }}>
          <div className="form-grid">
            <Input id="ret-qty" label={t.procurement.receipts.returnQty} type="number" min="0" step="any" value={retQty} onChange={(e) => setRetQty(e.target.value)} required />
            <Input id="ret-reason" label={t.procurement.receipts.returnReason} value={retReason} onChange={(e) => setRetReason(e.target.value)} required />
            <div className="field">
              <label htmlFor="ret-kind">{t.procurement.receipts.returnKind}</label>
              <select id="ret-kind" className="input" value={claimKind} onChange={(e) => setClaimKind(e.target.value)}>
                {["Replacement", "Credit"].map((k) => <option key={k} value={k}>{k}</option>)}
              </select>
            </div>
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.receipts.newReturn}</Button>
          </div>
        </form>
        <form onSubmit={(e) => void onClaim(e)} className="stack" style={{ marginTop: "1rem" }}>
          <div className="form-grid">
            <div className="field">
              <label htmlFor="cl-kind">{t.procurement.receipts.claimKind}</label>
              <select id="cl-kind" className="input" value={claimKind} onChange={(e) => setClaimKind(e.target.value)}>
                {["Replacement", "Credit"].map((k) => <option key={k} value={k}>{k}</option>)}
              </select>
            </div>
            <Input id="cl-amt" label={t.procurement.receipts.claimAmount} type="number" min="0" step="any" value={claimAmount} onChange={(e) => setClaimAmount(e.target.value)} />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.receipts.claim}</Button>
          </div>
        </form>
      </section>

      <Modal open={dispFor !== null} onClose={() => setDispFor(null)} title={t.procurement.receipts.dispose} closeLabel={t.common.cancel}>
        <form onSubmit={(e) => void onDispose(e)} className="stack">
          <p className="t-caption">{t.procurement.receipts.sodNote}</p>
          {/* the page-level banner sits behind the scrim — failures must render in-view (house modal pattern) */}
          {dispFor !== null && err ? <p className="banner" role="alert">{err}</p> : null}
          <div className="form-grid">
            <div className="field">
              <label htmlFor="d-status">{t.procurement.receipts.dispositionStatus}</label>
              <select id="d-status" className="input" value={dStatus} onChange={(e) => setDStatus(e.target.value)}>
                {["Partial", "Accepted", "Rejected"].map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            </div>
            {/* No native `required` here: these are the only modal inputs in the app
                using it, and an empty qty means 0 (onDispose reads Number("")). A
                required-but-empty d-rej made constraint validation swallow the submit
                with no request and no app-level message (clickthrough FINDING-5). */}
            <Input id="d-acc" label={t.procurement.receipts.qtyAccept} type="number" min="0" step="any" value={dAcc} onChange={(e) => setDAcc(e.target.value)} />
            <Input id="d-rej" label={t.procurement.receipts.qtyReject} type="number" min="0" step="any" value={dRej} onChange={(e) => setDRej(e.target.value)} />
            <Input id="d-defect" label={t.procurement.receipts.defectCode} value={dDefect} onChange={(e) => setDDefect(e.target.value)} />
            <Input id="d-evi" label={t.procurement.receipts.evidence} value={dEvidence} onChange={(e) => setDEvidence(e.target.value)} />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.receipts.dispositionConfirm}</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
}
