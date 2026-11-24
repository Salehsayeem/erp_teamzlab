"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  convertLead,
  createLead,
  fetchLeads,
  transitionLead,
  type Lead,
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
  Select,
  Modal,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import { salesActionError, salesStatusLabel, salesTone } from "../_lib/helpers";

const LEAD_STATUSES = ["New", "Working", "Qualified", "Disqualified", "Converted", "Recycled"];

type FormState = {
  company: string;
  contactName: string;
  email: string;
  phone: string;
  source: string;
  sourceRef: string;
  taxId: string;
};
const EMPTY_FORM: FormState = { company: "", contactName: "", email: "", phone: "", source: "", sourceRef: "", taxId: "" };

export default function LeadsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.leads;
  const { token, ready } = useAuth(locale);

  const [leads, setLeads] = useState<Lead[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [statusFilter, setStatusFilter] = useState("");

  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [sourceError, setSourceError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const [disqualifyId, setDisqualifyId] = useState<string | null>(null);
  const [reasonCode, setReasonCode] = useState("");
  const [reasonError, setReasonError] = useState<string | null>(null);

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
    const res = await fetchLeads(token, statusFilter || undefined);
    if (res.ok) {
      setLeads(res.data);
      setPhase("success");
    } else {
      setLeads(null);
      setPhase("error");
    }
  }, [token, statusFilter]);
  useEffect(() => { void load(); }, [load]);

  async function submitCapture(e: FormEvent) {
    e.preventDefault();
    setFormError(null);
    const source = form.source.trim();
    if (!source) {
      setSourceError(tc.sourceHint);
      return;
    }
    setSourceError(null);
    if (!token) return;
    setCreating(true);
    const res = await createLead(token, {
      company: form.company.trim() || undefined,
      contactName: form.contactName.trim() || undefined,
      email: form.email.trim() || undefined,
      phone: form.phone.trim() || undefined,
      source,
      sourceRef: form.sourceRef.trim() || undefined,
      taxId: form.taxId.trim() || undefined,
    });
    setCreating(false);
    if (!res.ok) {
      setFormError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.created);
    setForm(EMPTY_FORM);
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

  function leadName(lead: Lead): string {
    return lead.company ?? lead.contactName ?? t.sales.leads.anonymous;
  }

  async function confirmDisqualify() {
    if (!disqualifyId || !token) return;
    const reason = reasonCode.trim();
    if (!reason) {
      setReasonError(tc.reasonRequired);
      return;
    }
    setReasonError(null);
    const ok = await act(disqualifyId, () => transitionLead(token, disqualifyId, "Disqualified", reason));
    if (ok) {
      setDisqualifyId(null);
      setReasonCode("");
    }
  }

  async function convert(lead: Lead) {
    if (!token) return;
    setBusyId(lead.id);
    setActionError(null);
    const res = await convertLead(token, lead.id);
    setBusyId(null);
    if (!res.ok) {
      if (res.code === "CUSTOMER_DUPLICATE") {
        setActionError(`${tc.duplicateNote} (${res.message})`);
      } else {
        setActionError(salesActionError(t, res.code, res.message));
      }
      return;
    }
    pushToast("success", `${tc.converted} ${res.data.customer.id} + ${res.data.opportunity.id}`);
    await load();
  }

  function rowActions(lead: Lead) {
    if (lead.status === "Converted") return null;
    return (
      <div className="row-actions">
        {lead.status === "New" ? (
          <Button small loading={busyId === lead.id} disabled={busyId !== null} onClick={() => void act(lead.id, () => transitionLead(token ?? "", lead.id, "Working"))}>
            {tc.startWorking}
          </Button>
        ) : null}
        {lead.status === "Working" ? (
          <>
            <Button small loading={busyId === lead.id} disabled={busyId !== null} onClick={() => void act(lead.id, () => transitionLead(token ?? "", lead.id, "Qualified"))}>
              {tc.qualify}
            </Button>
            <Button small variant="danger" onClick={() => { setDisqualifyId(lead.id); setReasonCode(""); setReasonError(null); }}>
              {tc.disqualify}
            </Button>
          </>
        ) : null}
        {lead.status === "Disqualified" ? (
          <Button small loading={busyId === lead.id} disabled={busyId !== null} onClick={() => void act(lead.id, () => transitionLead(token ?? "", lead.id, "Recycled"))}>
            {tc.recycle}
          </Button>
        ) : null}
        {lead.status === "Qualified" ? (
          <Button small loading={busyId === lead.id} disabled={busyId !== null} onClick={() => void convert(lead)}>
            {tc.convert}
          </Button>
        ) : null}
      </div>
    );
  }

  if (!ready || (phase === "loading" && leads === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const list = leads ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.captureTitle}</h2>
        <form onSubmit={(e) => void submitCapture(e)}>
          <div className="form-grid">
            <Input id="lead-source" label={tc.source} value={form.source} onChange={(e) => setForm({ ...form, source: e.target.value })} hint={tc.sourceHint} error={sourceError ?? undefined} required />
            <Input id="lead-company" label={tc.company} value={form.company} onChange={(e) => setForm({ ...form, company: e.target.value })} />
            <Input id="lead-contact" label={tc.contactName} value={form.contactName} onChange={(e) => setForm({ ...form, contactName: e.target.value })} />
            <Input id="lead-email" label={tc.email} type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
            <Input id="lead-phone" label={tc.phone} value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
            <Input id="lead-source-ref" label={tc.sourceRef} value={form.sourceRef} onChange={(e) => setForm({ ...form, sourceRef: e.target.value })} />
            <Input id="lead-tax" label={tc.taxId} value={form.taxId} onChange={(e) => setForm({ ...form, taxId: e.target.value })} />
          </div>
          <p><Button type="submit" loading={creating}>{tc.create}</Button></p>
        </form>
      </section>

      <section className="card">
        <div className="form-grid">
          <Select
            id="lead-status-filter"
            label={tc.statusFilter}
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            options={LEAD_STATUSES.map((s) => ({ value: s, label: salesStatusLabel(t, s) }))}
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
                  <th scope="col">{tc.columns.lead}</th>
                  <th scope="col">{tc.columns.source}</th>
                  <th scope="col">{tc.columns.status}</th>
                  <th scope="col">{tc.columns.taxId}</th>
                  <th scope="col">{tc.columns.received}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((lead) => (
                  <tr key={lead.id}>
                    <td data-label={tc.columns.lead}>
                      {leadName(lead)}
                      {lead.email ? <span className="t-caption"> · {lead.email}</span> : null}
                      <span className="mono"> · {lead.id}</span>
                    </td>
                    <td data-label={tc.columns.source}>{lead.sourceRef ? `${lead.source} (${lead.sourceRef})` : lead.source}</td>
                    <td data-label={tc.columns.status}>
                      <Badge tone={salesTone(lead.status)}>{salesStatusLabel(t, lead.status)}</Badge>
                      {lead.reasonCode ? <span className="t-caption"> · {lead.reasonCode}</span> : null}
                    </td>
                    <td data-label={tc.columns.taxId} className="mono">{lead.taxId ?? "—"}</td>
                    <td data-label={tc.columns.received}><span className="t-caption">{fmtDate(lead.receivedAt, locale)}</span></td>
                    <td data-label={t.journals.actions}>{rowActions(lead)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <Modal open={disqualifyId !== null} onClose={() => setDisqualifyId(null)} title={tc.disqualify} closeLabel={t.common.cancel}>
        <Input
          id="lead-reason"
          label={tc.reasonCode}
          value={reasonCode}
          onChange={(e) => setReasonCode(e.target.value)}
          error={reasonError ?? undefined}
        />
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setDisqualifyId(null)}>{t.common.cancel}</Button>
          <Button variant="danger" loading={busyId !== null} onClick={() => void confirmDisqualify()}>{tc.confirmDisqualify}</Button>
        </div>
      </Modal>
    </div>
  );
}
