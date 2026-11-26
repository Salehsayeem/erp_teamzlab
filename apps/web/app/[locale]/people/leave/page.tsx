"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createLeaveAdjustment, createLeaveRequest, createLeaveType, decideLeaveAdjustment, decideLeaveRequest,
  disposeAttendanceException, fetchAttendanceExceptions, fetchLeaveAdjustments, fetchLeaveBalance, fetchLeaveRequests,
  fetchLeaveTypes, generateAttendanceExceptions, runAccrual, withdrawLeaveRequest,
  type AttendanceExceptionRow, type LeaveAdjustmentRow, type LeaveBalance, type LeaveRequestRow, type LeaveTypeRow,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Table, ToastRegion, type ToastItem } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../_lib/helpers";

export default function LeavePage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.leave;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canApprove = hasPermission(permissions, "leave:approve");
  const canAdjust = hasPermission(permissions, "leave:adjust");

  const [tab, setTab] = useState<"types" | "requests" | "balances" | "adjustments" | "attendance">("types");
  const [types, setTypes] = useState<LeaveTypeRow[] | null>(null);
  const [requests, setRequests] = useState<LeaveRequestRow[] | null>(null);
  const [adjustments, setAdjustments] = useState<LeaveAdjustmentRow[] | null>(null);
  const [attendance, setAttendance] = useState<AttendanceExceptionRow[] | null>(null);
  const [balance, setBalance] = useState<LeaveBalance | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [typeForm, setTypeForm] = useState({ code: "", name: "", units: "Days", accrualMethod: "Monthly", ratePerMonth: "1.25", requiresApproval: true });
  const [accrual, setAccrual] = useState({ leaveTypeId: "", period: "" });
  const [requestForm, setRequestForm] = useState({ employmentId: "", leaveTypeId: "", fromDate: "", toDate: "", days: "1", reason: "" });
  const [balanceForm, setBalanceForm] = useState({ workerId: "", leaveTypeId: "", asOf: "" });
  const [adjustmentForm, setAdjustmentForm] = useState({ employmentId: "", leaveTypeId: "", qty: "1", reason: "" });
  const [attForm, setAttForm] = useState({ from: "", to: "" });
  const [dispositionRow, setDispositionRow] = useState<AttendanceExceptionRow | null>(null);
  const [dispositionForm, setDispositionForm] = useState({ disposition: "Excused", reason: "" });
  const [deciding, setDeciding] = useState<{ row: LeaveRequestRow; approve: boolean } | null>(null);
  const [applying, setApplying] = useState<{ row: LeaveAdjustmentRow; approve: boolean } | null>(null);

  const push = (tone: ToastItem["tone"], message: string) => setToasts((p) => [...p, { id: crypto.randomUUID(), tone, message }]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [ty, rq, aj, at] = await Promise.all([
      fetchLeaveTypes(token), fetchLeaveRequests(token), fetchLeaveAdjustments(token), fetchAttendanceExceptions(token),
    ]);
    if (ty.ok && rq.ok && aj.ok && at.ok) {
      setTypes(ty.data); setRequests(rq.data); setAdjustments(aj.data); setAttendance(at.data);
      setPhase("success");
    } else {
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function guard(fn: () => Promise<{ ok: true; data: unknown } | { ok: false; code: string; message: string }>, label: string) {
    setBusy(true); setFormError(null);
    const res = await fn();
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return false; }
    push("success", label);
    void load();
    return true;
  }

  async function submitType(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    await guard(() => createLeaveType(token, {
      code: typeForm.code.trim(), name: typeForm.name.trim(), units: typeForm.units,
      accrualMethod: typeForm.accrualMethod, accrualRatePerMonth: typeForm.ratePerMonth ? Number(typeForm.ratePerMonth) : null,
      requiresApproval: typeForm.requiresApproval, effectiveFrom: new Date().toISOString().slice(0, 10),
    }), typeForm.code);
    setBusy(false);
  }

  async function submitAccrual(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const ok = await guard(() => runAccrual(token, accrual.leaveTypeId.trim(), { period: accrual.period.trim(), asOf: new Date().toISOString().slice(0, 10) }), accrual.period);
    setBusy(false);
    if (ok) push("success", accrual.period);
  }

  async function submitRequest(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    await guard(() => createLeaveRequest(token, {
      employmentId: requestForm.employmentId.trim(), leaveTypeId: requestForm.leaveTypeId.trim(),
      fromDate: requestForm.fromDate, toDate: requestForm.toDate,
      days: Number(requestForm.days) || 0, reason: requestForm.reason.trim(),
    }), requestForm.employmentId);
    setBusy(false);
  }

  async function submitAdjustment(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    await guard(() => createLeaveAdjustment(token, {
      employmentId: adjustmentForm.employmentId.trim(), leaveTypeId: adjustmentForm.leaveTypeId.trim(),
      qty: Number(adjustmentForm.qty) || 0, reason: adjustmentForm.reason.trim(),
    }), adjustmentForm.employmentId);
    setBusy(false);
  }

  async function decideRequest(approve: boolean) {
    if (!deciding) return;
    await guard(() => decideLeaveRequest(token!, deciding.row.id, approve), deciding.row.id);
    setDeciding(null);
    setBusy(false);
  }

  async function decideAdjustment(approve: boolean) {
    if (!applying) return;
    await guard(() => decideLeaveAdjustment(token!, applying.row.id, approve), applying.row.id);
    setApplying(null);
    setBusy(false);
  }

  async function submitDisposition(e: FormEvent) {
    e.preventDefault();
    if (!token || !dispositionRow) return;
    await guard(() => disposeAttendanceException(token, dispositionRow.id, {
      disposition: dispositionForm.disposition.trim(), reason: dispositionForm.reason.trim(),
    }), dispositionRow.id);
    setDispositionRow(null);
    setBusy(false);
  }

  async function submitAttendanceGeneration(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    await guard(() => generateAttendanceExceptions(token, { from: attForm.from, to: attForm.to }), "gen");
    setBusy(false);
  }

  async function submitBalance(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    setBusy(true); setFormError(null);
    const res = await fetchLeaveBalance(token, balanceForm.workerId.trim(), balanceForm.leaveTypeId.trim() || null, balanceForm.asOf || null);
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    setBalance(res.data);
  }

  const typeColumns: Column<LeaveTypeRow>[] = useMemo(() => [
    { key: "code", label: tc.code, mono: true, render: (x) => x.code },
    { key: "name", label: tc.columns.type, render: (x) => x.name },
    { key: "units", label: tc.units, mono: true, render: (x) => x.units + " · " + x.accrualMethod },
    { key: "status", label: tc.columns.status, render: (x) => <Badge tone={peopleTone(x.active ? "Active" : "Rejected")}>{peopleStatusLabel(t, x.active ? "Active" : "Rejected")}</Badge> },
  ], [tc, t]);

  const requestColumns: Column<LeaveRequestRow>[] = useMemo(() => [
    { key: "worker", label: tc.columns.worker, mono: true, render: (x) => x.employmentId },
    { key: "type", label: tc.columns.type, mono: true, render: (x) => x.leaveTypeId },
    { key: "days", label: tc.columns.days, mono: true, render: (x) => String(x.days) },
    { key: "from", label: tc.columns.from, mono: true, render: (x) => x.fromDate },
    { key: "status", label: tc.columns.status, render: (x) => <Badge tone={peopleTone(x.status)}>{peopleStatusLabel(t, x.status)}</Badge> },
    { key: "actions", label: "", render: (x) => x.status === "Pending" ? (
        <span className="inline-flex" style={{ gap: "0.25rem" }}>
          {canApprove === true ? <Button variant="secondary" onClick={() => setDeciding({ row: x, approve: true })}>{tc.approve}</Button> : null}
          {canApprove === true ? <Button variant="secondary" onClick={() => setDeciding({ row: x, approve: false })}>{tc.reject}</Button> : null}
        </span>
      ) : null },
  ], [tc, t, canApprove, token]);

  const adjustmentColumns: Column<LeaveAdjustmentRow>[] = useMemo(() => [
    { key: "worker", label: tc.columns.worker, mono: true, render: (x) => x.employmentId },
    { key: "type", label: tc.columns.type, mono: true, render: (x) => x.leaveTypeId },
    { key: "qty", label: tc.columns.qty, mono: true, render: (x) => String(x.qty) },
    { key: "status", label: tc.columns.status, render: (x) => <Badge tone={peopleTone(x.status)}>{peopleStatusLabel(t, x.status)}</Badge> },
    { key: "actions", label: "", render: (x) => x.status === "Pending" && canAdjust === true ? (
        <span className="inline-flex" style={{ gap: "0.25rem" }}>
          <Button variant="secondary" onClick={() => setApplying({ row: x, approve: true })}>{tc.applyAdjustment}</Button>
          <Button variant="secondary" onClick={() => setApplying({ row: x, approve: false })}>{tc.discardAdjustment}</Button>
        </span>
      ) : null },
  ], [tc, t, canAdjust, token]);

  const attColumns: Column<AttendanceExceptionRow>[] = useMemo(() => [
    { key: "worker", label: tc.attColumns.worker, mono: true, render: (x) => x.employmentId },
    { key: "date", label: tc.attColumns.date, mono: true, render: (x) => x.workDate },
    { key: "kind", label: tc.attColumns.kind, mono: true, render: (x) => x.kind },
    { key: "fact", label: tc.attColumns.fact, render: (x) => x.sourceFact },
    { key: "disp", label: tc.attColumns.disposition, render: (x) => x.disposition ?? "—" },
    { key: "actions", label: "", render: (x) => x.disposition === null ? (
        <Button variant="secondary" onClick={() => setDispositionRow(x)}>{tc.dispose}</Button>
      ) : null },
  ], [tc, token]);

  if (!ready || phase === "loading") return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={(id) => setToasts((p) => p.filter((x) => x.id !== id))} closeLabel={t.common.cancel} />
      {formError ? <p className="banner" role="alert">{formError}</p> : null}

      <nav className="inline-flex" style={{ gap: "0.5rem" }} aria-label={tc.title}>
        {(["types", "requests", "balances", "adjustments", "attendance"] as const).map((key) => (
          <Button key={key} variant={tab === key ? "primary" : "secondary"} onClick={() => setTab(key)}>
            {key === "types" ? tc.tabTypes : key === "requests" ? tc.tabRequests : key === "balances" ? tc.tabBalances
              : key === "adjustments" ? tc.tabAdjustments : tc.tabAttendance}
          </Button>
        ))}
      </nav>

      {tab === "types" ? (
        <section className="card">
          <h2 className="t-title">{tc.typeFormTitle}</h2>
          {canApprove === false ? <p className="t-caption">{t.people.errors.forbidden}</p> : null}
          <form onSubmit={(e) => void submitType(e)}>
            <div className="form-grid">
              <Input id="lt-code" label={tc.code} value={typeForm.code} onChange={(e) => setTypeForm({ ...typeForm, code: e.target.value })} />
              <Input id="lt-name" label={tc.name} value={typeForm.name} onChange={(e) => setTypeForm({ ...typeForm, name: e.target.value })} />
              <Input id="lt-units" label={tc.units} value={typeForm.units} onChange={(e) => setTypeForm({ ...typeForm, units: e.target.value })} />
              <Input id="lt-method" label={tc.accrualMethod} value={typeForm.accrualMethod} onChange={(e) => setTypeForm({ ...typeForm, accrualMethod: e.target.value })} />
              <Input id="lt-rate" label={tc.ratePerMonth} type="number" value={typeForm.ratePerMonth} onChange={(e) => setTypeForm({ ...typeForm, ratePerMonth: e.target.value })} />
            </div>
            <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createType}</Button></p>
          </form>
          {(types ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={typeColumns} rows={types ?? []} caption={tc.title} />}
        </section>
      ) : null}

      {tab === "requests" ? (
        <section className="card">
          <h2 className="t-title">{tc.requestFormTitle}</h2>
          <form onSubmit={(e) => void submitRequest(e)}>
            <div className="form-grid">
              <Input id="lr-worker" label={tc.columns.worker} value={requestForm.employmentId} onChange={(e) => setRequestForm({ ...requestForm, employmentId: e.target.value })} />
              <Input id="lr-type" label={tc.leaveTypeId} value={requestForm.leaveTypeId} onChange={(e) => setRequestForm({ ...requestForm, leaveTypeId: e.target.value })} />
              <Input id="lr-from" label={tc.fromDate} type="date" value={requestForm.fromDate} onChange={(e) => setRequestForm({ ...requestForm, fromDate: e.target.value })} />
              <Input id="lr-to" label={tc.toDate} type="date" value={requestForm.toDate} onChange={(e) => setRequestForm({ ...requestForm, toDate: e.target.value })} />
              <Input id="lr-days" label={tc.days} type="number" value={requestForm.days} onChange={(e) => setRequestForm({ ...requestForm, days: e.target.value })} />
              <Input id="lr-reason" label={tc.reason} value={requestForm.reason} onChange={(e) => setRequestForm({ ...requestForm, reason: e.target.value })} />
            </div>
            <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.submitRequest}</Button></p>
          </form>
          {(requests ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={requestColumns} rows={requests ?? []} caption={tc.tabRequests} />}
        </section>
      ) : null}

      {tab === "balances" ? (
        <section className="card">
          <h2 className="t-title">{tc.balanceTitle}</h2>
          <form onSubmit={(e) => void submitBalance(e)} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
            <Input id="lb-worker" label={tc.columns.worker} value={balanceForm.workerId} onChange={(e) => setBalanceForm({ ...balanceForm, workerId: e.target.value })} />
            <Input id="lb-type" label={tc.leaveTypeId} value={balanceForm.leaveTypeId} onChange={(e) => setBalanceForm({ ...balanceForm, leaveTypeId: e.target.value })} />
            <Input id="lb-asof" label={tc.asOf} type="date" value={balanceForm.asOf} onChange={(e) => setBalanceForm({ ...balanceForm, asOf: e.target.value })} />
            <Button type="submit" loading={busy}>{tc.loadBalance}</Button>
          </form>
          {balance ? (
            <div style={{ marginTop: "1rem" }}>
              <p className="banner" aria-live="polite">{balance.workerId}: {balance.balance}</p>
              {(balance.entries.length > 0) ? (
                <Table caption={tc.entries} columns={[
                  { key: "date", label: tc.columns.date, mono: true, render: (x: LeaveBalance["entries"][number]) => x.entryDate },
                  { key: "kind", label: tc.columns.kind, mono: true, render: (x: LeaveBalance["entries"][number]) => x.kind },
                  { key: "qty", label: tc.columns.qty, mono: true, render: (x: LeaveBalance["entries"][number]) => String(x.qty) },
                ]} rows={balance.entries} />
              ) : null}
            </div>
          ) : null}
        </section>
      ) : null}

      {tab === "adjustments" ? (
        <section className="card">
          <h2 className="t-title">{tc.adjustmentFormTitle}</h2>
          <p className="t-caption">{tc.adjustmentHint}</p>
          {canAdjust === true ? (
            <form onSubmit={(e) => void submitAdjustment(e)}>
              <div className="form-grid">
                <Input id="la-worker" label={tc.columns.worker} value={adjustmentForm.employmentId} onChange={(e) => setAdjustmentForm({ ...adjustmentForm, employmentId: e.target.value })} />
                <Input id="la-type" label={tc.leaveTypeId} value={adjustmentForm.leaveTypeId} onChange={(e) => setAdjustmentForm({ ...adjustmentForm, leaveTypeId: e.target.value })} />
                <Input id="la-qty" label={tc.qty} type="number" value={adjustmentForm.qty} onChange={(e) => setAdjustmentForm({ ...adjustmentForm, qty: e.target.value })} />
                <Input id="la-reason" label={tc.reason} value={adjustmentForm.reason} onChange={(e) => setAdjustmentForm({ ...adjustmentForm, reason: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createAdjustment}</Button></p>
            </form>
          ) : <p className="t-caption">{t.people.errors.forbidden}</p>}
          {(adjustments ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={adjustmentColumns} rows={adjustments ?? []} caption={tc.tabAdjustments} />}
        </section>
      ) : null}

      {tab === "attendance" ? (
        <section className="card">
          <h2 className="t-title">{tc.attendanceTitle}</h2>
          <form onSubmit={(e) => void submitAttendanceGeneration(e)} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
            <Input id="at-from" label={tc.attendanceFrom} type="date" value={attForm.from} onChange={(e) => setAttForm({ ...attForm, from: e.target.value })} />
            <Input id="at-to" label={tc.attendanceTo} type="date" value={attForm.to} onChange={(e) => setAttForm({ ...attForm, to: e.target.value })} />
            <Button type="submit" loading={busy}>{tc.generateAttendance}</Button>
          </form>
          {(attendance ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={attColumns} rows={attendance ?? []} caption={tc.tabAttendance} />}
        </section>
      ) : null}

      <Modal open={deciding !== null} onClose={() => setDeciding(null)} title={(deciding?.approve ? tc.approve : tc.reject) + " — " + (deciding?.row.id ?? "")} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-caption">{tc.decisionHint}</p>
        <p><Button onClick={() => void decideRequest(deciding?.approve ?? false)} loading={busy}>{deciding?.approve ? tc.approve : tc.reject}</Button></p>
      </Modal>

      <Modal open={applying !== null} onClose={() => setApplying(null)} title={(applying?.approve ? tc.applyAdjustment : tc.discardAdjustment) + " — " + (applying?.row.id ?? "")} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-caption">{tc.decisionHint}</p>
        <p><Button onClick={() => void decideAdjustment(applying?.approve ?? false)} loading={busy}>{applying?.approve ? tc.applyAdjustment : tc.discardAdjustment}</Button></p>
      </Modal>

      <Modal open={dispositionRow !== null} onClose={() => setDispositionRow(null)} title={tc.dispositionTitle} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void submitDisposition(e)}>
          <Input id="at-disp" label={tc.disposition} value={dispositionForm.disposition} onChange={(e) => setDispositionForm({ ...dispositionForm, disposition: e.target.value })} />
          <Input id="at-reason" label={tc.dispositionReason} value={dispositionForm.reason} onChange={(e) => setDispositionForm({ ...dispositionForm, reason: e.target.value })} />
          <p><Button type="submit" loading={busy}>{tc.dispose}</Button></p>
        </form>
      </Modal>
    </div>
  );
}

