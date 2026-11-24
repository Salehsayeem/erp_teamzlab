"use client";

import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createDispute,
  fetchCreditExposure,
  fetchInvoiceDisputes,
  fetchCustomers,
  fetchWorklist,
  logReminder,
  resolveDispute,
  type CreditExposure,
  type CustomerRow,
  type DisputeRow,
  type WorklistRow,
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
  Select,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import { fmtDay, hasPermission, salesActionError, salesStatusLabel, salesTone, SalesMeError, useSalesMe } from "../_lib/helpers";

const REMINDER_CHANNELS = ["Email", "Call", "Letter", "Sms"];

type InlineState = { reminderId: string | null; disputeId: string | null; resolveId: string | null };

export default function CollectionsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.collections;
  const { token, ready } = useAuth(locale);
  const { permissions, meFailed, retryMe } = useSalesMe(token);

  const [rows, setRows] = useState<WorklistRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [worklistErrorCode, setWorklistErrorCode] = useState<string | null>(null);

  const [customers, setCustomers] = useState<CustomerRow[]>([]);
  const [exposureCustomerId, setExposureCustomerId] = useState("");
  const [exposure, setExposure] = useState<CreditExposure | null>(null);
  const [exposurePhase, setExposurePhase] = useState<"idle" | "loading" | "error" | "success">("idle");
  const [exposureForbidden, setExposureForbidden] = useState(false);
  const [exposureErrorCode, setExposureErrorCode] = useState<string | null>(null);

  const [inline, setInline] = useState<InlineState>({ reminderId: null, disputeId: null, resolveId: null });
  const [reminderForm, setReminderForm] = useState({ channel: "", recipient: "", templateVersion: "" });
  const [disputeForm, setDisputeForm] = useState({ reason: "", promiseDate: "" });
  const [outcome, setOutcome] = useState("");
  const [formErrors, setFormErrors] = useState<{ channel?: string; recipient?: string; reason?: string; outcome?: string }>({});

  const [sessionDisputes, setSessionDisputes] = useState<Record<string, DisputeRow[]>>({});
  const [fetchedDisputes, setFetchedDisputes] = useState<Record<string, DisputeRow[]>>({});
  const triedDisputes = useRef<Set<string>>(new Set());

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
    const res = await fetchWorklist(token);
    if (res.ok) {
      setRows(res.data);
      setPhase("success");
    } else {
      setRows(null);
      setWorklistErrorCode(res.code);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  // Customers feed the exposure panel; a ?customerId= deep link (from the
  // customers screen) preselects one. Read once, client-only.
  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchCustomers(token).then((res) => {
      if (cancelled || !res.ok) return;
      setCustomers(res.data);
      const linked = new URLSearchParams(window.location.search).get("customerId");
      if (linked && res.data.some((c) => c.id === linked)) setExposureCustomerId(linked);
    });
    return () => { cancelled = true; };
  }, [token]);

  const loadExposure = useCallback(async (customerId: string) => {
    if (!token || !customerId) return;
    setExposurePhase("loading");
    setExposureErrorCode(null);
    const res = await fetchCreditExposure(token, customerId);
    if (!res.ok) {
      setExposure(null);
      setExposureForbidden(res.code === "FORBIDDEN" || res.code === "HTTP_403");
      setExposureErrorCode(res.code);
      setExposurePhase("error");
      return;
    }
    setExposure(res.data);
    setExposureForbidden(false);
    setExposurePhase("success");
  }, [token]);
  useEffect(() => {
    if (exposureCustomerId) void loadExposure(exposureCustomerId);
    else {
      setExposure(null);
      setExposurePhase("idle");
    }
  }, [exposureCustomerId, loadExposure]);

  // Best-effort dispute discovery for flagged rows: an active dispute found
  // here keeps Resolve working after a reload; 404 → session-only fallback.
  useEffect(() => {
    if (!token) return;
    for (const row of rows ?? []) {
      if (!row.disputed || triedDisputes.current.has(row.invoiceId)) continue;
      triedDisputes.current.add(row.invoiceId);
      void fetchInvoiceDisputes(token, row.invoiceId).then((res) => {
        if (res.ok) setFetchedDisputes((prev) => ({ ...prev, [row.invoiceId]: res.data }));
      });
    }
  }, [token, rows]);

  function activeDisputeFor(invoiceId: string): DisputeRow | null {
    const all = [...(sessionDisputes[invoiceId] ?? []), ...(fetchedDisputes[invoiceId] ?? [])];
    const seen = new Set<string>();
    for (const dispute of all) {
      if (seen.has(dispute.id)) continue;
      seen.add(dispute.id);
      if (dispute.status === "Open" || dispute.status === "Promised") return dispute;
    }
    return null;
  }

  async function submitReminder(e: FormEvent, row: WorklistRow) {
    e.preventDefault();
    const recipient = reminderForm.recipient.trim();
    if (!recipient || !reminderForm.channel) {
      setFormErrors((prev) => ({
        ...prev,
        channel: reminderForm.channel ? undefined : t.sales.errors.validation,
        recipient: recipient ? undefined : t.sales.errors.validation,
      }));
      return;
    }
    setFormErrors({});
    if (!token) return;
    setBusyId(row.invoiceId);
    setActionError(null);
    const res = await logReminder(token, {
      invoiceId: row.invoiceId,
      channel: reminderForm.channel,
      recipient,
      templateVersion: reminderForm.templateVersion.trim() || undefined,
    });
    setBusyId(null);
    if (!res.ok) {
      setActionError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.reminderLogged);
    setInline((prev) => ({ ...prev, reminderId: null }));
    setReminderForm({ channel: "", recipient: "", templateVersion: "" });
  }

  async function submitDispute(e: FormEvent, row: WorklistRow) {
    e.preventDefault();
    const reason = disputeForm.reason.trim();
    if (!reason) {
      setFormErrors((prev) => ({ ...prev, reason: tc.disputeReasonRequired }));
      return;
    }
    setFormErrors({});
    if (!token) return;
    setBusyId(row.invoiceId);
    setActionError(null);
    const res = await createDispute(token, {
      invoiceId: row.invoiceId,
      reason,
      promiseDueDate: disputeForm.promiseDate || null,
    });
    setBusyId(null);
    if (!res.ok) {
      setActionError(salesActionError(t, res.code, res.message));
      return;
    }
    setSessionDisputes((prev) => ({
      ...prev,
      [row.invoiceId]: [res.data, ...(prev[row.invoiceId] ?? [])],
    }));
    pushToast("success", `${tc.disputeOpened} ${res.data.id}`);
    setInline((prev) => ({ ...prev, disputeId: null }));
    setDisputeForm({ reason: "", promiseDate: "" });
    await load();
  }

  async function submitResolve(e: FormEvent, invoiceId: string, disputeId: string) {
    e.preventDefault();
    const text = outcome.trim();
    if (!text) {
      setFormErrors((prev) => ({ ...prev, outcome: tc.outcomeRequired }));
      return;
    }
    setFormErrors({});
    if (!token) return;
    setBusyId(disputeId);
    setActionError(null);
    const res = await resolveDispute(token, disputeId, text);
    setBusyId(null);
    if (!res.ok) {
      setActionError(salesActionError(t, res.code, res.message));
      return;
    }
    const updated = res.data;
    setSessionDisputes((prev) => ({
      ...prev,
      [invoiceId]: (prev[invoiceId] ?? []).map((d) => (d.id === updated.id ? updated : d)),
    }));
    setFetchedDisputes((prev) => ({
      ...prev,
      [invoiceId]: (prev[invoiceId] ?? []).map((d) => (d.id === updated.id ? updated : d)),
    }));
    pushToast("success", tc.resolvedNote);
    setInline((prev) => ({ ...prev, resolveId: null }));
    setOutcome("");
    await load();
  }

  if (!ready || (phase === "loading" && rows === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    // 403 on the worklist is an expected outcome for roles without
    // collections:read — permission-specific copy, not the generic retry.
    const forbidden = worklistErrorCode === "FORBIDDEN" || worklistErrorCode === "HTTP_403";
    return (
      <div className="stack">
        <div className="page-header">
          <h1 className="t-display">{tc.title}</h1>
          <p className="t-caption">{tc.subtitle}</p>
        </div>
        {forbidden ? (
          <div className="state state--error" role="alert">
            <p className="state__title">{tc.worklistForbidden}</p>
          </div>
        ) : (
          <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />
        )}
      </div>
    );
  }

  const list = rows ?? [];
  const totalOpen = list.reduce((sum, row) => sum + row.openAmount, 0);
  const disputedCount = list.filter((row) => row.disputed).length;
  const followUpCount = list.filter((row) => row.followUp).length;
  const creditGate = hasPermission(permissions, "credit:read");

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}

      <div className="tiles">
        <div className="kpi">
          <p className="kpi__label">{tc.kpi.pastDue}</p>
          <p className="kpi__value">{fmtNumber(list.length, locale)}</p>
        </div>
        <div className="kpi">
          <p className="kpi__label">{tc.kpi.totalOpen}</p>
          <p className="kpi__value">{fmtMoney(totalOpen, locale, "BDT")}</p>
        </div>
        <div className="kpi">
          <p className="kpi__label">{tc.kpi.disputed}</p>
          <p className="kpi__value">{fmtNumber(disputedCount, locale)}</p>
        </div>
        <div className="kpi">
          <p className="kpi__label">{tc.kpi.followUps}</p>
          <p className="kpi__value">{fmtNumber(followUpCount, locale)}</p>
        </div>
      </div>

      <section className="card">
        {list.length === 0 ? <EmptyState title={tc.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{tc.columns.invoice}</th>
                  <th scope="col">{tc.columns.customer}</th>
                  <th scope="col">{tc.columns.due}</th>
                  <th scope="col">{tc.columns.days}</th>
                  <th scope="col">{tc.columns.open}</th>
                  <th scope="col">{tc.columns.flags}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((row) => {
                  const activeDispute = activeDisputeFor(row.invoiceId);
                  return (
                    <tr key={row.invoiceId}>
                      <td data-label={tc.columns.invoice} className="mono">{row.invoiceId}</td>
                      <td data-label={tc.columns.customer} className="mono">{row.customerId}</td>
                      <td data-label={tc.columns.due}><span className="t-caption">{fmtDay(row.dueDate, locale)}</span></td>
                      <td data-label={tc.columns.days} className="mono">{fmtNumber(row.daysPastDue, locale)}</td>
                      <td data-label={tc.columns.open} className="mono">{fmtMoney(row.openAmount, locale, "BDT")}</td>
                      <td data-label={tc.columns.flags}>
                        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                          {row.disputed ? <Badge tone="danger">{tc.flagDisputed}</Badge> : null}
                          {row.suppressed ? <Badge tone="muted">{tc.flagSuppressed}</Badge> : null}
                          {row.followUp ? <Badge tone="warning">{tc.flagFollowUp}</Badge> : null}
                          {!row.disputed && !row.suppressed && !row.followUp ? "—" : null}
                        </div>
                      </td>
                      <td data-label={t.journals.actions}>
                        <div className="row-actions">
                          <Button small variant="secondary" aria-expanded={inline.reminderId === row.invoiceId} onClick={() => setInline((prev) => ({ ...prev, reminderId: prev.reminderId === row.invoiceId ? null : row.invoiceId, disputeId: null }))}>
                            {tc.reminderTitle}
                          </Button>
                          <Button small variant="secondary" aria-expanded={inline.disputeId === row.invoiceId} onClick={() => setInline((prev) => ({ ...prev, disputeId: prev.disputeId === row.invoiceId ? null : row.invoiceId, reminderId: null }))}>
                            {tc.disputeTitle}
                          </Button>
                          {activeDispute ? (
                            <Button small aria-expanded={inline.resolveId === activeDispute.id} onClick={() => setInline((prev) => ({ ...prev, resolveId: prev.resolveId === activeDispute.id ? null : activeDispute.id }))}>
                              {tc.resolve}
                            </Button>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {list.map((row) => (
          <RowInlineForms
            key={`inline-${row.invoiceId}`}
            row={row}
            t={t}
            locale={locale}
            inline={inline}
            reminderForm={reminderForm}
            disputeForm={disputeForm}
            outcome={outcome}
            formErrors={formErrors}
            busyId={busyId}
            activeDispute={activeDisputeFor(row.invoiceId)}
            onReminderForm={(patch) => setReminderForm((prev) => ({ ...prev, ...patch }))}
            onDisputeForm={(patch) => setDisputeForm((prev) => ({ ...prev, ...patch }))}
            onOutcome={setOutcome}
            onReminderCancel={() => setInline((prev) => ({ ...prev, reminderId: null }))}
            onDisputeCancel={() => setInline((prev) => ({ ...prev, disputeId: null }))}
            onResolveCancel={() => setInline((prev) => ({ ...prev, resolveId: null }))}
            onReminderSubmit={(e) => void submitReminder(e, row)}
            onDisputeSubmit={(e) => void submitDispute(e, row)}
            onResolveSubmit={(e) => void submitResolve(e, row.invoiceId, inline.resolveId ?? "")}
          />
        ))}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.exposureTitle}</h2>
        <p className="t-caption">{tc.exposureHint}</p>
        <div className="form-grid">
          <Select
            id="exposure-customer"
            label={tc.exposureCustomer}
            value={exposureCustomerId}
            onChange={(e) => setExposureCustomerId(e.target.value)}
            options={customers.map((c) => ({ value: c.id, label: `${c.name} (${c.id})` }))}
            placeholder={tc.pickCustomer}
          />
        </div>
        {creditGate === false ? (
          <div className="state state--error" role="alert">
            <p className="state__title">{tc.forbidden}</p>
          </div>
        ) : exposurePhase === "loading" ? (
          <LoadingSkeleton rows={2} />
        ) : exposurePhase === "error" && exposureForbidden ? (
          <div className="state state--error" role="alert">
            <p className="state__title">{tc.forbidden}</p>
          </div>
        ) : exposurePhase === "error" ? (
          <ErrorState message={salesActionError(t, exposureErrorCode ?? "ERROR", "")} retryLabel={t.common.retry} onRetry={() => void loadExposure(exposureCustomerId)} />
        ) : exposure && !exposure.hasData ? (
          <EmptyState title={tc.noData} />
        ) : exposure ? (
          <div className="tiles">
            <div className="kpi">
              <p className="kpi__label">{tc.exposureTiles.limit}</p>
              <p className="kpi__value">{exposure.creditLimit === null ? tc.noLimit : fmtMoney(exposure.creditLimit, locale, "BDT")}</p>
            </div>
            <div className="kpi">
              <p className="kpi__label">{tc.exposureTiles.openInvoiced}</p>
              <p className="kpi__value">{fmtMoney(exposure.openInvoiced, locale, "BDT")}</p>
            </div>
            <div className="kpi">
              <p className="kpi__label">{tc.exposureTiles.openOrders}</p>
              <p className="kpi__value">{fmtMoney(exposure.openOrders, locale, "BDT")}</p>
            </div>
            <div className="kpi">
              <p className="kpi__label">{tc.exposureTiles.total}</p>
              <p className="kpi__value">{fmtMoney(exposure.total, locale, "BDT")}</p>
              {exposure.overLimit ? <p className="kpi__delta"><Badge tone="danger">{tc.overLimit}</Badge></p> : null}
            </div>
          </div>
        ) : null}
      </section>
    </div>
  );
}

// Renders the inline reminder/dispute/resolve forms under the worklist card.
// Kept as a component so the row map above stays readable; renders nothing
// unless one of its forms is open for this row.
type InlineProps = {
  row: WorklistRow;
  t: ReturnType<typeof getDictionary>;
  locale: Locale;
  inline: InlineState;
  reminderForm: { channel: string; recipient: string; templateVersion: string };
  disputeForm: { reason: string; promiseDate: string };
  outcome: string;
  formErrors: { channel?: string; recipient?: string; reason?: string; outcome?: string };
  busyId: string | null;
  activeDispute: DisputeRow | null;
  onReminderForm: (patch: Partial<{ channel: string; recipient: string; templateVersion: string }>) => void;
  onDisputeForm: (patch: Partial<{ reason: string; promiseDate: string }>) => void;
  onOutcome: (value: string) => void;
  onReminderCancel: () => void;
  onDisputeCancel: () => void;
  onResolveCancel: () => void;
  onReminderSubmit: (e: FormEvent) => void;
  onDisputeSubmit: (e: FormEvent) => void;
  onResolveSubmit: (e: FormEvent) => void;
};

function RowInlineForms(props: InlineProps) {
  const { row, t, inline, reminderForm, disputeForm, outcome, formErrors, busyId, activeDispute } = props;
  const tc = t.sales.collections;
  const showReminder = inline.reminderId === row.invoiceId;
  const showDispute = inline.disputeId === row.invoiceId;
  const showResolve = activeDispute !== null && inline.resolveId === activeDispute.id;
  if (!showReminder && !showDispute && !showResolve) return null;
  return (
    <div className="stack" style={{ marginTop: 12 }}>
      {showReminder ? (
        <form onSubmit={props.onReminderSubmit}>
          <h3 className="t-body-strong">{tc.reminderTitle} · <span className="mono">{row.invoiceId}</span></h3>
          <div className="form-grid">
            <Select
              id={`rem-channel-${row.invoiceId}`}
              label={tc.channel}
              value={reminderForm.channel}
              onChange={(e) => props.onReminderForm({ channel: e.target.value })}
              options={REMINDER_CHANNELS.map((c) => ({ value: c, label: c }))}
              placeholder={tc.channel}
              error={formErrors.channel}
            />
            <Input id={`rem-recipient-${row.invoiceId}`} label={tc.recipient} value={reminderForm.recipient} onChange={(e) => props.onReminderForm({ recipient: e.target.value })} error={formErrors.recipient} />
            <Input id={`rem-template-${row.invoiceId}`} label={tc.templateVersion} value={reminderForm.templateVersion} onChange={(e) => props.onReminderForm({ templateVersion: e.target.value })} />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busyId === row.invoiceId}>{tc.logReminder}</Button>
            <Button type="button" variant="secondary" small onClick={props.onReminderCancel}>{t.common.cancel}</Button>
          </div>
        </form>
      ) : null}
      {showDispute ? (
        <form onSubmit={props.onDisputeSubmit}>
          <h3 className="t-body-strong">{tc.disputeTitle} · <span className="mono">{row.invoiceId}</span></h3>
          <div className="form-grid">
            <Input id={`dsp-reason-${row.invoiceId}`} label={tc.disputeReason} value={disputeForm.reason} onChange={(e) => props.onDisputeForm({ reason: e.target.value })} error={formErrors.reason} />
            <Input id={`dsp-promise-${row.invoiceId}`} label={tc.promiseDate} type="date" value={disputeForm.promiseDate} onChange={(e) => props.onDisputeForm({ promiseDate: e.target.value })} hint={tc.promiseHint} />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small variant="danger" loading={busyId === row.invoiceId}>{tc.openDispute}</Button>
            <Button type="button" variant="secondary" small onClick={props.onDisputeCancel}>{t.common.cancel}</Button>
          </div>
        </form>
      ) : null}
      {showResolve && activeDispute ? (
        <form onSubmit={props.onResolveSubmit}>
          <h3 className="t-body-strong">{tc.resolve} · <span className="mono">{activeDispute.id}</span> <Badge tone={salesTone(activeDispute.status)}>{salesStatusLabel(t, activeDispute.status)}</Badge></h3>
          <div className="form-grid">
            <Input id={`dsp-outcome-${row.invoiceId}`} label={tc.outcome} value={outcome} onChange={(e) => props.onOutcome(e.target.value)} error={formErrors.outcome} />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busyId === activeDispute.id}>{tc.confirmResolve}</Button>
            <Button type="button" variant="secondary" small onClick={props.onResolveCancel}>{t.common.cancel}</Button>
          </div>
        </form>
      ) : null}
    </div>
  );
}
