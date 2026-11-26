"use client";

import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  getSalesReturn,
  receiveReturn,
  type SalesReturnRow,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { hasPermission, salesActionError, salesTone, salesStatusLabel, SalesMeError, useSalesMe } from "../../_lib/helpers";
import { localStatus } from "../../_lib/deferred";
import { fmtDate, fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";

export default function ReturnDetailPage() {
  const params = useParams<{ locale: string; id: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const returnId = params.id;
  const t = getDictionary(locale);
  const td = t.sales.returns.detail;
  const tl = t.sales.returns;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady, meFailed, retryMe } = useSalesMe(token);

  const [row, setRow] = useState<SalesReturnRow | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "notFound" | "success">("loading");

  const [receiveOpen, setReceiveOpen] = useState(false);
  const [receiveQty, setReceiveQty] = useState("");
  const [receiveError, setReceiveError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [modalError, setModalError] = useState<string | null>(null);
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  function pushToast(tone: ToastItem["tone"], message: string) {
    setToasts((prev) => [...prev, { id: crypto.randomUUID(), tone, message }]);
  }
  function dismissToast(id: string) {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await getSalesReturn(token, returnId);
    if (res.ok) {
      setRow(res.data);
      setPhase("success");
    } else if (res.code === "RETURN_NOT_FOUND" || res.code === "HTTP_404") {
      setRow(null);
      setPhase("notFound");
    } else {
      setRow(null);
      setPhase("error");
    }
  }, [token, returnId]);
  useEffect(() => { void load(); }, [load]);

  async function submitReceive(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const qty = Number(receiveQty);
    if (!Number.isFinite(qty) || qty <= 0) {
      setReceiveError(td.receiveQtyInvalid);
      return;
    }
    setBusy(true);
    setModalError(null);
    const res = await receiveReturn(token, returnId, qty);
    setBusy(false);
    if (!res.ok) {
      setModalError(salesActionError(t, res.code, res.message));
      return;
    }
    setReceiveOpen(false);
    setReceiveQty("");
    setReceiveError(null);
    pushToast("success", td.receivedNote);
    await load();
  }

  const writeGate = hasPermission(permissions, "returns:write");

  if (!ready || (phase === "loading" && row === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "notFound") return <EmptyState title={td.notFound} />;
  if (phase === "error" || row === null) return <ErrorState message={td.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const openQty = row.authorizedQty - row.receivedQty;
  return (
    <div className="stack">
      <div className="page-header">
        <p className="t-caption"><Link href={"/" + locale + "/sales/returns"}>{td.backToList}</Link></p>
        <h1 className="t-display">{td.returnLabel} <span className="mono">{row.id}</span></h1>
        <p className="t-caption">{tl.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}

      <section className="card">
        <div className="form-grid">
          <p><span className="t-caption">{td.orderLabel}: </span><span className="mono">{row.salesOrderId}</span></p>
          <p><span className="t-caption">{td.customerLabel}: </span><span className="mono">{row.customerId}</span></p>
          <p><span className="t-caption">{td.status}: </span><Badge tone={salesTone(row.status)}>{localStatus(t, tl.statusLabels, row.status)}</Badge></p>
          <p><span className="t-caption">{td.reason}: </span>{row.reason}</p>
          <p><span className="t-caption">{td.authorized}: </span><span className="mono">{fmtNumber(row.authorizedQty, locale)}</span></p>
          <p><span className="t-caption">{td.received}: </span><span className="mono">{fmtNumber(row.receivedQty, locale)}</span></p>
        </div>
        {row.status === "Completed" ? (
          <p className="banner" role="status">{td.completedNote}</p>
        ) : (
          <p><Button disabled={!meReady || writeGate === false || openQty <= 0} onClick={() => { setReceiveOpen(true); setModalError(null); setReceiveQty(String(openQty)); }}>{td.receive}</Button></p>
        )}
      </section>

      <section className="card">
        <h2 className="t-title">{td.linesTitle}</h2>
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{td.lineColumns.sku}</th>
                <th scope="col">{td.lineColumns.qty}</th>
                <th scope="col">{td.lineColumns.reason}</th>
                <th scope="col">{td.lineColumns.condition}</th>
              </tr>
            </thead>
            <tbody>
              {row.lines.map((ln, i) => (
                <tr key={ln.itemSku + "-" + i}>
                  <td data-label={td.lineColumns.sku} className="mono">{ln.itemSku}</td>
                  <td data-label={td.lineColumns.qty} className="mono">{fmtNumber(ln.qty, locale)}</td>
                  <td data-label={td.lineColumns.reason}>{ln.reason}</td>
                  <td data-label={td.lineColumns.condition} className="mono">{ln.condition}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{td.receiptsTitle}</h2>
        {row.receipts.length === 0 ? (
          <p className="t-caption">{td.noReceipts}</p>
        ) : (
          <div className="table-scroll">
            <table className="table table--lines">
              <thead>
                <tr>
                  <th scope="col">{td.receiptColumns.id}</th>
                  <th scope="col">{td.receiptColumns.qty}</th>
                  <th scope="col">{td.receiptColumns.by}</th>
                  <th scope="col">{td.receiptColumns.at}</th>
                </tr>
              </thead>
              <tbody>
                {row.receipts.map((rc) => (
                  <tr key={rc.id}>
                    <td data-label={td.receiptColumns.id} className="mono">{rc.id}</td>
                    <td data-label={td.receiptColumns.qty} className="mono">{fmtNumber(rc.receivedQty, locale)}</td>
                    <td data-label={td.receiptColumns.by} className="mono">{rc.receivedBy}</td>
                    <td data-label={td.receiptColumns.at}><span className="t-caption">{fmtDate(rc.receivedAt, locale)}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <Modal open={receiveOpen} onClose={() => setReceiveOpen(false)} title={td.receiveConfirmTitle} closeLabel={t.common.cancel}>
        <p>{td.receiveConfirm}</p>
        <form onSubmit={(e) => void submitReceive(e)}>
          <Input id="rtn-receive-qty" label={td.receiveQty} type="number" min="0" step="any" value={receiveQty} onChange={(e) => setReceiveQty(e.target.value)} error={receiveError ?? undefined} />
          {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setReceiveOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{td.receive}</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
}
