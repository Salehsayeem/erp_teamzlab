"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createCrmCase,
  fetchCrmCases,
  fetchInteractions,
  logInteraction,
  transitionCrmCase,
  type CrmCase,
  type Interaction,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { fmtDate } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  Select,
  Tabs,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import { salesActionError, salesStatusLabel, salesTone } from "../_lib/helpers";

const PARTY_TYPES = ["Lead", "Customer", "Opportunity"];
const CHANNELS = ["Call", "Meeting", "Email", "Note", "Task"];
const PRIORITIES = ["Low", "Medium", "High"];

type InteractionForm = { partyType: string; partyId: string; channel: string; subject: string; notes: string; occurredAt: string };
const EMPTY_INTERACTION: InteractionForm = { partyType: "", partyId: "", channel: "", subject: "", notes: "", occurredAt: "" };

type CaseForm = { category: string; priority: string; customerId: string; salesOrderId: string };

type TabProps = {
  token: string | null;
  t: ReturnType<typeof getDictionary>;
  locale: Locale;
  pushToast: (tone: ToastItem["tone"], message: string) => void;
};

export default function CrmPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.crm;
  const { token, ready } = useAuth(locale);

  const [activeTab, setActiveTab] = useState("interactions");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  function pushToast(tone: ToastItem["tone"], message: string) {
    setToasts((prev) => [...prev, { id: crypto.randomUUID(), tone, message }]);
  }
  function dismissToast(id: string) {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }

  if (!ready) return <LoadingSkeleton rows={6} />;
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      <Tabs
        label={tc.title}
        activeId={activeTab}
        onChange={setActiveTab}
        tabs={[
          { id: "interactions", label: tc.tabInteractions, content: <InteractionsTab token={token} t={t} locale={locale} pushToast={pushToast} /> },
          { id: "cases", label: tc.tabCases, content: <CasesTab token={token} t={t} locale={locale} pushToast={pushToast} /> },
        ]}
      />
    </div>
  );
}

// ── Interactions ─────────────────────────────────────────────────────────────

function InteractionsTab({ token, t, locale, pushToast }: TabProps) {
  const tc = t.sales.crm.interactions;
  const [rows, setRows] = useState<Interaction[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [filterType, setFilterType] = useState("");
  const [filterPartyId, setFilterPartyId] = useState("");
  const [applied, setApplied] = useState<{ partyType?: string; partyId?: string }>({});
  const [form, setForm] = useState<InteractionForm>(EMPTY_INTERACTION);
  const [errors, setErrors] = useState<Partial<Record<keyof InteractionForm, string>>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [logging, setLogging] = useState(false);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchInteractions(token, applied.partyType, applied.partyId);
    if (res.ok) {
      setRows(res.data);
      setPhase("success");
    } else {
      setRows(null);
      setPhase("error");
    }
  }, [token, applied]);
  useEffect(() => { void load(); }, [load]);

  function validate(): boolean {
    const next: typeof errors = {};
    if (!form.partyType || !form.partyId.trim()) next.partyType = tc.errors.partyRequired;
    if (!form.channel) next.channel = tc.errors.channelRequired;
    if (!form.subject.trim()) next.subject = tc.errors.subjectRequired;
    if (!form.occurredAt) next.occurredAt = tc.errors.occurredRequired;
    setErrors(next);
    return Object.keys(next).length === 0;
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    setFormError(null);
    if (!validate() || !token) return;
    setLogging(true);
    const res = await logInteraction(token, {
      partyType: form.partyType,
      partyId: form.partyId.trim(),
      channel: form.channel,
      subject: form.subject.trim(),
      notes: form.notes.trim() || undefined,
      occurredAt: new Date(form.occurredAt).toISOString(),
    });
    setLogging(false);
    if (!res.ok) {
      setFormError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.logged);
    setForm(EMPTY_INTERACTION);
    await load();
  }

  if (phase === "loading" && rows === null) return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const list = rows ?? [];
  const filtered = applied.partyType !== undefined || applied.partyId !== undefined;
  return (
    <div className="stack">
      <section className="card">
        <h2 className="t-title">{tc.filterTitle}</h2>
        <div className="form-grid">
          <Select
            id="int-filter-type"
            label={tc.partyType}
            value={filterType}
            onChange={(e) => setFilterType(e.target.value)}
            options={PARTY_TYPES.map((p) => ({ value: p, label: p }))}
            placeholder={t.sales.statusFilterAll}
          />
          <Input id="int-filter-party" label={tc.partyId} value={filterPartyId} onChange={(e) => setFilterPartyId(e.target.value)} />
        </div>
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          <Button small onClick={() => setApplied({ partyType: filterType || undefined, partyId: filterPartyId.trim() || undefined })}>
            {tc.apply}
          </Button>
          {filtered ? (
            <Button small variant="secondary" onClick={() => { setFilterType(""); setFilterPartyId(""); setApplied({}); }}>
              {tc.clear}
            </Button>
          ) : null}
        </div>
      </section>

      <section className="card">
        {list.length === 0 ? <EmptyState title={filtered ? tc.noMatch : tc.empty} /> : (
          <ul className="stack" style={{ margin: 0, padding: 0, listStyle: "none", display: "flex", flexDirection: "column", gap: 12 }}>
            {list.map((row) => (
              <li key={row.id} style={{ borderBottom: "1px solid var(--border)", paddingBottom: 12 }}>
                <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                  <Badge tone={salesTone(row.channel)}>{salesStatusLabel(t, row.channel)}</Badge>
                  <strong>{row.subject}</strong>
                  <span className="mono">{row.id}</span>
                </div>
                {row.notes ? <p className="t-caption">{row.notes}</p> : null}
                <p className="t-caption">
                  {tc.partyType}: {row.partyType} · {tc.partyId}: <span className="mono">{row.partyId}</span>
                </p>
                <p className="t-caption">
                  {tc.occurred}: {fmtDate(row.occurredAt, locale)} · {tc.recorded}: {fmtDate(row.recordedAt, locale)}
                </p>
              </li>
            ))}
          </ul>
        )}
        <p className="t-caption">{tc.occurredVsRecorded}</p>
      </section>

      <section className="card">
        <h2 className="t-title">{tc.logTitle}</h2>
        {formError ? <p className="banner" role="alert">{formError}</p> : null}
        <form onSubmit={(e) => void submit(e)}>
          <div className="form-grid">
            <Select
              id="int-party-type"
              label={tc.partyType}
              value={form.partyType}
              onChange={(e) => setForm({ ...form, partyType: e.target.value })}
              options={PARTY_TYPES.map((p) => ({ value: p, label: p }))}
              placeholder={tc.partyType}
              error={errors.partyType}
            />
            <Input id="int-party-id" label={tc.partyId} value={form.partyId} onChange={(e) => setForm({ ...form, partyId: e.target.value })} />
            <Select
              id="int-channel"
              label={tc.channel}
              value={form.channel}
              onChange={(e) => setForm({ ...form, channel: e.target.value })}
              options={CHANNELS.map((c) => ({ value: c, label: salesStatusLabel(t, c) }))}
              placeholder={tc.channel}
              error={errors.channel}
            />
            <Input id="int-subject" label={tc.subject} value={form.subject} onChange={(e) => setForm({ ...form, subject: e.target.value })} error={errors.subject} />
            <Input id="int-occurred" label={tc.occurredAt} type="datetime-local" value={form.occurredAt} onChange={(e) => setForm({ ...form, occurredAt: e.target.value })} hint={tc.occurredHint} error={errors.occurredAt} />
            <Input id="int-notes" label={tc.notes} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </div>
          <p><Button type="submit" loading={logging}>{tc.log}</Button></p>
        </form>
      </section>
    </div>
  );
}

// ── Cases ─────────────────────────────────────────────────────────────────────

function CasesTab({ token, t, locale, pushToast }: TabProps) {
  const tc = t.sales.crm.cases;
  const [rows, setRows] = useState<CrmCase[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [overdueOnly, setOverdueOnly] = useState(false);

  const [form, setForm] = useState<CaseForm>({ category: "", priority: "", customerId: "", salesOrderId: "" });
  const [errors, setErrors] = useState<Partial<Record<keyof CaseForm, string>>>({});
  const [creating, setCreating] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const [resolveId, setResolveId] = useState<string | null>(null);
  const [resolution, setResolution] = useState("");
  const [resolutionError, setResolutionError] = useState<string | null>(null);

  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchCrmCases(token, overdueOnly);
    if (res.ok) {
      setRows(res.data);
      setPhase("success");
    } else {
      setRows(null);
      setPhase("error");
    }
  }, [token, overdueOnly]);
  useEffect(() => { void load(); }, [load]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setFormError(null);
    const next: typeof errors = {};
    if (!form.category.trim()) next.category = tc.errors.categoryRequired;
    if (!form.priority) next.priority = tc.errors.priorityRequired;
    setErrors(next);
    if (Object.keys(next).length > 0 || !token) return;
    setCreating(true);
    const res = await createCrmCase(token, {
      category: form.category.trim(),
      priority: form.priority,
      customerId: form.customerId.trim() || undefined,
      salesOrderId: form.salesOrderId.trim() || undefined,
    });
    setCreating(false);
    if (!res.ok) {
      setFormError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.created);
    setForm({ category: "", priority: "", customerId: "", salesOrderId: "" });
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

  async function confirmResolve() {
    if (!resolveId || !token) return;
    const text = resolution.trim();
    if (!text) {
      setResolutionError(tc.resolutionRequired);
      return;
    }
    setResolutionError(null);
    const ok = await act(resolveId, () => transitionCrmCase(token, resolveId, "Resolved", text));
    if (ok) {
      setResolveId(null);
      setResolution("");
    }
  }

  function rowActions(crmCase: CrmCase) {
    return (
      <div className="row-actions">
        {crmCase.status === "Open" ? (
          <Button small loading={busyId === crmCase.id} disabled={busyId !== null} onClick={() => void act(crmCase.id, () => transitionCrmCase(token ?? "", crmCase.id, "InProgress"))}>
            {tc.start}
          </Button>
        ) : null}
        {crmCase.status === "InProgress" ? (
          <Button small onClick={() => { setResolveId(crmCase.id); setResolution(""); setResolutionError(null); }}>
            {tc.resolve}
          </Button>
        ) : null}
        {crmCase.status === "Resolved" ? (
          <Button small loading={busyId === crmCase.id} disabled={busyId !== null} onClick={() => void act(crmCase.id, () => transitionCrmCase(token ?? "", crmCase.id, "Closed"))}>
            {tc.close}
          </Button>
        ) : null}
      </div>
    );
  }

  if (phase === "loading" && rows === null) return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const list = rows ?? [];
  return (
    <div className="stack">
      <section className="card">
        <h2 className="t-title">{tc.createTitle}</h2>
        {formError ? <p className="banner" role="alert">{formError}</p> : null}
        <form onSubmit={(e) => void submit(e)}>
          <div className="form-grid">
            <Input id="case-category" label={tc.category} value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })} error={errors.category} />
            <Select
              id="case-priority"
              label={tc.priority}
              value={form.priority}
              onChange={(e) => setForm({ ...form, priority: e.target.value })}
              options={PRIORITIES.map((p) => ({ value: p, label: salesStatusLabel(t, p) }))}
              placeholder={tc.priority}
              error={errors.priority}
            />
            <Input id="case-customer" label={tc.customer} value={form.customerId} onChange={(e) => setForm({ ...form, customerId: e.target.value })} />
            <Input id="case-so" label={tc.salesOrder} value={form.salesOrderId} onChange={(e) => setForm({ ...form, salesOrderId: e.target.value })} />
          </div>
          <p><Button type="submit" loading={creating}>{tc.create}</Button></p>
        </form>
      </section>

      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}

      <section className="card">
        <label htmlFor="case-overdue-toggle" className="field" style={{ maxWidth: 280, flexDirection: "row", alignItems: "center", gap: 8 }}>
          <input id="case-overdue-toggle" type="checkbox" checked={overdueOnly} onChange={(e) => setOverdueOnly(e.target.checked)} />
          <span style={{ fontWeight: 600 }}>{tc.overdueOnly}</span>
        </label>
        {list.length === 0 ? <EmptyState title={overdueOnly ? tc.noOverdue : tc.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{tc.columns.case}</th>
                  <th scope="col">{tc.columns.category}</th>
                  <th scope="col">{tc.columns.priority}</th>
                  <th scope="col">{tc.columns.status}</th>
                  <th scope="col">{tc.columns.due}</th>
                  <th scope="col">{tc.columns.resolution}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((crmCase) => (
                  <tr key={crmCase.id}>
                    <td data-label={tc.columns.case} className="mono">{crmCase.id}</td>
                    <td data-label={tc.columns.category}>{crmCase.category}</td>
                    <td data-label={tc.columns.priority}><Badge tone={salesTone(crmCase.priority)}>{salesStatusLabel(t, crmCase.priority)}</Badge></td>
                    <td data-label={tc.columns.status}>
                      <Badge tone={salesTone(crmCase.status)}>{salesStatusLabel(t, crmCase.status)}</Badge>
                      {crmCase.overdue ? <> <Badge tone="danger">{tc.overdueBadge}</Badge></> : null}
                    </td>
                    <td data-label={tc.columns.due}><span className="t-caption">{fmtDate(crmCase.responseDueAt, locale)}</span></td>
                    <td data-label={tc.columns.resolution}>{crmCase.resolution ?? "—"}</td>
                    <td data-label={t.journals.actions}>{rowActions(crmCase)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <Modal open={resolveId !== null} onClose={() => setResolveId(null)} title={tc.resolve} closeLabel={t.common.cancel}>
        <Input
          id="case-resolution"
          label={tc.resolution}
          value={resolution}
          onChange={(e) => setResolution(e.target.value)}
          error={resolutionError ?? undefined}
        />
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setResolveId(null)}>{t.common.cancel}</Button>
          <Button loading={busyId !== null} onClick={() => void confirmResolve()}>{tc.confirmResolve}</Button>
        </div>
      </Modal>
    </div>
  );
}
