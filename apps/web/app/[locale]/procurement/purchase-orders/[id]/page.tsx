"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  fetchPoDetail, fetchPoChanges, createPoChange, decidePoChange, createPoAck, sendDispatch,
  type PurchaseOrderDetail, type PoChange, type PoAck, type DispatchState,
} from "@/lib/services/procurement";
import { fmtDate, fmtMoney } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";

export default function PurchaseOrderDetailPage() {
  const params = useParams<{ locale: string; id: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [po, setPo] = useState<PurchaseOrderDetail | null>(null);
  const [changes, setChanges] = useState<PoChange[] | null>(null);
  const [acks, setAcks] = useState<PoAck[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [rationale, setRationale] = useState("");
  const [chgQty, setChgQty] = useState("");
  const [chgPrice, setChgPrice] = useState("");
  const [ackStatus, setAckStatus] = useState("Accepted");
  const [ackVersion, setAckVersion] = useState("");
  const [dispatchState, setDispatchState] = useState("Sent");
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [d, c] = await Promise.all([fetchPoDetail(token, params.id), fetchPoChanges(token, params.id)]);
    if (d.ok && c.ok) { setPo(d.data); setChanges(c.data); setPhase("success"); }
    else { setPo(null); setPhase("error"); }
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

  async function onChange(e: React.FormEvent) {
    e.preventDefault();
    if (!token || !po) return;
    const qty = chgQty ? Number(chgQty) : null;
    const price = chgPrice ? Number(chgPrice) : null;
    if (!rationale.trim()) return;
    if (qty !== null && !Number.isFinite(qty)) return;
    if (price !== null && !Number.isFinite(price)) return;
    const ok = await run(() => createPoChange(token, po.id, {
      rationale: rationale.trim(), supplierId: null,
      lines: [{ itemSku: po.lines[0]?.itemSku ?? "", qty, unitPrice: price }],
    }));
    if (ok) { setRationale(""); setChgQty(""); setChgPrice(""); }
  }

  async function onAck(e: React.FormEvent) {
    e.preventDefault();
    if (!token || !po) return;
    const version = Number(ackVersion || String(po.version));
    if (!Number.isFinite(version)) return;
    const ok = await run(() => createPoAck(token, po.id, {
      supplierId: po.supplierId, orderVersion: version,
      lines: po.lines.map((l) => ({ itemSku: l.itemSku, status: ackStatus })),
    }));
    if (ok) setAckVersion("");
  }

  async function onDispatch(e: React.FormEvent) {
    e.preventDefault();
    if (!token || !po) return;
    const ok = await run(() => sendDispatch(token, po.id, {
      orderVersion: po.version, state: dispatchState as DispatchState, detail: null,
    }));
    if (ok) setDispatchState("Sent");
  }

  const statusLabel = (s: string) => (t.common.status as Record<string, string>)[s] ?? s;

  if (!ready || (phase === "loading" && po === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error" || !po) return <ErrorState message={t.po.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display mono">{po.id}</h1>
        <p className="t-caption">
          <Badge tone={statusTone(po.status)}>{statusLabel(po.status)}</Badge>
          {" · v"}{po.version} · {po.supplierId} · {fmtMoney(po.total, locale, "BDT")}
        </p>
      </div>
      {err ? <p className="banner" role="alert">{err}</p> : null}

      <section className="card">
        <h2 className="t-title">{t.po.title}</h2>
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{t.po.line.sku}</th>
                <th scope="col">{t.po.line.description}</th>
                <th scope="col">{t.po.line.qty}</th>
                <th scope="col">{t.po.line.unitPrice}</th>
              </tr>
            </thead>
            <tbody>
              {po.lines.map((l) => (
                <tr key={l.itemSku}>
                  <td className="mono">{l.itemSku}</td>
                  <td>{l.description}</td>
                  <td className="mono">{l.qty}</td>
                  <td className="mono">{fmtMoney(l.unitPrice, locale, "BDT")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.receipts.changes}</h2>
        {changes === null || changes.length === 0 ? <EmptyState title={t.procurement.reqs.empty} /> : (
          <ul className="stack">
            {changes.map((c) => (
              <li key={c.id}>
                <strong className="mono">{c.id}</strong> · {c.status} · v{c.baseOrderVersion} →
                {" "}<span className="t-caption">{c.rationale} · {c.requestedBy}</span>
                {c.status === "Pending" ? (
                  <span className="row-actions" style={{ justifyContent: "flex-start" }}>
                    <Button small loading={busy} disabled={busy} onClick={() => void run(() => decidePoChange(token ?? "", po.id, c.id, true))}>{t.po.approve}</Button>
                    <Button small variant="danger" loading={busy} disabled={busy} onClick={() => void run(() => decidePoChange(token ?? "", po.id, c.id, false))}>{t.po.reject}</Button>
                  </span>
                ) : null}
              </li>
            ))}
          </ul>
        )}
        <form onSubmit={(e) => void onChange(e)} className="stack" style={{ marginTop: "1rem" }}>
          <Input id="chg-rationale" label={t.procurement.reqs.decisionReason} value={rationale} onChange={(e) => setRationale(e.target.value)} required />
          <div className="form-grid">
            <Input id="chg-qty" label={t.po.line.qty} type="number" min="0" step="any" value={chgQty} onChange={(e) => setChgQty(e.target.value)} />
            <Input id="chg-price" label={t.po.line.unitPrice} type="number" min="0" step="any" value={chgPrice} onChange={(e) => setChgPrice(e.target.value)} />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.receipts.changes}</Button>
          </div>
        </form>
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.receipts.acks}</h2>
        <form onSubmit={(e) => void onAck(e)} className="stack">
          <div className="form-grid">
            <Input id="ack-ver" label={t.procurement.receipts.asnVersion} type="number" min="1" value={ackVersion} onChange={(e) => setAckVersion(e.target.value)} />
            <div className="field">
              <label htmlFor="ack-status">{t.procurement.common.status}</label>
              <select id="ack-status" className="input" value={ackStatus} onChange={(e) => setAckStatus(e.target.value)}>
                {["Accepted", "Discrepancy", "Rejected"].map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            </div>
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.receipts.acks}</Button>
          </div>
        </form>
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.receipts.dispatch}</h2>
        <form onSubmit={(e) => void onDispatch(e)} className="stack">
          <div className="form-grid">
            <div className="field">
              <label htmlFor="dsp-state">{t.procurement.receipts.dispatchState}</label>
              <select id="dsp-state" className="input" value={dispatchState} onChange={(e) => setDispatchState(e.target.value)}>
                {(["Sent", "Failed", "Unknown"] as DispatchState[]).map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            </div>
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.receipts.dispatch}</Button>
          </div>
        </form>
      </section>
    </div>
  );
}

