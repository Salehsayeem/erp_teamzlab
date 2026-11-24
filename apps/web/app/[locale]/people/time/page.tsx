"use client";

import { Fragment, useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createTimeCorrection,
  createTimeEntry,
  decideTimeCorrection,
  decideTimeEntry,
  fetchTimeCorrections,
  fetchTimeEntries,
  fetchWorkers,
  type TimeCorrectionRow,
  type TimeEntryRow,
  type Worker,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Select,
  Table,
  Tabs,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { fmtDay, hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../_lib/helpers";

const ENTRY_AND_CORRECTION_STATUSES = ["Pending", "Approved", "Rejected"];

export default function TimePage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.time;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady } = usePeopleMe(token);
  const canDecide = hasPermission(permissions, "time:approve");

  const [tab, setTab] = useState("entries");
  const [workerFilter, setWorkerFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");

  const [entries, setEntries] = useState<TimeEntryRow[] | null>(null);
  const [entriesPhase, setEntriesPhase] = useState<"loading" | "error" | "success">("loading");
  const [corrections, setCorrections] = useState<TimeCorrectionRow[] | null>(null);
  const [correctionsPhase, setCorrectionsPhase] = useState<"loading" | "error" | "success">("loading");
  const [workers, setWorkers] = useState<Worker[]>([]);

  const [entryForm, setEntryForm] = useState({ employmentId: "", workDate: "", hours: "8", overtimeHours: "0", note: "" });
  const [entryErrors, setEntryErrors] = useState<{ worker?: string; date?: string; hours?: string }>({});
  const [logging, setLogging] = useState(false);

  const [corrForm, setCorrForm] = useState({ timeEntryId: "", proposedHours: "", proposedOvertimeHours: "", reason: "" });
  const [corrErrors, setCorrErrors] = useState<{ entry?: string; hours?: string; reason?: string }>({});
  const [proposing, setProposing] = useState(false);

  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [rejectId, setRejectId] = useState<string | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [toasts, setToasts] = useState<ToastItem[]>([]);

  function pushToast(tone: ToastItem["tone"], message: string) {
    setToasts((prev) => [...prev, { id: crypto.randomUUID(), tone, message }]);
  }
  function dismissToast(id: string) {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }

  const load = useCallback(async () => {
    if (!token) return;
    setEntriesPhase("loading");
    setCorrectionsPhase("loading");
    const entriesRes = await fetchTimeEntries(token, workerFilter || undefined, statusFilter || undefined);
    if (entriesRes.ok) {
      setEntries(entriesRes.data);
      setEntriesPhase("success");
    } else {
      setEntries(null);
      setEntriesPhase("error");
    }
    const corrRes = await fetchTimeCorrections(token, workerFilter || undefined, statusFilter || undefined);
    if (corrRes.ok) {
      setCorrections(corrRes.data);
      setCorrectionsPhase("success");
    } else {
      setCorrections(null);
      setCorrectionsPhase("error");
    }
  }, [token, workerFilter, statusFilter]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchWorkers(token).then((res) => { if (!cancelled && res.ok) setWorkers(res.data); });
    return () => { cancelled = true; };
  }, [token]);

  const canDecideEntry = (row: TimeEntryRow) =>
    row.status === "Pending" && canDecide !== false && meId !== null && row.createdBy !== meId;
  const canDecideCorrection = (row: TimeCorrectionRow) =>
    row.status === "Pending" && canDecide !== false && meId !== null && row.requestedBy !== meId;

  async function submitEntry(e: FormEvent) {
    e.preventDefault();
    const errs: { worker?: string; date?: string; hours?: string } = {};
    if (!entryForm.employmentId) errs.worker = tc.workerRequired;
    if (!entryForm.workDate) errs.date = t.people.workerDetail.dateRequired;
    const h = Number(entryForm.hours), ot = Number(entryForm.overtimeHours);
    if (!Number.isFinite(h) || h < 0 || !Number.isFinite(ot) || ot < 0 || h + ot > 24 || h + ot === 0) errs.hours = tc.hoursInvalid;
    setEntryErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setLogging(true);
    setActionError(null);
    const res = await createTimeEntry(token, {
      employmentId: entryForm.employmentId,
      workDate: entryForm.workDate,
      hours: h,
      overtimeHours: ot,
      note: entryForm.note.trim() || null,
    });
    setLogging(false);
    if (!res.ok) {
      setActionError(peopleActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.logged + " " + res.data.id);
    setEntryForm({ employmentId: entryForm.employmentId, workDate: "", hours: "8", overtimeHours: "0", note: "" });
    void load();
  }

  async function decideEntry(row: TimeEntryRow, approve: boolean, reason: string | null) {
    if (!token) return;
    setBusyId(row.id);
    setActionError(null);
    const res = await decideTimeEntry(token, row.id, approve, reason);
    setBusyId(null);
    if (!res.ok) {
      setActionError(peopleActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", approve ? tc.approvedNote : tc.rejectedNote);
    void load();
  }

  async function submitCorrection(e: FormEvent) {
    e.preventDefault();
    const errs: { entry?: string; hours?: string; reason?: string } = {};
    if (!corrForm.timeEntryId) errs.entry = tc.entryRequired;
    const h = Number(corrForm.proposedHours), ot = Number(corrForm.proposedOvertimeHours);
    if (!Number.isFinite(h) || h < 0 || !Number.isFinite(ot) || ot < 0) errs.hours = tc.hoursInvalid;
    if (!corrForm.reason.trim()) errs.reason = tc.reasonRequired;
    setCorrErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setProposing(true);
    setActionError(null);
    const res = await createTimeCorrection(token, {
      timeEntryId: corrForm.timeEntryId, proposedHours: h, proposedOvertimeHours: ot, reason: corrForm.reason.trim(),
    });
    setProposing(false);
    if (!res.ok) {
      setActionError(peopleActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.proposed + " " + res.data.id);
    setCorrForm({ timeEntryId: corrForm.timeEntryId, proposedHours: "", proposedOvertimeHours: "", reason: "" });
    void load();
  }

  async function decideCorrection(row: TimeCorrectionRow, approve: boolean, reason: string | null) {
    if (!token) return;
    setBusyId(row.id);
    setActionError(null);
    const res = await decideTimeCorrection(token, row.id, approve, reason);
    setBusyId(null);
    if (!res.ok) {
      setActionError(peopleActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", approve ? tc.correctionApproved : tc.correctionRejected);
    void load();
  }

  const entryColumns: Column<TimeEntryRow>[] = useMemo(() => [
    { key: "entry", label: tc.entryColumns.entry, mono: true, render: (r) => r.id },
    { key: "worker", label: tc.entryColumns.worker, mono: true, render: (r) => r.employmentId },
    { key: "date", label: tc.entryColumns.date, render: (r) => <span className="t-caption">{fmtDay(r.workDate, locale)}</span> },
    { key: "hours", label: tc.entryColumns.hours, mono: true, render: (r) => fmtNumber(r.hours, locale) + " + " + fmtNumber(r.overtimeHours, locale) + " OT" },
    { key: "status", label: tc.entryColumns.status, render: (r) => <Badge tone={peopleTone(r.status)}>{peopleStatusLabel(t, r.status)}</Badge> },
    {
      key: "source", label: tc.entryColumns.source,
      render: (r) => <Badge tone={peopleTone(r.source)}>{peopleStatusLabel(t, r.source)}</Badge>,
    },
    { key: "actions", label: "", render: (r) => (
      <div className="row-actions">
        {canDecideEntry(r) ? (
          <Fragment>
            <Button small disabled={busyId !== null} onClick={() => void decideEntry(r, true, null)}>{tc.approve}</Button>
            <Button small variant="danger" disabled={busyId !== null} onClick={() => { setRejectId(r.id); setRejectReason(""); }}>{tc.reject}</Button>
          </Fragment>
        ) : null}
        {r.status === "Pending" && meId !== null && r.createdBy === meId ? (
          <p className="t-caption" role="status">{t.people.makerChecker}</p>
        ) : null}
        {r.status === "Pending" && meId === null ? (
          <Button small disabled={!meReady}>{tc.approve}</Button>
        ) : null}
      </div>
    ) },
  ], [t, tc, locale, busyId, meId, meReady, canDecide, decideEntry]);

  const correctionColumns: Column<TimeCorrectionRow>[] = useMemo(() => [
    { key: "correction", label: tc.correctionColumns.correction, mono: true, render: (r) => r.id },
    { key: "entry", label: tc.correctionColumns.entry, mono: true, render: (r) => r.timeEntryId },
    {
      key: "change", label: tc.correctionColumns.change,
      render: (r) => fmtNumber(r.previousHours, locale) + "h + " + fmtNumber(r.previousOvertimeHours, locale) + "h OT → " +
        fmtNumber(r.proposedHours, locale) + "h + " + fmtNumber(r.proposedOvertimeHours, locale) + "h OT",
    },
    { key: "delta", label: tc.correctionColumns.delta, mono: true, render: (r) => (r.deltaHours >= 0 ? "+" : "") + fmtNumber(r.deltaHours, locale) + "h / " + (r.deltaOvertimeHours >= 0 ? "+" : "") + fmtNumber(r.deltaOvertimeHours, locale) + "h OT" },
    { key: "reason", label: tc.correctionColumns.reason, render: (r) => r.reason },
    { key: "status", label: tc.correctionColumns.status, render: (r) => <Badge tone={peopleTone(r.status)}>{peopleStatusLabel(t, r.status)}</Badge> },
    { key: "actions", label: "", render: (r) => (
      <div className="row-actions">
        {canDecideCorrection(r) ? (
          <Fragment>
            <Button small disabled={busyId !== null} onClick={() => void decideCorrection(r, true, null)}>{tc.approve}</Button>
            <Button small variant="danger" disabled={busyId !== null} onClick={() => { setRejectId("corr-" + r.id); setRejectReason(""); }}>{tc.reject}</Button>
          </Fragment>
        ) : null}
        {r.status === "Pending" && meId !== null && r.requestedBy === meId ? (
          <p className="t-caption" role="status">{t.people.makerChecker}</p>
        ) : null}
      </div>
    ) },
  ], [t, tc, locale, busyId, meId, decideCorrection]);

  if (!ready || (entriesPhase === "loading" && entries === null)) return <LoadingSkeleton rows={7} />;
  if (entriesPhase === "error") {
    return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  }

  const entryList = entries ?? [];

  const correctionList = corrections ?? [];
  const workerOptions = workers.map((w) => ({ value: w.id, label: w.fullName + " (" + w.id + ")" }));
  const entryOptions = entryList
    .filter((r) => !workerFilter || r.employmentId === workerFilter)
    .filter((r) => r.status !== "Rejected")
    .map((r) => ({ value: r.id, label: r.id + " — " + r.workDate + " (" + peopleStatusLabel(t, r.status) + ")" }));

  function rejectBox(kindLabel: string) {
    return (
      <div className="stack" style={{ marginBlockStart: 12 }}>
        <Input id="time-reject-reason" label={tc.rejectReason} value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} />
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          <Button small loading={busyId !== null} onClick={() => {
            if (rejectId === null) return;
            if (rejectId.startsWith("corr-")) {
              const corr = correctionList.find((c) => c.id === rejectId.slice(5));
              if (corr) void decideCorrection(corr, false, rejectReason.trim() || null);
            } else {
              const entry = entryList.find((c) => c.id === rejectId);
              if (entry) void decideEntry(entry, false, rejectReason.trim() || null);
            }
            setRejectId(null);
          }}>
            {tc.reject}
          </Button>
          <Button small variant="secondary" onClick={() => setRejectId(null)}>{t.common.cancel}</Button>
        </div>
      </div>
    );
  }

  const entriesFormSection = (
    <section className="card">
        <h2 className="t-title">{tc.entryFormTitle}</h2>
        <p className="t-caption">{tc.selfEntryHint}</p>
        <form onSubmit={(e) => void submitEntry(e)}>
          <div className="form-grid">
            <Select id="time-worker" label={tc.worker} value={entryForm.employmentId} onChange={(e) => setEntryForm({ ...entryForm, employmentId: e.target.value })} options={workerOptions} placeholder={tc.worker} error={entryErrors.worker} />
            <Input id="time-date" label={tc.workDate} type="date" value={entryForm.workDate} onChange={(e) => setEntryForm({ ...entryForm, workDate: e.target.value })} error={entryErrors.date} />
            <Input id="time-hours" label={tc.hours} type="number" min="0" step="any" value={entryForm.hours} onChange={(e) => setEntryForm({ ...entryForm, hours: e.target.value })} error={entryErrors.hours} />
            <Input id="time-ot" label={tc.overtime} type="number" min="0" step="any" value={entryForm.overtimeHours} onChange={(e) => setEntryForm({ ...entryForm, overtimeHours: e.target.value })} />
            <Input id="time-note" label={tc.note} value={entryForm.note} onChange={(e) => setEntryForm({ ...entryForm, note: e.target.value })} />
          </div>
          <p><Button type="submit" loading={logging} disabled={!entryForm.employmentId}>{tc.logTime}</Button></p>
        </form>
    </section>
  );

  const entriesListSection = (
    <section className="card">
      <div className="form-grid">
        <Select id="time-worker-filter" label={tc.workerFilter} value={workerFilter} onChange={(e) => setWorkerFilter(e.target.value)} options={workerOptions} placeholder={t.people.statusFilterAll} />
        <Select id="time-status-filter" label={tc.statusFilter} value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} options={ENTRY_AND_CORRECTION_STATUSES.map((s) => ({ value: s, label: peopleStatusLabel(t, s) }))} placeholder={t.people.statusFilterAll} />
      </div>
      {entryList.length === 0 ? <EmptyState title={tc.entriesEmpty} /> : (
        <Table columns={entryColumns} rows={entryList} caption={tc.tabEntries} />
      )}
    </section>
  );

  const correctionsFormSection = (
    <section className="card">
      <h2 className="t-title">{tc.correctionFormTitle}</h2>
      <p className="t-caption">{tc.correctionHint}</p>
      <form onSubmit={(e) => void submitCorrection(e)}>
        <div className="form-grid">
          <Select id="corr-entry" label={tc.timeEntry} value={corrForm.timeEntryId} onChange={(e) => setCorrForm({ ...corrForm, timeEntryId: e.target.value })} options={entryOptions} placeholder={tc.timeEntry} error={corrErrors.entry} hint={tc.entryPickerHint} />
          <Input id="corr-hours" label={tc.proposedHours} type="number" min="0" step="any" value={corrForm.proposedHours} onChange={(e) => setCorrForm({ ...corrForm, proposedHours: e.target.value })} error={corrErrors.hours} />
          <Input id="corr-ot" label={tc.proposedOvertime} type="number" min="0" step="any" value={corrForm.proposedOvertimeHours} onChange={(e) => setCorrForm({ ...corrForm, proposedOvertimeHours: e.target.value })} />
          <Input id="corr-reason" label={tc.reason} value={corrForm.reason} onChange={(e) => setCorrForm({ ...corrForm, reason: e.target.value })} error={corrErrors.reason} />
        </div>
        <p><Button type="submit" loading={proposing} disabled={!corrForm.timeEntryId}>{tc.propose}</Button></p>
      </form>
    </section>
  );

  const correctionsListSection = (
    <section className="card">
      <div className="form-grid">
        <Select id="corr-worker-filter" label={tc.workerFilter} value={workerFilter} onChange={(e) => setWorkerFilter(e.target.value)} options={workerOptions} placeholder={t.people.statusFilterAll} />
        <Select id="corr-status-filter" label={tc.statusFilter} value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} options={ENTRY_AND_CORRECTION_STATUSES.map((s) => ({ value: s, label: peopleStatusLabel(t, s) }))} placeholder={t.people.statusFilterAll} />
      </div>
      {correctionList.length === 0 ? <EmptyState title={tc.correctionsEmpty} /> : (
        <Table columns={correctionColumns} rows={correctionList} caption={tc.tabCorrections} />
      )}
      {rejectId !== null ? rejectBox(tc.rejectReason) : null}
    </section>
  );

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}
      <Tabs
        label={tc.title}
        activeId={tab}
        onChange={setTab}
        tabs={[
          { id: "entries", label: tc.tabEntries, content: (
            <div className="stack" style={{ marginBlockStart: 0 }}>
              {entriesFormSection}
              {entriesListSection}
            </div>
          ) },
          { id: "corrections", label: tc.tabCorrections, content: (
            <div className="stack" style={{ marginBlockStart: 0 }}>
              {correctionsFormSection}
              {correctionsListSection}
            </div>
          ) },
        ]}
      />
    </div>
  );
}
