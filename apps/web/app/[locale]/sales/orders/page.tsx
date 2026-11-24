"use client";

import Link from "next/link";
import { Fragment, useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  approveSalesOrder,
  createSalesOrder,
  fetchCustomers,
  fetchSalesOrders,
  releaseCreditHold,
  shipSalesOrder,
  submitSalesOrder,
  type CustomerRow,
  type SalesOrderRow,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { fmtDate, fmtMoney, fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  Select,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import { hasPermission, salesActionError, salesStatusLabel, salesTone, SalesMeError, useSalesMe } from "../_lib/helpers";

type SoLineForm = { itemSku: string; description: string; qty: string; unitPrice: string };
const EMPTY_SO_LINE: SoLineForm = { itemSku: "", description: "", qty: "", unitPrice: "" };

export default function SalesOrdersPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.orders;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady, meFailed, retryMe } = useSalesMe(token);

  const [orders, setOrders] = useState<SalesOrderRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [customers, setCustomers] = useState<CustomerRow[]>([]);

  const [customerId, setCustomerId] = useState("");
  const [lines, setLines] = useState<SoLineForm[]>([{ ...EMPTY_SO_LINE }]);
  const [createErrors, setCreateErrors] = useState<{ customer?: string; line?: string }>({});
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [shipFor, setShipFor] = useState<string | null>(null);
  const [shipItem, setShipItem] = useState("");
  const [shipQty, setShipQty] = useState("");
  const [rejectFor, setRejectFor] = useState<string | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [releaseId, setReleaseId] = useState<string | null>(null);
  const [modalError, setModalError] = useState<string | null>(null);

  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
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
    const res = await fetchSalesOrders(token);
    if (res.ok) {
      setOrders(res.data);
      setPhase("success");
    } else {
      setOrders(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchCustomers(token).then((res) => { if (!cancelled && res.ok) setCustomers(res.data); });
    return () => { cancelled = true; };
  }, [token]);

  function setLine(i: number, patch: Partial<SoLineForm>) {
    setLines((prev) => prev.map((ln, idx) => (idx === i ? { ...ln, ...patch } : ln)));
  }

  async function submitCreate(e: FormEvent) {
    e.preventDefault();
    const errs: { customer?: string; line?: string } = {};
    if (!customerId) errs.customer = tc.customerRequired;
    const parsed = lines.map((ln) => ({ ...ln, qtyNum: Number(ln.qty), priceNum: Number(ln.unitPrice) }));
    if (parsed.length === 0 || parsed.some((ln) => !ln.itemSku.trim())) errs.line = tc.errors.skuRequired;
    else if (parsed.some((ln) => !ln.qty.trim() || !Number.isFinite(ln.qtyNum) || ln.qtyNum <= 0)) errs.line = tc.errors.qtyInvalid;
    else if (parsed.some((ln) => !ln.unitPrice.trim() || !Number.isFinite(ln.priceNum) || ln.priceNum < 0)) errs.line = tc.errors.priceInvalid;
    setCreateErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setCreating(true);
    setCreateError(null);
    const res = await createSalesOrder(token, {
      customerId,
      lines: parsed.map((ln) => ({ itemSku: ln.itemSku.trim(), description: ln.description.trim(), qty: ln.qtyNum, unitPrice: ln.priceNum })),
    });
    setCreating(false);
    if (!res.ok) {
      setCreateError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", `${tc.created} ${res.data.id}`);
    setCustomerId("");
    setLines([{ ...EMPTY_SO_LINE }]);
    await load();
  }

  async function act(id: string, run: () => Promise<{ ok: true; data: unknown } | { ok: false; code: string; message: string }>) {
    setBusyId(id);
    setActionError(null);
    const res = await run();
    setBusyId(null);
    if (!res.ok) {
      setActionError(salesActionError(t, res.code, res.message));
      return false;
    }
    await load();
    return true;
  }

  async function submitShip(e: FormEvent, so: SalesOrderRow) {
    e.preventDefault();
    const qty = Number(shipQty);
    if (!shipItem || !shipQty.trim() || !Number.isFinite(qty) || qty <= 0 || !token) {
      setActionError(t.sales.errors.validation);
      return;
    }
    setBusyId(so.id);
    setActionError(null);
    const res = await shipSalesOrder(token, so.id, shipItem, qty);
    setBusyId(null);
    if (!res.ok) {
      setActionError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", `${tc.shippedNote} ${res.data.shipment.id} (${tc.journalLabel} ${res.data.journal.id})`);
    setShipFor(null);
    setShipItem("");
    setShipQty("");
    await load();
  }

  async function confirmRelease() {
    if (!releaseId || !token) return;
    setBusyId(releaseId);
    setModalError(null);
    const res = await releaseCreditHold(token, releaseId);
    setBusyId(null);
    if (!res.ok) {
      setModalError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.releasedNote);
    setReleaseId(null);
    await load();
  }

  const approveGate = hasPermission(permissions, "so:approve");
  const releaseGate = hasPermission(permissions, "credit:override");

  if (!ready || (phase === "loading" && orders === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const list = orders ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.createTitle}</h2>
        {customers.length === 0 ? <EmptyState title={tc.noCustomers} /> : (
          <form onSubmit={(e) => void submitCreate(e)}>
            <div className="form-grid">
              <Select
                id="so-customer"
                label={tc.customer}
                value={customerId}
                onChange={(e) => setCustomerId(e.target.value)}
                options={customers.map((c) => ({ value: c.id, label: `${c.name} (${c.id})` }))}
                placeholder={tc.customer}
                error={createErrors.customer}
              />
            </div>
            {lines.map((ln, i) => (
              <div className="form-grid" key={i}>
                <Input id={`so-sku-${i}`} label={`${tc.line.sku} ${i + 1}`} value={ln.itemSku} onChange={(e) => setLine(i, { itemSku: e.target.value })} />
                <Input id={`so-desc-${i}`} label={`${tc.line.description} ${i + 1}`} value={ln.description} onChange={(e) => setLine(i, { description: e.target.value })} />
                <Input id={`so-qty-${i}`} label={`${tc.line.qty} ${i + 1}`} type="number" min="0" step="any" value={ln.qty} onChange={(e) => setLine(i, { qty: e.target.value })} />
                <Input id={`so-price-${i}`} label={`${tc.line.unitPrice} ${i + 1}`} type="number" min="0" step="any" value={ln.unitPrice} onChange={(e) => setLine(i, { unitPrice: e.target.value })} />
                <div className="field">
                  <Button type="button" variant="secondary" small aria-label={`${tc.removeLine} ${i + 1}`} disabled={lines.length === 1} onClick={() => setLines((prev) => prev.filter((_, idx) => idx !== i))}>
                    {tc.removeLine}
                  </Button>
                </div>
              </div>
            ))}
            {createErrors.line ? <p className="field__error">{createErrors.line}</p> : null}
            <div className="row-actions" style={{ justifyContent: "flex-start" }}>
              <Button type="button" variant="secondary" small onClick={() => setLines((prev) => [...prev, { ...EMPTY_SO_LINE }])}>{tc.addLine}</Button>
              <Button type="submit" loading={creating}>{tc.create}</Button>
            </div>
          </form>
        )}
      </section>

      <section className="card">
        {list.length === 0 ? <EmptyState title={tc.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{tc.columns.order}</th>
                  <th scope="col">{tc.columns.customer}</th>
                  <th scope="col">{tc.columns.status}</th>
                  <th scope="col">{tc.columns.total}</th>
                  <th scope="col">{tc.columns.approvedBy}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((so) => {
                  const isMaker = meId !== null && so.createdBy === meId;
                  const showApprove = so.status === "PendingApproval" && approveGate !== false && !isMaker;
                  return (
                    <Fragment key={so.id}>
                      <tr>
                        <td data-label={tc.columns.order} className="mono">{so.id}</td>
                        <td data-label={tc.columns.customer} className="mono">{so.customerId}</td>
                        <td data-label={tc.columns.status}><Badge tone={salesTone(so.status)}>{salesStatusLabel(t, so.status)}</Badge></td>
                        <td data-label={tc.columns.total} className="mono">{fmtMoney(so.total, locale, "BDT")}</td>
                        <td data-label={tc.columns.approvedBy} className="mono">{so.approvedBy ?? "—"}</td>
                        <td data-label={t.journals.actions}>
                          <div className="row-actions">
                            <Button small variant="secondary" aria-expanded={expandedId === so.id} onClick={() => setExpandedId(expandedId === so.id ? null : so.id)}>
                              {expandedId === so.id ? tc.hideLines : tc.showLines}
                            </Button>
                            {so.status === "Draft" ? (
                              <Button small loading={busyId === so.id} disabled={busyId !== null} onClick={() => void act(so.id, () => submitSalesOrder(token ?? "", so.id))}>
                                {tc.submit}
                              </Button>
                            ) : null}
                            {showApprove ? (
                              <>
                                <Button small disabled={!meReady || busyId !== null} onClick={() => void act(so.id, () => approveSalesOrder(token ?? "", so.id, null))}>
                                  {tc.approve}
                                </Button>
                                <Button small variant="danger" disabled={!meReady || busyId !== null} onClick={() => { setRejectFor(so.id); setRejectReason(""); setModalError(null); }}>
                                  {tc.reject}
                                </Button>
                              </>
                            ) : null}
                            {so.status === "Confirmed" ? (
                              <Button small onClick={() => { setShipFor(shipFor === so.id ? null : so.id); setShipItem(so.lines[0]?.itemSku ?? ""); setShipQty(""); }}>
                                {tc.ship}
                              </Button>
                            ) : null}
                            {so.status === "Shipped" ? (
                              <Link href={`/${locale}/sales/invoices`}>{tc.draftInvoice}</Link>
                            ) : null}
                            {so.status === "CreditHold" && releaseGate !== false ? (
                              <Button small disabled={!meReady || busyId !== null} onClick={() => setReleaseId(so.id)}>
                                {tc.release}
                              </Button>
                            ) : null}
                          </div>
                        </td>
                      </tr>
                      {so.status === "PendingApproval" && isMaker ? (
                        <tr className="sub-row">
                          <td colSpan={6}><p className="t-caption" role="status">{t.sales.makerChecker}</p></td>
                        </tr>
                      ) : null}
                      {so.status === "CreditHold" ? (
                        <tr className="sub-row">
                          <td colSpan={6}><p className="t-caption" role="status">{tc.creditHoldNote}</p></td>
                        </tr>
                      ) : null}
                      {so.status === "Rejected" && so.rejectionReason ? (
                        <tr className="sub-row">
                          <td colSpan={6}><p className="t-caption" role="status">{tc.rejectedReason} {so.rejectionReason}</p></td>
                        </tr>
                      ) : null}
                      {expandedId === so.id ? (
                        <tr className="sub-row">
                          <td colSpan={6}>
                            <div className="table-scroll">
                              <table className="table table--lines">
                                <thead>
                                  <tr>
                                    <th scope="col">{tc.line.sku}</th>
                                    <th scope="col">{tc.line.description}</th>
                                    <th scope="col">{tc.line.qty}</th>
                                    <th scope="col">{tc.line.unitPrice}</th>
                                    <th scope="col">{t.sales.opportunities.line.amount}</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {so.lines.map((line) => (
                                    <tr key={line.id}>
                                      <td data-label={tc.line.sku} className="mono">{line.itemSku}</td>
                                      <td data-label={tc.line.description}>{line.description}</td>
                                      <td data-label={tc.line.qty} className="mono">{fmtNumber(line.qty, locale)}</td>
                                      <td data-label={tc.line.unitPrice} className="mono">{fmtMoney(line.unitPrice, locale, "BDT")}</td>
                                      <td data-label={t.sales.opportunities.line.amount} className="mono">{fmtMoney(line.qty * line.unitPrice, locale, "BDT")}</td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            </div>
                            <p className="t-caption">{tc.columns.order} <span className="mono">{so.id}</span> · {fmtDate(so.createdAt, locale)}</p>
                          </td>
                        </tr>
                      ) : null}
                      {shipFor === so.id && so.status === "Confirmed" ? (
                        <tr className="sub-row">
                          <td colSpan={6}>
                            <form onSubmit={(e) => void submitShip(e, so)}>
                              <div className="form-grid">
                                <Select
                                  id={`ship-item-${so.id}`}
                                  label={tc.shipItem}
                                  value={shipItem}
                                  onChange={(e) => setShipItem(e.target.value)}
                                  options={so.lines.map((ln) => ({ value: ln.itemSku, label: `${ln.itemSku} — ${ln.description}` }))}
                                  placeholder={tc.shipItem}
                                />
                                <Input id={`ship-qty-${so.id}`} label={tc.shipQty} type="number" min="0" step="any" value={shipQty} onChange={(e) => setShipQty(e.target.value)} />
                              </div>
                              <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                                <Button type="submit" small loading={busyId === so.id} disabled={busyId !== null}>{tc.shipBtn}</Button>
                                <Button type="button" variant="secondary" small onClick={() => setShipFor(null)}>{t.common.cancel}</Button>
                              </div>
                            </form>
                          </td>
                        </tr>
                      ) : null}
                      {rejectFor === so.id && so.status === "PendingApproval" ? (
                        <tr className="sub-row">
                          <td colSpan={6}>
                            <Input id={`reject-${so.id}`} label={tc.rejectionReason} value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} />
                            <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                              <Button small loading={busyId === so.id} disabled={busyId !== null || !rejectReason.trim()} onClick={() => void act(so.id, () => approveSalesOrder(token ?? "", so.id, rejectReason.trim())).then((ok) => { if (ok) setRejectFor(null); })}>
                                {tc.confirmReject}
                              </Button>
                              <Button small variant="secondary" onClick={() => setRejectFor(null)}>{t.common.cancel}</Button>
                            </div>
                          </td>
                        </tr>
                      ) : null}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <Modal open={releaseId !== null} onClose={() => setReleaseId(null)} title={tc.releaseConfirmTitle} closeLabel={t.common.cancel}>
        <p>{tc.releaseConfirm}</p>
        {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setReleaseId(null)}>{t.common.cancel}</Button>
          <Button loading={busyId !== null} onClick={() => void confirmRelease()}>{tc.release}</Button>
        </div>
      </Modal>
    </div>
  );
}
