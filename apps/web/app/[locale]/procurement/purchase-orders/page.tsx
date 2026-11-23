"use client";

import { Fragment, useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchPurchaseOrders, submitPo, approvePo, postReceipt, type PurchaseOrder } from "@/lib/services/api";
import { fmtDate, fmtMoney } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";
type RowAction = "reject" | "receive";

export default function PurchaseOrdersPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [pos, setPos] = useState<PurchaseOrder[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [openAction, setOpenAction] = useState<{ id: string; action: RowAction } | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [receiptSku, setReceiptSku] = useState("");
  const [receiptQty, setReceiptQty] = useState("");
  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchPurchaseOrders(token);
    if (res.ok) {
      setPos(res.data);
      setPhase("success");
    } else {
      setPos(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function act(id: string, fn: () => Promise<{ ok: true; data: unknown } | { ok: false; code: string; message: string }>) {
    setBusyId(id);
    setActionError(null);
    const res = await fn();
    setBusyId(null);
    if (!res.ok) {
      setActionError(res.code === "NETWORK" ? t.common.networkError : `${t.po.actionFailed}: ${res.message}`);
      return false;
    }
    await load();
    return true;
  }

  async function receive(e: FormEvent, po: PurchaseOrder) {
    e.preventDefault();
    if (!token || !receiptSku) return;
    const qty = Number(receiptQty);
    if (!Number.isFinite(qty) || qty <= 0) return;
    const ok = await act(po.id, () => postReceipt(token, { purchaseOrderId: po.id, itemSku: receiptSku, qty }));
    if (ok) {
      setOpenAction(null);
      setReceiptSku("");
      setReceiptQty("");
    }
  }

  function statusLabel(s: string): string {
    const labels = t.common.status as Record<string, string>;
    return labels[s] ?? s;
  }

  if (!ready || (phase === "loading" && pos === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={t.po.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  const posList = pos ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.po.title}</h1>
        <p className="t-caption">{t.po.subtitle}</p>
      </div>
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}
      <section className="card">
        {posList.length === 0 ? <EmptyState title={t.po.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{t.po.id}</th>
                  <th scope="col">{t.po.supplier}</th>
                  <th scope="col">{t.po.status}</th>
                  <th scope="col">{t.po.total}</th>
                  <th scope="col">{t.po.created}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {posList.map((po) => (
                  <Fragment key={po.id}>
                    <tr>
                      <td data-label={t.po.id} className="mono">{po.id}</td>
                      <td data-label={t.po.supplier}>{po.supplierId}</td>
                      <td data-label={t.po.status}><Badge tone={statusTone(po.status)}>{statusLabel(po.status)}</Badge></td>
                      <td data-label={t.po.total} className="mono">{fmtMoney(po.total, locale, "BDT")}</td>
                      <td data-label={t.po.created}><span className="t-caption">{fmtDate(po.createdAt, locale)}</span></td>
                      <td data-label={t.journals.actions}>
                        <div className="row-actions">
                          <Button small variant="secondary" aria-expanded={expandedId === po.id} onClick={() => setExpandedId(expandedId === po.id ? null : po.id)}>
                            {expandedId === po.id ? t.po.hideLines : t.po.showLines}
                          </Button>
                          {po.status === "Draft" ? (
                            <Button small loading={busyId === po.id} disabled={busyId !== null} onClick={() => void act(po.id, () => submitPo(token ?? "", po.id))}>
                              {t.po.submit}
                            </Button>
                          ) : null}
                          {po.status === "PendingApproval" ? (
                            <>
                              <Button small loading={busyId === po.id} disabled={busyId !== null} onClick={() => void act(po.id, () => approvePo(token ?? "", po.id, null))}>
                                {t.po.approve}
                              </Button>
                              <Button small variant="danger" onClick={() => { setOpenAction({ id: po.id, action: "reject" }); setRejectReason(""); }}>
                                {t.po.reject}
                              </Button>
                            </>
                          ) : null}
                          {po.status === "Approved" || po.status === "Sent" ? (
                            <Button small onClick={() => { setOpenAction({ id: po.id, action: "receive" }); setReceiptSku(po.lines[0]?.itemSku ?? ""); setReceiptQty(""); }}>
                              {t.po.receive}
                            </Button>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                    {expandedId === po.id ? (
                      <tr className="sub-row">
                        <td colSpan={6}>
                          <table className="table table--lines">
                            <thead>
                              <tr>
                                <th scope="col">{t.po.line.sku}</th>
                                <th scope="col">{t.po.line.description}</th>
                                <th scope="col">{t.po.line.qty}</th>
                                <th scope="col">{t.po.line.unitPrice}</th>
                                <th scope="col">{t.po.line.amount}</th>
                              </tr>
                            </thead>
                            <tbody>
                              {po.lines.map((line) => (
                                <tr key={line.id}>
                                  <td data-label={t.po.line.sku} className="mono">{line.itemSku}</td>
                                  <td data-label={t.po.line.description}>{line.description}</td>
                                  <td data-label={t.po.line.qty} className="mono">{line.qty}</td>
                                  <td data-label={t.po.line.unitPrice} className="mono">{fmtMoney(line.unitPrice, locale, "BDT")}</td>
                                  <td data-label={t.po.line.amount} className="mono">{fmtMoney(line.qty * line.unitPrice, locale, "BDT")}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </td>
                      </tr>
                    ) : null}
                    {openAction?.id === po.id && openAction.action === "reject" ? (
                      <tr className="sub-row">
                        <td colSpan={6}>
                          <div className="stack">
                            <Input id={`reject-${po.id}`} label={t.po.rejectionReason} value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} />
                            <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                              <Button small loading={busyId === po.id} disabled={busyId !== null} onClick={() => void act(po.id, () => approvePo(token ?? "", po.id, rejectReason.trim() || null)).then((ok) => { if (ok) setOpenAction(null); })}>
                                {t.po.confirmReject}
                              </Button>
                              <Button small variant="secondary" onClick={() => setOpenAction(null)}>{t.common.cancel}</Button>
                            </div>
                          </div>
                        </td>
                      </tr>
                    ) : null}
                    {openAction?.id === po.id && openAction.action === "receive" ? (
                      <tr className="sub-row">
                        <td colSpan={6}>
                          <form onSubmit={(e) => void receive(e, po)}>
                            <div className="form-grid">
                              <div className="field">
                                <label htmlFor={`sku-${po.id}`}>{t.po.itemSku}</label>
                                <select id={`sku-${po.id}`} className="input" value={receiptSku} onChange={(e) => setReceiptSku(e.target.value)}>
                                  {po.lines.map((line) => (
                                    <option key={line.id} value={line.itemSku}>{line.itemSku} — {line.description}</option>
                                  ))}
                                </select>
                              </div>
                              <Input id={`qty-${po.id}`} label={t.po.qty} type="number" min="1" step="1" value={receiptQty} onChange={(e) => setReceiptQty(e.target.value)} />
                            </div>
                            <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                              <Button type="submit" small loading={busyId === po.id} disabled={busyId !== null || !receiptSku || !receiptQty}>{t.po.postReceipt}</Button>
                              <Button type="button" small variant="secondary" onClick={() => setOpenAction(null)}>{t.common.cancel}</Button>
                            </div>
                          </form>
                        </td>
                      </tr>
                    ) : null}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
