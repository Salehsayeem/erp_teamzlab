"use client";

import { Fragment, useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  addOpportunityLine,
  fetchOpportunities,
  transitionOpportunity,
  type Opportunity,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { fmtMoney, fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  Select,
} from "@/components/uikit";
import { fmtDay, salesActionError, salesStatusLabel, salesTone, SalesMeError, useSalesMe } from "../_lib/helpers";

const OPP_STATUSES = ["Open", "Won", "Lost"];

type LineForm = { itemSku: string; description: string; qty: string; targetPrice: string };
const EMPTY_LINE: LineForm = { itemSku: "", description: "", qty: "", targetPrice: "" };
type LineErrors = { itemSku?: string; qty?: string; targetPrice?: string };

export default function OpportunitiesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.opportunities;
  const { token, ready } = useAuth(locale);
  const { meFailed, retryMe } = useSalesMe(token);

  const [rows, setRows] = useState<Opportunity[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [statusFilter, setStatusFilter] = useState("");
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const [lineForms, setLineForms] = useState<Record<string, LineForm>>({});
  const [lineErrors, setLineErrors] = useState<Record<string, LineErrors>>({});
  const [addingLineId, setAddingLineId] = useState<string | null>(null);

  const [wonId, setWonId] = useState<string | null>(null);
  const [orderId, setOrderId] = useState("");
  const [lostId, setLostId] = useState<string | null>(null);
  const [lossReason, setLossReason] = useState("");
  const [competitor, setCompetitor] = useState("");
  const [reasonError, setReasonError] = useState<string | null>(null);
  const [modalError, setModalError] = useState<string | null>(null);

  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchOpportunities(token, statusFilter || undefined);
    if (res.ok) {
      setRows(res.data);
      setPhase("success");
    } else {
      setRows(null);
      setPhase("error");
    }
  }, [token, statusFilter]);
  useEffect(() => { void load(); }, [load]);

  const list = rows ?? [];
  const openRows = list.filter((o) => o.status === "Open");
  const openTotal = openRows.reduce((sum, o) => sum + o.total, 0);
  const wonTotal = list.filter((o) => o.status === "Won").reduce((sum, o) => sum + o.total, 0);
  const lostCount = list.filter((o) => o.status === "Lost").length;

  async function submitLine(e: FormEvent, opp: Opportunity) {
    e.preventDefault();
    const form = lineForms[opp.id] ?? EMPTY_LINE;
    const errs: LineErrors = {};
    if (!form.itemSku.trim()) errs.itemSku = tc.errors.skuRequired;
    const qty = Number(form.qty);
    if (!form.qty.trim()) errs.qty = tc.errors.qtyRequired;
    else if (!Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    const price = Number(form.targetPrice);
    if (!form.targetPrice.trim() || !Number.isFinite(price) || price < 0) errs.targetPrice = tc.errors.priceInvalid;
    setLineErrors((prev) => ({ ...prev, [opp.id]: errs }));
    if (Object.keys(errs).length > 0 || !token) return;

    setAddingLineId(opp.id);
    setActionError(null);
    const res = await addOpportunityLine(token, opp.id, {
      itemSku: form.itemSku.trim(),
      description: form.description.trim(),
      qty,
      targetPrice: price,
    });
    setAddingLineId(null);
    if (!res.ok) {
      setActionError(salesActionError(t, res.code, res.message));
      return;
    }
    setLineForms((prev) => ({ ...prev, [opp.id]: EMPTY_LINE }));
    await load();
  }

  /** Returns a localized error string, or null on success — modal callers
   * render it inside the dialog (fix round 1: failures were behind the scrim). */
  async function transition(id: string, body: { status: string; stage: string; orderId?: string; lossReason?: string; competitor?: string }): Promise<string | null> {
    if (!token) return t.common.networkError;
    setBusyId(id);
    const res = await transitionOpportunity(token, id, body);
    setBusyId(null);
    if (!res.ok) {
      return salesActionError(t, res.code, res.message);
    }
    return null;
  }

  async function confirmWon() {
    if (!wonId) return;
    const error = await transition(wonId, { status: "Won", stage: "", orderId: orderId.trim() });
    if (error) {
      setModalError(error);
      return;
    }
    setWonId(null);
    setOrderId("");
    await load();
  }

  async function confirmLost() {
    if (!lostId) return;
    const reason = lossReason.trim();
    if (!reason) {
      setReasonError(tc.lossReasonRequired);
      return;
    }
    setReasonError(null);
    const error = await transition(lostId, { status: "Lost", stage: "", lossReason: reason, competitor: competitor.trim() || undefined });
    if (error) {
      setModalError(error);
      return;
    }
    setLostId(null);
    setLossReason("");
    setCompetitor("");
    await load();
  }

  function rowActions(opp: Opportunity) {
    if (opp.status !== "Open") return null;
    return (
      <div className="row-actions">
        <Button small onClick={() => { setWonId(opp.id); setOrderId(""); setModalError(null); }}>
          {tc.markWon}
        </Button>
        <Button small variant="danger" onClick={() => { setLostId(opp.id); setLossReason(""); setCompetitor(""); setReasonError(null); setModalError(null); }}>
          {tc.markLost}
        </Button>
      </div>
    );
  }

  if (!ready || (phase === "loading" && rows === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}

      <div className="tiles">
        <div className="kpi">
          <p className="kpi__label">{tc.kpi.openCount}</p>
          <p className="kpi__value">{fmtNumber(openRows.length, locale)}</p>
        </div>
        <div className="kpi">
          <p className="kpi__label">{tc.kpi.openTotal}</p>
          <p className="kpi__value">{fmtMoney(openTotal, locale, "BDT")}</p>
        </div>
        <div className="kpi">
          <p className="kpi__label">{tc.kpi.wonTotal}</p>
          <p className="kpi__value">{fmtMoney(wonTotal, locale, "BDT")}</p>
        </div>
        <div className="kpi">
          <p className="kpi__label">{tc.kpi.lostCount}</p>
          <p className="kpi__value">{fmtNumber(lostCount, locale)}</p>
        </div>
      </div>

      <section className="card">
        <div className="form-grid">
          <Select
            id="opp-status-filter"
            label={tc.statusFilter}
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            options={OPP_STATUSES.map((s) => ({ value: s, label: salesStatusLabel(t, s) }))}
            placeholder={t.sales.statusFilterAll}
          />
        </div>
        {list.length === 0 ? (
          statusFilter ? <EmptyState title={tc.noMatch} /> : <EmptyState title={tc.empty} />
        ) : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{tc.columns.opportunity}</th>
                  <th scope="col">{tc.columns.customer}</th>
                  <th scope="col">{tc.columns.status}</th>
                  <th scope="col">{tc.columns.stage}</th>
                  <th scope="col">{tc.columns.close}</th>
                  <th scope="col">{tc.columns.owner}</th>
                  <th scope="col">{tc.columns.total}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((opp) => {
                  const form = lineForms[opp.id] ?? EMPTY_LINE;
                  const formErrs = lineErrors[opp.id] ?? {};
                  return (
                    <Fragment key={opp.id}>
                      <tr>
                        <td data-label={tc.columns.opportunity}>
                          {opp.title}
                          <span className="mono"> · {opp.id}</span>
                          {opp.lossReason ? <span className="t-caption"> · {opp.lossReason}</span> : null}
                        </td>
                        <td data-label={tc.columns.customer} className="mono">{opp.customerId ?? "—"}</td>
                        <td data-label={tc.columns.status}><Badge tone={salesTone(opp.status)}>{salesStatusLabel(t, opp.status)}</Badge></td>
                        <td data-label={tc.columns.stage}>{opp.stage}</td>
                        <td data-label={tc.columns.close}><span className="t-caption">{opp.expectedCloseDate ? fmtDay(opp.expectedCloseDate, locale) : "—"}</span></td>
                        <td data-label={tc.columns.owner} className="mono">{opp.ownerId}</td>
                        <td data-label={tc.columns.total} className="mono">{fmtMoney(opp.total, locale, opp.currency)}</td>
                        <td data-label={t.journals.actions}>
                          <div className="row-actions">
                            <Button small variant="secondary" aria-expanded={expandedId === opp.id} onClick={() => setExpandedId(expandedId === opp.id ? null : opp.id)}>
                              {expandedId === opp.id ? tc.hideLines : tc.showLines}
                            </Button>
                            {rowActions(opp)}
                          </div>
                        </td>
                      </tr>
                      {expandedId === opp.id ? (
                        <tr className="sub-row">
                          <td colSpan={8}>
                            <div className="stack" style={{ marginTop: 0 }}>
                              {opp.lines.length === 0 ? null : (
                                <div className="table-scroll">
                                  <table className="table table--lines">
                                    <thead>
                                      <tr>
                                        <th scope="col">{tc.line.sku}</th>
                                        <th scope="col">{tc.line.description}</th>
                                        <th scope="col">{tc.line.qty}</th>
                                        <th scope="col">{tc.line.targetPrice}</th>
                                        <th scope="col">{tc.line.amount}</th>
                                      </tr>
                                    </thead>
                                    <tbody>
                                      {opp.lines.map((line, i) => (
                                        <tr key={`${opp.id}-${line.itemSku}-${i}`}>
                                          <td data-label={tc.line.sku} className="mono">{line.itemSku}</td>
                                          <td data-label={tc.line.description}>{line.description}</td>
                                          <td data-label={tc.line.qty} className="mono">{fmtNumber(line.qty, locale)}</td>
                                          <td data-label={tc.line.targetPrice} className="mono">{fmtMoney(line.targetPrice, locale, opp.currency)}</td>
                                          <td data-label={tc.line.amount} className="mono">{fmtMoney(line.qty * line.targetPrice, locale, opp.currency)}</td>
                                        </tr>
                                      ))}
                                    </tbody>
                                  </table>
                                </div>
                              )}
                              {opp.status === "Open" ? (
                                <form onSubmit={(e) => void submitLine(e, opp)}>
                                  <div className="form-grid">
                                    <Input id={`opp-sku-${opp.id}`} label={tc.line.sku} value={form.itemSku} onChange={(e) => setLineForms((prev) => ({ ...prev, [opp.id]: { ...form, itemSku: e.target.value } }))} error={formErrs.itemSku} />
                                    <Input id={`opp-desc-${opp.id}`} label={tc.line.description} value={form.description} onChange={(e) => setLineForms((prev) => ({ ...prev, [opp.id]: { ...form, description: e.target.value } }))} />
                                    <Input id={`opp-qty-${opp.id}`} label={tc.line.qty} type="number" min="0" step="any" value={form.qty} onChange={(e) => setLineForms((prev) => ({ ...prev, [opp.id]: { ...form, qty: e.target.value } }))} error={formErrs.qty} />
                                    <Input id={`opp-price-${opp.id}`} label={tc.line.targetPrice} type="number" min="0" step="any" value={form.targetPrice} onChange={(e) => setLineForms((prev) => ({ ...prev, [opp.id]: { ...form, targetPrice: e.target.value } }))} error={formErrs.targetPrice} />
                                  </div>
                                  <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                                    <Button type="submit" small loading={addingLineId === opp.id} disabled={addingLineId !== null}>
                                      {tc.addLine}
                                    </Button>
                                  </div>
                                </form>
                              ) : (
                                <p className="t-caption">{tc.linesClosed}</p>
                              )}
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

      <Modal open={wonId !== null} onClose={() => setWonId(null)} title={tc.markWon} closeLabel={t.common.cancel}>
        <Input
          id="opp-order-id"
          label={tc.orderId}
          value={orderId}
          onChange={(e) => setOrderId(e.target.value)}
          hint={tc.orderIdHint}
        />
        {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setWonId(null)}>{t.common.cancel}</Button>
          <Button loading={busyId !== null} onClick={() => void confirmWon()}>{tc.confirmWon}</Button>
        </div>
      </Modal>

      <Modal open={lostId !== null} onClose={() => setLostId(null)} title={tc.markLost} closeLabel={t.common.cancel}>
        <Input id="opp-loss-reason" label={tc.lossReason} value={lossReason} onChange={(e) => setLossReason(e.target.value)} error={reasonError ?? undefined} />
        <Input id="opp-competitor" label={tc.competitor} value={competitor} onChange={(e) => setCompetitor(e.target.value)} />
        {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setLostId(null)}>{t.common.cancel}</Button>
          <Button variant="danger" loading={busyId !== null} onClick={() => void confirmLost()}>{tc.confirmLost}</Button>
        </div>
      </Modal>
    </div>
  );
}
