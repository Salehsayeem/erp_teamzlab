"use client";

import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  approveAmendment,
  cancelOrder,
  createAmendment,
  getSalesOrder,
  placeHold,
  releaseHold,
  type SalesOrderDetail,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { fmtDay, hasPermission, salesActionError, salesStatusLabel, salesTone, SalesMeError, useSalesMe } from "../../_lib/helpers";
import { localStatus, localTone } from "../../_lib/deferred";
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
  type BadgeTone,
} from "@/components/uikit";

const TONE_MAP: Record<string, BadgeTone> = {
  Active: "warning",
  Cleared: "success",
  Pending: "warning",
  Approved: "success",
  Rejected: "danger",
};

function backLink(locale: Locale): string {
  return "/" + locale + "/sales/orders";
}

type AmendLineForm = { itemSku: string; description: string; qty: string; unitPrice: string };
const EMPTY_AMEND_LINE: AmendLineForm = { itemSku: "", description: "", qty: "", unitPrice: "" };

export default function OrderLifecyclePage() {
  const params = useParams<{ locale: string; id: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const orderId = params.id;
  const t = getDictionary(locale);
  const tc = t.sales.orderlifecycle;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady, meFailed, retryMe } = useSalesMe(token);

  const [order, setOrder] = useState<SalesOrderDetail | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "notFound" | "success">("loading");

  const [holdOpen, setHoldOpen] = useState(false);
  const [holdType, setHoldType] = useState("Manual");
  const [holdReason, setHoldReason] = useState("");
  const [holdSource, setHoldSource] = useState("");
  const [holdErrors, setHoldErrors] = useState<{ reason?: string }>({});
  const [releaseFor, setReleaseFor] = useState<string | null>(null);
  const [releaseReason, setReleaseReason] = useState("");

  const [amendOpen, setAmendOpen] = useState(false);
  const [amendLines, setAmendLines] = useState<AmendLineForm[]>([{ ...EMPTY_AMEND_LINE }]);
  const [amendRationale, setAmendRationale] = useState("");
  const [amendErrors, setAmendErrors] = useState<{ lines?: string; rationale?: string }>({});
  const [decideFor, setDecideFor] = useState<{ id: string; mode: "approve" | "reject" } | null>(null);
  const [decideReason, setDecideReason] = useState("");

  const [cancelOpen, setCancelOpen] = useState(false);
  const [cancelScope, setCancelScope] = useState("Remaining");
  const [cancelReason, setCancelReason] = useState("");
  const [cancelErrors, setCancelErrors] = useState<{ reason?: string }>({});

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
    const res = await getSalesOrder(token, orderId);
    if (res.ok) {
      setOrder(res.data);
      setPhase("success");
    } else if (res.code === "SO_NOT_FOUND" || res.code === "HTTP_404") {
      setOrder(null);
      setPhase("notFound");
    } else {
      setOrder(null);
      setPhase("error");
    }
  }, [token, orderId]);
  useEffect(() => { void load(); }, [load]);

  async function runAction(run: () => Promise<{ ok: true; data: unknown } | { ok: false; code: string; message: string }>): Promise<boolean> {
    setBusy(true);
    setModalError(null);
    const res = await run();
    setBusy(false);
    if (!res.ok) {
      setModalError(salesActionError(t, res.code, res.message));
      return false;
    }
    await load();
    return true;
  }

  async function submitHold(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const errs: { reason?: string } = {};
    if (!holdReason.trim()) errs.reason = tc.reasonRequired;
    setHoldErrors(errs);
    if (Object.keys(errs).length > 0) return;
    const ok = await runAction(() => placeHold(token, orderId, {
      holdType,
      reason: holdReason.trim(),
      ...(holdSource.trim() ? { source: holdSource.trim() } : {}),
    }));
    if (ok) {
      setHoldOpen(false);
      setHoldReason("");
      setHoldSource("");
      pushToast("success", tc.holdPlaced);
    }
  }

  async function confirmRelease() {
    if (!token || releaseFor === null) return;
    const ok = await runAction(() => releaseHold(token, orderId, releaseFor, releaseReason.trim() || null));
    if (ok) {
      setReleaseFor(null);
      setReleaseReason("");
      pushToast("success", tc.holdReleased);
    }
  }

  function setAmendLine(i: number, patch: Partial<AmendLineForm>) {
    setAmendLines((prev) => prev.map((ln, idx) => (idx === i ? { ...ln, ...patch } : ln)));
  }

  async function submitAmendment(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const errs: { lines?: string; rationale?: string } = {};
    const valid = amendLines.filter((ln) => ln.itemSku.trim() && Number(ln.qty) > 0 && Number.isFinite(Number(ln.qty)));
    if (valid.length === 0) errs.lines = tc.errors.lineRequired;
    if (!amendRationale.trim()) errs.rationale = tc.rationaleRequired;
    setAmendErrors(errs);
    if (Object.keys(errs).length > 0) return;
    const ok = await runAction(() => createAmendment(token, orderId, {
      lines: valid.map((ln) => ({ itemSku: ln.itemSku.trim(), description: ln.description.trim(), qty: Number(ln.qty), unitPrice: Number(ln.unitPrice) || 0 })),
      rationale: amendRationale.trim(),
    }));
    if (ok) {
      setAmendOpen(false);
      setAmendLines([{ ...EMPTY_AMEND_LINE }]);
      setAmendRationale("");
      pushToast("success", tc.amendmentCreated);
    }
  }

  async function confirmDecide() {
    if (!token || decideFor === null) return;
    const rejecting = decideFor.mode === "reject";
    if (rejecting && !decideReason.trim()) return;
    const ok = await runAction(() => approveAmendment(token, orderId, decideFor.id, decideReason.trim() || null));
    if (ok) {
      const note = rejecting ? tc.amendmentRejected : tc.amendmentApproved;
      setDecideFor(null);
      setDecideReason("");
      pushToast(rejecting ? "info" : "success", note);
    }
  }

  async function submitCancel(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const errs: { reason?: string } = {};
    if (!cancelReason.trim()) errs.reason = tc.cancelReasonRequired;
    setCancelErrors(errs);
    if (Object.keys(errs).length > 0) return;
    const ok = await runAction(() => cancelOrder(token, orderId, { reason: cancelReason.trim(), scope: cancelScope }));
    if (ok) {
      setCancelOpen(false);
      setCancelReason("");
      pushToast("info", tc.cancelledNote);
    }
  }

  const writeGate = hasPermission(permissions, "so:write");
  const approveGate = hasPermission(permissions, "so:approve");

  if (!ready || (phase === "loading" && order === null)) return <LoadingSkeleton rows={7} />;
  if (phase === "notFound") return <EmptyState title={tc.notFound} />;
  if (phase === "error" || order === null) return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const isMaker = meId !== null && order.createdBy === meId;
  const pendingAmendment = order.amendments.some((a) => a.status === "Pending");
  const activeHolds = order.holds.filter((h) => h.status === "Active");
  const cancelled = order.cancellations.length > 0;
  return (
    <div className="stack">
      <div className="page-header">
        <p className="t-caption"><Link href={backLink(locale)}>{tc.backToList}</Link></p>
        <h1 className="t-display">{tc.orderLabel} <span className="mono">{order.id}</span></h1>
        <p className="t-caption">{t.sales.orders.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}

      <section className="card">
        <div className="form-grid">
          <p><span className="t-caption">{tc.customer}: </span><span className="mono">{order.customerId}</span></p>
          <p><span className="t-caption">{tc.status}: </span><Badge tone={salesTone(order.status)}>{salesStatusLabel(t, order.status)}</Badge></p>
          <p><span className="t-caption">{tc.total}: </span><span className="mono">{fmtMoney(order.total, locale, "BDT")}</span></p>
          <p><span className="t-caption">{tc.createdBy}: </span><span className="mono">{order.createdBy}</span></p>
          <p><span className="t-caption">{tc.sourceQuote}: </span><span className="mono">{order.sourceQuoteRevisionId ?? "—"}</span></p>
        </div>
        {pendingAmendment ? <p className="banner" role="status">{tc.pendingBlocksShip}</p> : null}
        {activeHolds.length > 0 ? <p className="banner" role="status">{tc.holdsTitle}: {activeHolds.length}</p> : null}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.linesTitle}</h2>
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{tc.lineColumns.sku}</th>
                <th scope="col">{tc.lineColumns.description}</th>
                <th scope="col">{tc.lineColumns.qty}</th>
                <th scope="col">{tc.lineColumns.unitPrice}</th>
                <th scope="col">{tc.lineColumns.expected}</th>
                <th scope="col">{tc.lineColumns.fulfilled}</th>
                <th scope="col">{tc.lineColumns.remaining}</th>
                <th scope="col">{tc.lineColumns.backorder}</th>
              </tr>
            </thead>
            <tbody>
              {order.lines.map((ln) => (
                <tr key={ln.itemSku}>
                  <td data-label={tc.lineColumns.sku} className="mono">{ln.itemSku}</td>
                  <td data-label={tc.lineColumns.description}>{ln.description}</td>
                  <td data-label={tc.lineColumns.qty} className="mono">{fmtNumber(ln.qty, locale)}</td>
                  <td data-label={tc.lineColumns.unitPrice} className="mono">{fmtMoney(ln.unitPrice, locale, "BDT")}</td>
                  <td data-label={tc.lineColumns.expected}><span className="t-caption">{ln.expectedDate ? fmtDay(ln.expectedDate, locale) : "—"}</span></td>
                  <td data-label={tc.lineColumns.fulfilled} className="mono">{fmtNumber(ln.fulfilledQty, locale)}</td>
                  <td data-label={tc.lineColumns.remaining} className="mono">{fmtNumber(ln.remainingQty, locale)}</td>
                  <td data-label={tc.lineColumns.backorder} className="mono">{ln.backorderQty > 0 ? <Badge tone="warning">{fmtNumber(ln.backorderQty, locale)}</Badge> : "0"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="card">
        <div className="row-actions" style={{ justifyContent: "space-between" }}>
          <h2 className="t-title">{tc.holdsTitle}</h2>
          <Button small disabled={writeGate === false || cancelled} onClick={() => { setHoldOpen(true); setModalError(null); }}>{tc.placeHold}</Button>
        </div>
        {order.holds.length === 0 ? (
          <p className="t-caption">{tc.noHolds}</p>
        ) : (
          <div className="table-scroll">
            <table className="table table--lines">
              <thead>
                <tr>
                  <th scope="col">{tc.holdColumns.id}</th>
                  <th scope="col">{tc.holdColumns.type}</th>
                  <th scope="col">{tc.holdColumns.status}</th>
                  <th scope="col">{tc.holdColumns.reason}</th>
                  <th scope="col">{tc.holdColumns.setBy}</th>
                  <th scope="col">{tc.holdColumns.setAt}</th>
                  <th scope="col">{tc.holdColumns.clearedBy}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {order.holds.map((hold) => (
                  <tr key={hold.id}>
                    <td data-label={tc.holdColumns.id} className="mono">{hold.id}</td>
                    <td data-label={tc.holdColumns.type} className="mono">{localStatus(t, tc.holdTypeOptions as unknown as Record<string, string>, hold.holdType)}</td>
                    <td data-label={tc.holdColumns.status}><Badge tone={localTone(TONE_MAP, hold.status)}>{localStatus(t, tc.statusLabels, hold.status)}</Badge></td>
                    <td data-label={tc.holdColumns.reason}>{hold.reason}</td>
                    <td data-label={tc.holdColumns.setBy} className="mono">{hold.setBy}</td>
                    <td data-label={tc.holdColumns.setAt}><span className="t-caption">{fmtDate(hold.setAt, locale)}</span></td>
                    <td data-label={tc.holdColumns.clearedBy} className="mono">{hold.clearedBy ?? "—"}</td>
                    <td data-label={t.journals.actions}>
                      {hold.status === "Active" ? (
                        <Button small variant="danger" disabled={!meReady || busy} onClick={() => { setReleaseFor(hold.id); setReleaseReason(""); setModalError(null); }}>{tc.releaseHold}</Button>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <div className="row-actions" style={{ justifyContent: "space-between" }}>
          <h2 className="t-title">{tc.amendmentsTitle}</h2>
          <Button small disabled={writeGate === false || cancelled} onClick={() => { setAmendOpen(true); setModalError(null); }}>{tc.createAmendment}</Button>
        </div>
        {order.amendments.length === 0 ? (
          <p className="t-caption">{tc.noAmendments}</p>
        ) : (
          <div className="table-scroll">
            <table className="table table--lines">
              <thead>
                <tr>
                  <th scope="col">{tc.amendmentColumns.id}</th>
                  <th scope="col">{tc.amendmentColumns.revision}</th>
                  <th scope="col">{tc.amendmentColumns.status}</th>
                  <th scope="col">{tc.amendmentColumns.rationale}</th>
                  <th scope="col">{tc.amendmentColumns.createdBy}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {order.amendments.map((am) => {
                  const maker = meId !== null && am.createdBy === meId;
                  return (
                    <tr key={am.id}>
                      <td data-label={tc.amendmentColumns.id} className="mono">{am.id}</td>
                      <td data-label={tc.amendmentColumns.revision} className="mono">{am.revision}</td>
                      <td data-label={tc.amendmentColumns.status}><Badge tone={localTone(TONE_MAP, am.status)}>{localStatus(t, tc.statusLabels, am.status)}</Badge></td>
                      <td data-label={tc.amendmentColumns.rationale}>{am.rationale}</td>
                      <td data-label={tc.amendmentColumns.createdBy} className="mono">{am.createdBy}</td>
                      <td data-label={t.journals.actions}>
                        {am.status === "Pending" ? (
                          maker ? (
                            <p className="t-caption" role="status">{t.sales.makerChecker}</p>
                          ) : (
                            <div className="row-actions">
                              <Button small disabled={!meReady || approveGate === null || busy} onClick={() => { setDecideFor({ id: am.id, mode: "approve" }); setDecideReason(""); setModalError(null); }}>{tc.approveAmendment}</Button>
                              <Button small variant="danger" disabled={!meReady || approveGate === null || busy} onClick={() => { setDecideFor({ id: am.id, mode: "reject" }); setDecideReason(""); setModalError(null); }}>{tc.rejectAmendment}</Button>
                            </div>
                          )
                        ) : null}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <div className="row-actions" style={{ justifyContent: "space-between" }}>
          <h2 className="t-title">{tc.cancellationsTitle}</h2>
          {!cancelled && writeGate !== false ? (
            <Button small variant="danger" onClick={() => { setCancelOpen(true); setCancelScope("Remaining"); setCancelReason(""); setModalError(null); }}>{tc.cancelTitle}</Button>
          ) : null}
        </div>
        {order.cancellations.length === 0 ? (
          <p className="t-caption">{tc.noCancellations}</p>
        ) : (
          <div className="table-scroll">
            <table className="table table--lines">
              <thead>
                <tr>
                  <th scope="col">{tc.cancelColumns.id}</th>
                  <th scope="col">{tc.cancelColumns.scope}</th>
                  <th scope="col">{tc.cancelColumns.reason}</th>
                  <th scope="col">{tc.cancelColumns.by}</th>
                  <th scope="col">{tc.cancelColumns.at}</th>
                </tr>
              </thead>
              <tbody>
                {order.cancellations.map((c) => (
                  <tr key={c.id}>
                    <td data-label={tc.cancelColumns.id} className="mono">{c.id}</td>
                    <td data-label={tc.cancelColumns.scope}>{c.scope === "Full" ? tc.scopeFull : tc.scopeRemaining}</td>
                    <td data-label={tc.cancelColumns.reason}>{c.reason}</td>
                    <td data-label={tc.cancelColumns.by} className="mono">{c.cancelledBy}</td>
                    <td data-label={tc.cancelColumns.at}><span className="t-caption">{fmtDate(c.cancelledAt, locale)}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <Modal open={holdOpen} onClose={() => setHoldOpen(false)} title={tc.holdConfirmTitle} closeLabel={t.common.cancel}>
        <p>{tc.holdConfirm}</p>
        <form onSubmit={(e) => void submitHold(e)}>
          <div className="form-grid">
            <Select
              id="ol-hold-type"
              label={tc.holdType}
              value={holdType}
              onChange={(e) => setHoldType(e.target.value)}
              options={[
                { value: "Credit", label: tc.holdTypeOptions.Credit },
                { value: "Compliance", label: tc.holdTypeOptions.Compliance },
                { value: "Pricing", label: tc.holdTypeOptions.Pricing },
                { value: "Manual", label: tc.holdTypeOptions.Manual },
              ]}
            />
            <Input id="ol-hold-reason" label={tc.holdReason} value={holdReason} onChange={(e) => setHoldReason(e.target.value)} error={holdErrors.reason} />
            <Input id="ol-hold-source" label={tc.holdSource} value={holdSource} onChange={(e) => setHoldSource(e.target.value)} />
          </div>
          {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setHoldOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.placeHold}</Button>
          </div>
        </form>
      </Modal>

      <Modal open={releaseFor !== null} onClose={() => setReleaseFor(null)} title={tc.releaseConfirmTitle} closeLabel={t.common.cancel}>
        <p>{tc.releaseConfirm}</p>
        <p className="t-caption">{tc.holdColumns.id}: <span className="mono">{releaseFor}</span></p>
        <Input id="ol-release-reason" label={tc.holdReason} value={releaseReason} onChange={(e) => setReleaseReason(e.target.value)} />
        {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setReleaseFor(null)}>{t.common.cancel}</Button>
          <Button loading={busy} onClick={() => void confirmRelease()}>{tc.releaseHold}</Button>
        </div>
      </Modal>

      <Modal open={amendOpen} onClose={() => setAmendOpen(false)} title={tc.createAmendment} closeLabel={t.common.cancel}>
        <p className="t-caption">{tc.pendingBlocksShip}</p>
        <form onSubmit={(e) => void submitAmendment(e)}>
          {amendLines.map((ln, i) => (
            <div className="form-grid" key={i}>
              <Input id={`am-sku-${i}`} label={`${tc.amendLine.sku} ${i + 1}`} value={ln.itemSku} onChange={(e) => setAmendLine(i, { itemSku: e.target.value })} />
              <Input id={`am-desc-${i}`} label={`${tc.amendLine.description} ${i + 1}`} value={ln.description} onChange={(e) => setAmendLine(i, { description: e.target.value })} />
              <Input id={`am-qty-${i}`} label={`${tc.amendLine.qty} ${i + 1}`} type="number" min="0" step="any" value={ln.qty} onChange={(e) => setAmendLine(i, { qty: e.target.value })} />
              <Input id={`am-price-${i}`} label={`${tc.amendLine.unitPrice} ${i + 1}`} type="number" min="0" step="any" value={ln.unitPrice} onChange={(e) => setAmendLine(i, { unitPrice: e.target.value })} />
              <div className="field">
                <Button type="button" variant="secondary" small aria-label={`${tc.removeLine} ${i + 1}`} disabled={amendLines.length === 1} onClick={() => setAmendLines((prev) => prev.filter((_, idx) => idx !== i))}>
                  {tc.removeLine}
                </Button>
              </div>
            </div>
          ))}
          {amendErrors.lines ? <p className="field__error">{amendErrors.lines}</p> : null}
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="button" variant="secondary" small onClick={() => setAmendLines((prev) => [...prev, { ...EMPTY_AMEND_LINE }])}>{tc.addLine}</Button>
          </div>
          <Input id="ol-amend-rationale" label={tc.amendRationale} value={amendRationale} onChange={(e) => setAmendRationale(e.target.value)} error={amendErrors.rationale} />
          {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setAmendOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.submitAmendment}</Button>
          </div>
        </form>
      </Modal>

      <Modal open={decideFor !== null} onClose={() => setDecideFor(null)} title={decideFor?.mode === "reject" ? tc.confirmReject : tc.approveConfirmTitle} closeLabel={t.common.cancel}>
        <p>{decideFor?.mode === "reject" ? tc.confirmReject : tc.approveConfirm}</p>
        <p className="t-caption">{tc.amendmentColumns.id}: <span className="mono">{decideFor?.id}</span></p>
        {decideFor?.mode === "reject" ? (
          <Input id="ol-decide-reason" label={tc.rejectReason} value={decideReason} onChange={(e) => setDecideReason(e.target.value)} />
        ) : null}
        {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setDecideFor(null)}>{t.common.cancel}</Button>
          <Button variant={decideFor?.mode === "reject" ? "danger" : "primary"} loading={busy} onClick={() => void confirmDecide()}>
            {decideFor?.mode === "reject" ? tc.confirmReject : tc.approveAmendment}
          </Button>
        </div>
      </Modal>

      <Modal open={cancelOpen} onClose={() => setCancelOpen(false)} title={tc.cancelConfirmTitle} closeLabel={t.common.cancel}>
        <p>{tc.cancelConfirm}</p>
        <p className="t-caption">{tc.cancelFullNote}</p>
        <form onSubmit={(e) => void submitCancel(e)}>
          <div className="form-grid">
            <Select
              id="ol-cancel-scope"
              label={tc.cancelScope}
              value={cancelScope}
              onChange={(e) => setCancelScope(e.target.value)}
              options={[
                { value: "Remaining", label: tc.scopeRemaining },
                { value: "Full", label: tc.scopeFull },
              ]}
            />
            <Input id="ol-cancel-reason" label={tc.cancelReason} value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} error={cancelErrors.reason} />
          </div>
          {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setCancelOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" variant="danger" loading={busy}>{tc.cancelTitle}</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
}
