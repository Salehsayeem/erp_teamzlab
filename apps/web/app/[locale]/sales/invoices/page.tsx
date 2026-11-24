"use client";

import { Fragment, useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  approveAdjustment,
  createAdjustment,
  discardInvoice,
  draftInvoiceFromOrder,
  fetchAdjustments,
  fetchCustomers,
  fetchInvoices,
  fetchSalesOrders,
  postInvoice,
  type AdjustmentDocument,
  type CustomerRow,
  type Invoice,
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
import { fmtDay, hasPermission, salesActionError, salesStatusLabel, salesTone, useSalesMe } from "../_lib/helpers";

const INVOICE_STATUSES = ["Draft", "Posted", "Discarded"];

type AdjustLineForm = { itemSku: string; qty: string; unitPrice: string };
const EMPTY_ADJUST_LINE: AdjustLineForm = { itemSku: "", qty: "1", unitPrice: "" };
type AdjustForm = { kind: string; reason: string; lines: AdjustLineForm[] };
const EMPTY_ADJUST_FORM: AdjustForm = { kind: "Credit", reason: "", lines: [{ ...EMPTY_ADJUST_LINE }] };

export default function InvoicesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.invoices;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady } = useSalesMe(token);

  const [invoices, setInvoices] = useState<Invoice[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [statusFilter, setStatusFilter] = useState("");
  const [customerFilter, setCustomerFilter] = useState("");
  const [customers, setCustomers] = useState<CustomerRow[]>([]);

  const [orders, setOrders] = useState<SalesOrderRow[] | null>(null);
  const [draftSoId, setDraftSoId] = useState("");
  const [draftDate, setDraftDate] = useState("");
  const [drafting, setDrafting] = useState(false);
  const [draftError, setDraftError] = useState<string | null>(null);

  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [postModalId, setPostModalId] = useState<string | null>(null);
  const [discardModalId, setDiscardModalId] = useState<string | null>(null);

  const [adjustFor, setAdjustFor] = useState<string | null>(null);
  const [adjustForm, setAdjustForm] = useState<AdjustForm>(EMPTY_ADJUST_FORM);
  const [adjustErrors, setAdjustErrors] = useState<{ reason?: string; line?: string }>({});
  const [adjusting, setAdjusting] = useState(false);

  const [sessionAdjustments, setSessionAdjustments] = useState<Record<string, AdjustmentDocument[]>>({});
  const [historyAdjustments, setHistoryAdjustments] = useState<Record<string, AdjustmentDocument[]>>({});
  const [historyUnavailable, setHistoryUnavailable] = useState<Record<string, boolean>>({});
  const [rejectAdjustId, setRejectAdjustId] = useState<string | null>(null);
  const [rejectReason, setRejectReason] = useState("");

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
    const res = await fetchInvoices(token, statusFilter || undefined, customerFilter || undefined);
    if (res.ok) {
      setInvoices(res.data);
      setPhase("success");
    } else {
      setInvoices(null);
      setPhase("error");
    }
  }, [token, statusFilter, customerFilter]);
  useEffect(() => { void load(); }, [load]);

  // Customers feed the filter select; orders feed the draft-from card.
  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchCustomers(token).then((res) => { if (!cancelled && res.ok) setCustomers(res.data); });
    void fetchSalesOrders(token).then((res) => { if (!cancelled && res.ok) setOrders(res.data); });
    return () => { cancelled = true; };
  }, [token]);

  const billableOrders = useMemo(
    () => (orders ?? []).filter((o) => o.status === "Shipped" || o.status === "Invoiced"),
    [orders],
  );

  // Adjustment history: fetched when a posted invoice expands (read endpoint
  // landing in parallel — degrade honestly when it is not merged yet).
  useEffect(() => {
    if (!token || expandedId === null) return;
    const invoice = (invoices ?? []).find((i) => i.id === expandedId);
    if (!invoice || invoice.status !== "Posted") return;
    if (historyAdjustments[invoice.id] || historyUnavailable[invoice.id]) return;
    let cancelled = false;
    void fetchAdjustments(token, invoice.id).then((res) => {
      if (cancelled) return;
      if (res.ok) setHistoryAdjustments((prev) => ({ ...prev, [invoice.id]: res.data }));
      else setHistoryUnavailable((prev) => ({ ...prev, [invoice.id]: true }));
    });
    return () => { cancelled = true; };
  }, [token, expandedId, invoices, historyAdjustments, historyUnavailable]);

  function adjustmentsFor(invoiceId: string): AdjustmentDocument[] {
    const seen = new Set<string>();
    const merged: AdjustmentDocument[] = [];
    for (const doc of [...(sessionAdjustments[invoiceId] ?? []), ...(historyAdjustments[invoiceId] ?? [])]) {
      if (seen.has(doc.id)) continue;
      seen.add(doc.id);
      merged.push(doc);
    }
    return merged;
  }

  async function submitDraft(e: FormEvent) {
    e.preventDefault();
    setDraftError(null);
    if (!draftSoId || !token) return;
    setDrafting(true);
    const res = await draftInvoiceFromOrder(token, draftSoId, draftDate || null);
    setDrafting(false);
    if (!res.ok) {
      setDraftError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", `${tc.draftCreated} ${res.data.id}`);
    setDraftSoId("");
    setDraftDate("");
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

  async function confirmPost() {
    if (!postModalId || !token) return;
    setBusyId(postModalId);
    setActionError(null);
    const res = await postInvoice(token, postModalId);
    setBusyId(null);
    if (!res.ok) {
      setActionError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", `${tc.postedNote} ${res.data.journal.id}`);
    setPostModalId(null);
    await load();
  }

  async function confirmDiscard() {
    if (!discardModalId || !token) return;
    const ok = await act(discardModalId, () => discardInvoice(token, discardModalId));
    if (ok) {
      pushToast("success", tc.discardNote);
      setDiscardModalId(null);
      await load();
    }
  }

  function setAdjustLine(i: number, patch: Partial<AdjustLineForm>) {
    setAdjustForm((prev) => ({
      ...prev,
      lines: prev.lines.map((ln, idx) => (idx === i ? { ...ln, ...patch } : ln)),
    }));
  }

  function openAdjustForm(invoice: Invoice) {
    setAdjustFor(invoice.id);
    setAdjustErrors({});
    const first = invoice.lines[0];
    setAdjustForm({
      kind: "Credit",
      reason: "",
      lines: [{ itemSku: first?.itemSku ?? "", qty: "1", unitPrice: "" }],
    });
  }

  async function submitAdjustment(e: FormEvent, invoice: Invoice) {
    e.preventDefault();
    const errs: { reason?: string; line?: string } = {};
    if (!adjustForm.reason.trim()) errs.reason = tc.reasonRequired;
    const parsed = adjustForm.lines.map((ln) => ({ ...ln, qtyNum: Number(ln.qty), priceNum: Number(ln.unitPrice) }));
    if (parsed.length === 0 || parsed.some((ln) => !ln.itemSku.trim())) errs.line = tc.errors.lineRequired;
    else if (parsed.some((ln) => !ln.qty.trim() || !Number.isFinite(ln.qtyNum) || ln.qtyNum <= 0)) errs.line = tc.errors.qtyInvalid;
    else if (parsed.some((ln) => !ln.unitPrice.trim() || !Number.isFinite(ln.priceNum) || ln.priceNum < 0)) errs.line = tc.errors.priceInvalid;
    setAdjustErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;

    setAdjusting(true);
    const res = await createAdjustment(token, {
      kind: adjustForm.kind,
      invoiceId: invoice.id,
      reason: adjustForm.reason.trim(),
      lines: parsed.map((ln) => ({ itemSku: ln.itemSku.trim(), qty: ln.qtyNum, unitPrice: ln.priceNum })),
    });
    setAdjusting(false);
    if (!res.ok) {
      setActionError(salesActionError(t, res.code, res.message));
      return;
    }
    setSessionAdjustments((prev) => ({
      ...prev,
      [invoice.id]: [res.data, ...(prev[invoice.id] ?? [])],
    }));
    pushToast("success", `${tc.adjustCreated} ${res.data.id}`);
    setAdjustFor(null);
    setAdjustForm(EMPTY_ADJUST_FORM);
  }

  async function approve(id: string, invoiceId: string, reason: string | null) {
    if (!token) return false;
    setBusyId(id);
    setActionError(null);
    const res = await approveAdjustment(token, id, reason);
    setBusyId(null);
    if (!res.ok) {
      setActionError(salesActionError(t, res.code, res.message));
      return false;
    }
    setSessionAdjustments((prev) => ({
      ...prev,
      [invoiceId]: (prev[invoiceId] ?? []).map((doc) => (doc.id === id ? res.data : doc)),
    }));
    setHistoryAdjustments((prev) => ({
      ...prev,
      [invoiceId]: (prev[invoiceId] ?? []).map((doc) => (doc.id === id ? res.data : doc)),
    }));
    if (reason === null) pushToast("success", `${tc.adjustApproved} ${res.data.journalId ?? ""}`);
    else pushToast("info", tc.adjustRejected);
    return true;
  }

  // Maker-checker gates: while /me is unresolved the controls render disabled;
  // a resolved non-holder sees nothing; the maker sees the note instead.
  const postGate = hasPermission(permissions, "invoices:approve");
  const adminGate = hasPermission(permissions, "*");

  if (!ready || (phase === "loading" && invoices === null)) return <LoadingSkeleton rows={7} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const list = invoices ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.draftFromTitle}</h2>
        <p className="t-caption">{tc.draftFromHint}</p>
        {billableOrders.length === 0 ? <EmptyState title={tc.noBillable} /> : (
          <form onSubmit={(e) => void submitDraft(e)}>
            <div className="form-grid">
              <Select
                id="inv-draft-so"
                label={tc.draftOrder}
                value={draftSoId}
                onChange={(e) => setDraftSoId(e.target.value)}
                options={billableOrders.map((o) => ({ value: o.id, label: `${o.id} — ${o.customerId} (${salesStatusLabel(t, o.status)})` }))}
                placeholder={tc.draftOrder}
              />
              <Input id="inv-draft-date" label={tc.draftDate} type="date" value={draftDate} onChange={(e) => setDraftDate(e.target.value)} />
            </div>
            <p><Button type="submit" loading={drafting} disabled={!draftSoId}>{tc.draft}</Button></p>
          </form>
        )}
      </section>

      <section className="card">
        <div className="form-grid">
          <Select
            id="inv-status-filter"
            label={tc.statusFilter}
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            options={INVOICE_STATUSES.map((s) => ({ value: s, label: salesStatusLabel(t, s) }))}
            placeholder={t.sales.statusFilterAll}
          />
          <Select
            id="inv-customer-filter"
            label={tc.customerFilter}
            value={customerFilter}
            onChange={(e) => setCustomerFilter(e.target.value)}
            options={customers.map((c) => ({ value: c.id, label: `${c.name} (${c.id})` }))}
            placeholder={t.sales.statusFilterAll}
          />
        </div>
        {list.length === 0 ? <EmptyState title={tc.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{tc.columns.invoice}</th>
                  <th scope="col">{tc.columns.customer}</th>
                  <th scope="col">{tc.columns.status}</th>
                  <th scope="col">{tc.columns.date}</th>
                  <th scope="col">{tc.columns.due}</th>
                  <th scope="col">{tc.columns.total}</th>
                  <th scope="col">{tc.columns.open}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((invoice) => {
                  const isMaker = meId !== null && invoice.createdBy === meId;
                  const canDiscard = !meReady ? null : isMaker || adminGate === true;
                  return (
                    <Fragment key={invoice.id}>
                      <tr>
                        <td data-label={tc.columns.invoice} className="mono">
                          {invoice.id}
                          <span className="t-caption"> · {invoice.sourceEventId}</span>
                        </td>
                        <td data-label={tc.columns.customer} className="mono">{invoice.customerId}</td>
                        <td data-label={tc.columns.status}><Badge tone={salesTone(invoice.status)}>{salesStatusLabel(t, invoice.status)}</Badge></td>
                        <td data-label={tc.columns.date}><span className="t-caption">{fmtDay(invoice.invoiceDate, locale)}</span></td>
                        <td data-label={tc.columns.due}><span className="t-caption">{invoice.dueDate ? fmtDay(invoice.dueDate, locale) : "—"}</span></td>
                        <td data-label={tc.columns.total} className="mono">{fmtMoney(invoice.total, locale, invoice.currency)}</td>
                        <td data-label={tc.columns.open} className="mono">{fmtMoney(invoice.openAmount, locale, invoice.currency)}</td>
                        <td data-label={t.journals.actions}>
                          <div className="row-actions">
                            <Button small variant="secondary" aria-expanded={expandedId === invoice.id} onClick={() => setExpandedId(expandedId === invoice.id ? null : invoice.id)}>
                              {expandedId === invoice.id ? tc.hideLines : tc.showLines}
                            </Button>
                            {invoice.status === "Draft" && postGate !== false ? (
                              isMaker ? null : (
                                <Button small disabled={!meReady || busyId !== null} onClick={() => setPostModalId(invoice.id)}>
                                  {tc.post}
                                </Button>
                              )
                            ) : null}
                            {invoice.status === "Draft" && canDiscard !== false ? (
                              <Button small variant="danger" disabled={!meReady || busyId !== null} onClick={() => setDiscardModalId(invoice.id)}>
                                {tc.discard}
                              </Button>
                            ) : null}
                            {invoice.status === "Posted" ? (
                              <Button small variant="secondary" onClick={() => (adjustFor === invoice.id ? setAdjustFor(null) : openAdjustForm(invoice))}>
                                {tc.adjust}
                              </Button>
                            ) : null}
                          </div>
                        </td>
                      </tr>
                      {invoice.status === "Draft" && isMaker ? (
                        <tr className="sub-row">
                          <td colSpan={8}><p className="t-caption" role="status">{t.sales.makerChecker}</p></td>
                        </tr>
                      ) : null}
                      {expandedId === invoice.id ? (
                        <tr className="sub-row">
                          <td colSpan={8}>
                            <InvoiceDetail
                              invoice={invoice}
                              t={t}
                              locale={locale}
                              adjustments={invoice.status === "Posted" ? adjustmentsFor(invoice.id) : []}
                              historyUnavailable={invoice.status === "Posted" && (historyUnavailable[invoice.id] ?? false)}
                              meId={meId}
                              meReady={meReady}
                              approveGate={postGate}
                              busyId={busyId}
                              rejectId={rejectAdjustId}
                              rejectReason={rejectReason}
                              onRejectOpen={(id) => { setRejectAdjustId(id); setRejectReason(""); }}
                              onRejectReason={setRejectReason}
                              onRejectCancel={() => setRejectAdjustId(null)}
                              onApprove={(doc) => void approve(doc.id, invoice.id, null)}
                              onRejectConfirm={(doc) => void approve(doc.id, invoice.id, rejectReason.trim() || null).then((ok) => { if (ok) setRejectAdjustId(null); })}
                            />
                            {invoice.status === "Posted" && adjustFor === invoice.id ? (
                              <AdjustmentForm
                                t={t}
                                form={adjustForm}
                                errors={adjustErrors}
                                busy={adjusting}
                                onKind={(kind) => setAdjustForm({ ...adjustForm, kind })}
                                onReason={(reason) => setAdjustForm({ ...adjustForm, reason })}
                                onLine={(i, patch) => setAdjustLine(i, patch)}
                                onAddLine={() => setAdjustForm({ ...adjustForm, lines: [...adjustForm.lines, { ...EMPTY_ADJUST_LINE }] })}
                                onRemoveLine={(i) => setAdjustForm({ ...adjustForm, lines: adjustForm.lines.filter((_, idx) => idx !== i) })}
                                onSubmit={(e) => void submitAdjustment(e, invoice)}
                                onCancel={() => { setAdjustFor(null); setAdjustForm(EMPTY_ADJUST_FORM); }}
                              />
                            ) : null}
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

      <Modal open={postModalId !== null} onClose={() => setPostModalId(null)} title={tc.postConfirmTitle} closeLabel={t.common.cancel}>
        <p>{tc.postConfirm}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setPostModalId(null)}>{t.common.cancel}</Button>
          <Button loading={busyId !== null} onClick={() => void confirmPost()}>{tc.post}</Button>
        </div>
      </Modal>

      <Modal open={discardModalId !== null} onClose={() => setDiscardModalId(null)} title={tc.discardConfirmTitle} closeLabel={t.common.cancel}>
        <p>{tc.discardConfirm}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setDiscardModalId(null)}>{t.common.cancel}</Button>
          <Button variant="danger" loading={busyId !== null} onClick={() => void confirmDiscard()}>{tc.discard}</Button>
        </div>
      </Modal>
    </div>
  );
}

// ── Expanded invoice detail: meta, lines, adjustments ────────────────────────

type DetailProps = {
  invoice: Invoice;
  t: ReturnType<typeof getDictionary>;
  locale: Locale;
  adjustments: AdjustmentDocument[];
  historyUnavailable: boolean;
  meId: string | null;
  meReady: boolean;
  approveGate: boolean | null;
  busyId: string | null;
  rejectId: string | null;
  rejectReason: string;
  onRejectOpen: (id: string) => void;
  onRejectReason: (value: string) => void;
  onRejectCancel: () => void;
  onApprove: (doc: AdjustmentDocument) => void;
  onRejectConfirm: (doc: AdjustmentDocument) => void;
};

function InvoiceDetail(props: DetailProps) {
  const { invoice, t, locale, adjustments, historyUnavailable } = props;
  const tc = t.sales.invoices;
  return (
    <div className="stack" style={{ marginTop: 0 }}>
      {invoice.status === "Draft" && invoice.stale ? <p className="banner" role="alert">{tc.stale}</p> : null}
      <p className="t-caption">
        {tc.sourceEvent}: <span className="mono">{invoice.sourceEventId}</span>
        {invoice.journalId ? <> · {tc.journal}: <span className="mono">{invoice.journalId}</span></> : null}
        {invoice.postedBy ? <> · {tc.postedBy}: <span className="mono">{invoice.postedBy}</span> ({fmtDate(invoice.postedAt ?? invoice.createdAt, locale)})</> : null}
      </p>
      <div className="table-scroll">
        <table className="table table--lines">
          <thead>
            <tr>
              <th scope="col">{tc.line.sku}</th>
              <th scope="col">{tc.line.description}</th>
              <th scope="col">{tc.line.qty}</th>
              <th scope="col">{tc.line.unitPrice}</th>
              <th scope="col">{tc.line.net}</th>
              <th scope="col">{tc.line.tax}</th>
            </tr>
          </thead>
          <tbody>
            {invoice.lines.map((line, i) => (
              <tr key={`${invoice.id}-${line.itemSku}-${i}`}>
                <td data-label={tc.line.sku} className="mono">{line.itemSku}</td>
                <td data-label={tc.line.description}>{line.description}</td>
                <td data-label={tc.line.qty} className="mono">{fmtNumber(line.qty, locale)}</td>
                <td data-label={tc.line.unitPrice} className="mono">{fmtMoney(line.unitPrice, locale, invoice.currency)}</td>
                <td data-label={tc.line.net} className="mono">{fmtMoney(line.lineNet, locale, invoice.currency)}</td>
                <td data-label={tc.line.tax} className="mono">{fmtMoney(line.taxAmount, locale, invoice.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {invoice.status === "Posted" ? (
        <AdjustmentList {...props} adjustments={adjustments} historyUnavailable={historyUnavailable} />
      ) : null}
    </div>
  );
}

function AdjustmentList(props: DetailProps & { adjustments: AdjustmentDocument[]; historyUnavailable: boolean }) {
  const { t, locale, adjustments, historyUnavailable, meId, meReady, approveGate, busyId, rejectId, rejectReason } = props;
  const tc = t.sales.invoices;
  if (historyUnavailable && adjustments.length === 0) {
    return <p className="t-caption">{tc.adjustHistoryUnavailable}</p>;
  }
  return (
    <div>
      <h3 className="t-body-strong">{tc.adjustments}</h3>
      {adjustments.length === 0 ? (
        <p className="t-caption">{historyUnavailable ? tc.adjustHistoryUnavailable : tc.noAdjustments}</p>
      ) : (
        <div className="table-scroll">
          <table className="table table--lines">
            <thead>
              <tr>
                <th scope="col">{tc.adjustColumns.id}</th>
                <th scope="col">{tc.adjustColumns.kind}</th>
                <th scope="col">{tc.adjustColumns.status}</th>
                <th scope="col">{tc.reason}</th>
                <th scope="col">{tc.adjustColumns.total}</th>
                <th scope="col">{tc.adjustColumns.journal}</th>
                <th scope="col">{t.journals.actions}</th>
              </tr>
            </thead>
            <tbody>
              {adjustments.map((doc) => {
                const isMaker = meId !== null && doc.createdBy === meId;
                const showActions = doc.status === "Draft" && approveGate !== false && !isMaker;
                return (
                  <tr key={doc.id}>
                    <td data-label={tc.adjustColumns.id} className="mono">{doc.id}</td>
                    <td data-label={tc.adjustColumns.kind}><Badge tone={salesTone(doc.kind)}>{salesStatusLabel(t, doc.kind)}</Badge></td>
                    <td data-label={tc.adjustColumns.status}><Badge tone={salesTone(doc.status)}>{salesStatusLabel(t, doc.status)}</Badge></td>
                    <td data-label={tc.reason}>{doc.rejectionReason ?? doc.reason}</td>
                    <td data-label={tc.adjustColumns.total} className="mono">{fmtMoney(doc.total, locale, "BDT")}</td>
                    <td data-label={tc.adjustColumns.journal} className="mono">{doc.journalId ?? "—"}</td>
                    <td data-label={t.journals.actions}>
                      {showActions ? (
                        <div className="row-actions">
                          <Button small disabled={!meReady || busyId !== null} onClick={() => props.onApprove(doc)}>
                            {tc.approveAdjustment}
                          </Button>
                          <Button small variant="danger" disabled={!meReady || busyId !== null} onClick={() => props.onRejectOpen(doc.id)}>
                            {tc.rejectAdjustment}
                          </Button>
                        </div>
                      ) : null}
                      {doc.status === "Draft" && isMaker ? (
                        <p className="t-caption" role="status">{t.sales.makerChecker}</p>
                      ) : null}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {rejectId !== null ? (
        <div className="stack" style={{ marginTop: 12 }}>
          <Input id="adjust-reject-reason" label={tc.rejectReason} value={rejectReason} onChange={(e) => props.onRejectReason(e.target.value)} />
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button small loading={busyId !== null} onClick={() => {
              const doc = adjustments.find((d) => d.id === rejectId);
              if (doc) props.onRejectConfirm(doc);
            }}>
              {tc.rejectAdjustment}
            </Button>
            <Button small variant="secondary" onClick={props.onRejectCancel}>{t.common.cancel}</Button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

// ── New-adjustment form (credit / debit memo against a posted invoice) ───────

type AdjustFormProps = {
  t: ReturnType<typeof getDictionary>;
  form: AdjustForm;
  errors: { reason?: string; line?: string };
  busy: boolean;
  onKind: (kind: string) => void;
  onReason: (reason: string) => void;
  onLine: (index: number, patch: Partial<AdjustLineForm>) => void;
  onAddLine: () => void;
  onRemoveLine: (index: number) => void;
  onSubmit: (e: FormEvent) => void;
  onCancel: () => void;
};

function AdjustmentForm(props: AdjustFormProps) {
  const { t, form, errors, busy } = props;
  const tc = t.sales.invoices;
  return (
    <form onSubmit={props.onSubmit}>
      <h3 className="t-body-strong">{tc.adjustTitle}</h3>
      <div className="form-grid">
        <Select
          id="adjust-kind"
          label={tc.kind}
          value={form.kind}
          onChange={(e) => props.onKind(e.target.value)}
          options={["Credit", "Debit"].map((k) => ({ value: k, label: salesStatusLabel(t, k) }))}
        />
        <Input id="adjust-reason" label={tc.reason} value={form.reason} onChange={(e) => props.onReason(e.target.value)} error={errors.reason} />
      </div>
      {form.lines.map((ln, i) => (
        <div className="form-grid" key={i}>
          <Input id={`adjust-sku-${i}`} label={`${tc.adjustLineSku} ${i + 1}`} value={ln.itemSku} onChange={(e) => props.onLine(i, { itemSku: e.target.value })} />
          <Input id={`adjust-qty-${i}`} label={`${tc.adjustLineQty} ${i + 1}`} type="number" min="0" step="any" value={ln.qty} onChange={(e) => props.onLine(i, { qty: e.target.value })} />
          <Input id={`adjust-price-${i}`} label={`${tc.adjustLinePrice} ${i + 1}`} type="number" min="0" step="any" value={ln.unitPrice} onChange={(e) => props.onLine(i, { unitPrice: e.target.value })} />
          <div className="field">
            <Button type="button" variant="secondary" small aria-label={`${tc.removeAdjustLine} ${i + 1}`} disabled={form.lines.length === 1} onClick={() => props.onRemoveLine(i)}>
              {tc.removeAdjustLine}
            </Button>
          </div>
        </div>
      ))}
      {errors.line ? <p className="field__error">{errors.line}</p> : null}
      <div className="row-actions" style={{ justifyContent: "flex-start" }}>
        <Button type="button" variant="secondary" small onClick={props.onAddLine}>{tc.addAdjustLine}</Button>
        <Button type="submit" small loading={busy}>{tc.submitAdjustment}</Button>
        <Button type="button" variant="secondary" small onClick={props.onCancel}>{t.common.cancel}</Button>
      </div>
    </form>
  );
}
