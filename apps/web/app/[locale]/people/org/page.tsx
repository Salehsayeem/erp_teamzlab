"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  assignPosition, createOrgUnit, createPosition, createWorkLocation,
  decideAssignmentException, fetchOrgUnits, fetchPositionAssignments, fetchPositions, fetchWorkLocations,
  type OrgUnitRow, type PositionAssignmentRow, type PositionRow, type WorkLocationRow,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Table,
  ToastRegion, type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../_lib/helpers";

export default function OrgPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.org;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canWrite = hasPermission(permissions, "org:write");
  const canDecide = hasPermission(permissions, "payroll:approve");

  const [units, setUnits] = useState<OrgUnitRow[] | null>(null);
  const [locations, setLocations] = useState<WorkLocationRow[] | null>(null);
  const [positions, setPositions] = useState<PositionRow[] | null>(null);
  const [assignments, setAssignments] = useState<PositionAssignmentRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [unit, setUnit] = useState({ code: "", name: "", parentOrgUnitId: "", effectiveFrom: "" });
  const [location, setLocation] = useState({ code: "", name: "", timezone: "Asia/Dhaka" });
  const [position, setPosition] = useState({ code: "", title: "", gradeCode: "", budgetedFte: "1", budgetedHeadcount: "1" });
  const [assign, setAssign] = useState<{ position: PositionRow; employmentId: string; fte: string; costCenter: string; effectiveFrom: string; exceptionReason: string } | null>(null);
  const [pending, setPending] = useState<PositionAssignmentRow | null>(null);
  const [decisionReason, setDecisionReason] = useState("");

  const push = (tone: ToastItem["tone"], message: string) => setToasts((p) => [...p, { id: crypto.randomUUID(), tone, message }]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [u, p, a, l] = await Promise.all([fetchOrgUnits(token), fetchPositions(token), fetchPositionAssignments(token), fetchWorkLocations(token)]);
    if (u.ok && p.ok && a.ok && l.ok) {
      setUnits(u.data);
      setPositions(p.data);
      setAssignments(a.data);
      setLocations(l.data);
      setPhase("success");
    } else {
      setUnits(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function submitUnit(e: FormEvent) {
    e.preventDefault();
    if (!token || !unit.code.trim() || !unit.name.trim() || !unit.effectiveFrom) return;
    setBusy(true); setFormError(null);
    const res = await createOrgUnit(token, { code: unit.code.trim(), name: unit.name.trim(), parentOrgUnitId: unit.parentOrgUnitId.trim() || null, effectiveFrom: unit.effectiveFrom });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", res.data.code);
    setUnit({ code: "", name: "", parentOrgUnitId: "", effectiveFrom: "" });
    void load();
  }

  async function submitLocation(e: FormEvent) {
    e.preventDefault();
    if (!token || !location.code.trim() || !location.name.trim()) return;
    setBusy(true); setFormError(null);
    const res = await createWorkLocation(token, { code: location.code.trim(), name: location.name.trim(), timezone: location.timezone.trim() });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", res.data.code);
    setLocation({ code: "", name: "", timezone: location.timezone });
    void load();
  }

  async function submitPosition(e: FormEvent) {
    e.preventDefault();
    if (!token || !position.code.trim() || !position.title.trim()) return;
    setBusy(true); setFormError(null);
    const res = await createPosition(token, {
      code: position.code.trim(), title: position.title.trim(),
      gradeCode: position.gradeCode.trim() || null,
      budgetedFte: Number(position.budgetedFte) || 0,
      budgetedHeadcount: Number(position.budgetedHeadcount) || 0,
    });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", res.data.code);
    void load();
  }

  async function submitAssign(e: FormEvent) {
    e.preventDefault();
    if (!token || !assign) return;
    setBusy(true); setFormError(null);
    const res = await assignPosition(token, assign.position.id, {
      employmentId: assign.employmentId.trim(), fte: Number(assign.fte) || 0,
      costCenter: assign.costCenter.trim() || null,
      effectiveFrom: assign.effectiveFrom,
      exceptionReason: assign.exceptionReason.trim() || null,
    });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", res.data.status + " · " + res.data.id);
    setAssign(null);
    void load();
  }

  async function submitDecision(approve: boolean) {
    if (!token || !pending || !decisionReason.trim()) return;
    setBusy(true);
    const res = await decideAssignmentException(token, pending.id, { approve, reason: decisionReason.trim() });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", res.data.status);
    setPending(null); setDecisionReason("");
    void load();
  }

  const locationColumns: Column<WorkLocationRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.code, mono: true, render: (l) => l.code },
    { key: "name", label: tc.columns.name, render: (l) => l.name },
    { key: "tz", label: tc.timezone, mono: true, render: (l) => l.timezone },
    { key: "active", label: tc.columns.status, mono: true, render: (l) => (l.active ? "✓" : "—") },
  ], [tc]);

  const unitColumns: Column<OrgUnitRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.code, mono: true, render: (u) => u.code },
    { key: "name", label: tc.columns.name, render: (u) => u.name },
    { key: "parent", label: tc.columns.parent, mono: true, render: (u) => u.parentOrgUnitId ?? "—" },
    { key: "valid", label: tc.columns.valid, mono: true, render: (u) => u.effectiveFrom },
  ], [tc]);

  const pendingRows = (assignments ?? []).filter((a) => a.status === "PendingException");
  const pendingColumns: Column<PositionAssignmentRow>[] = useMemo(() => [
    { key: "id", label: tc.columns.code, mono: true, render: (a) => a.id },
    { key: "worker", label: tc.columns.worker, mono: true, render: (a) => a.employmentId },
    { key: "pos", label: tc.columns.code, mono: true, render: (a) => a.positionId },
    { key: "reason", label: tc.exceptionReason, render: (a) => a.exceptionReason ?? "—" },
    { key: "actions", label: "", render: (a) => canDecide === true ? (
        <Button variant="secondary" onClick={() => setPending(a)}>{tc.assign}</Button>
      ) : null },
  ], [tc, canDecide]);

  const positionColumns: Column<PositionRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.code, mono: true, render: (p) => p.code },
    { key: "title", label: tc.columns.title, render: (p) => p.title },
    { key: "grade", label: tc.columns.grade, mono: true, render: (p) => p.gradeCode ?? "—" },
    { key: "budget", label: tc.columns.budget, mono: true, render: (p) => fmtNumber(p.budgetedFte, locale) },
    { key: "headcount", label: tc.columns.headcount, mono: true, render: (p) => fmtNumber(p.budgetedHeadcount, locale) },
    { key: "incumbents", label: tc.columns.incumbents, mono: true, render: (p) => fmtNumber(p.incumbents, locale) },
    { key: "status", label: tc.columns.status, render: (p) => <Badge tone={peopleTone(p.status)}>{peopleStatusLabel(t, p.status)}</Badge> },
    { key: "actions", label: "", render: (p) => canWrite === true ? (
        <Button variant="secondary" onClick={() => setAssign({ position: p, employmentId: "", fte: "1", costCenter: "", effectiveFrom: new Date().toISOString().slice(0, 10), exceptionReason: "" })}>
          {tc.assign}
        </Button>
      ) : null },
  ], [tc, t, locale, canWrite]);

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

      {canWrite === true ? (
        <div className="grid-2">
          <section className="card">
            <h2 className="t-title">{tc.unitFormTitle}</h2>
            <form onSubmit={(e) => void submitUnit(e)}>
              <div className="form-grid">
                <Input id="org-code" label={tc.code} value={unit.code} onChange={(e) => setUnit({ ...unit, code: e.target.value })} />
                <Input id="org-name" label={tc.name} value={unit.name} onChange={(e) => setUnit({ ...unit, name: e.target.value })} />
                <Input id="org-parent" label={tc.parentUnit} value={unit.parentOrgUnitId} onChange={(e) => setUnit({ ...unit, parentOrgUnitId: e.target.value })} />
                <Input id="org-from" label={tc.effectiveFrom} type="date" value={unit.effectiveFrom} onChange={(e) => setUnit({ ...unit, effectiveFrom: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createUnit}</Button></p>
            </form>
          </section>
          <section className="card">
            <h2 className="t-title">{tc.positionFormTitle}</h2>
            <form onSubmit={(e) => void submitPosition(e)}>
              <div className="form-grid">
                <Input id="pos-code" label={tc.code} value={position.code} onChange={(e) => setPosition({ ...position, code: e.target.value })} />
                <Input id="pos-title" label={tc.positionTitle} value={position.title} onChange={(e) => setPosition({ ...position, title: e.target.value })} />
                <Input id="pos-grade" label={tc.grade} value={position.gradeCode} onChange={(e) => setPosition({ ...position, gradeCode: e.target.value })} />
                <Input id="pos-fte" label={tc.budgetFte} type="number" step="0.5" value={position.budgetedFte} onChange={(e) => setPosition({ ...position, budgetedFte: e.target.value })} />
                <Input id="pos-hc" label={tc.budgetHeadcount} type="number" value={position.budgetedHeadcount} onChange={(e) => setPosition({ ...position, budgetedHeadcount: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createPosition}</Button></p>
            </form>
          </section>
        </div>
      ) : null}

      <section className="card">
        <h2 className="t-title">{tc.unitsTitle}</h2>
        {(units ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={unitColumns} rows={units ?? []} caption={tc.title} />}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.locationFormTitle}</h2>
        {canWrite === true ? (
          <form onSubmit={(e) => void submitLocation(e)} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
            <Input id="loc-code" label={tc.code} value={location.code} onChange={(e) => setLocation({ ...location, code: e.target.value })} />
            <Input id="loc-name" label={tc.name} value={location.name} onChange={(e) => setLocation({ ...location, name: e.target.value })} />
            <Input id="loc-tz" label={tc.timezone} value={location.timezone} onChange={(e) => setLocation({ ...location, timezone: e.target.value })} />
            <Button type="submit" loading={busy} disabled={!meReady}>{tc.createLocation}</Button>
          </form>
        ) : <p className="t-caption">{t.people.errors.forbidden}</p>}
        {(locations ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={locationColumns} rows={locations ?? []} caption={tc.locationFormTitle} />}
      </section>

      {(pendingRows.length > 0 && canDecide === true) ? (
        <section className="card">
          <h2 className="t-title">{tc.exceptionReason}</h2>
          <Table columns={pendingColumns} rows={pendingRows} caption={tc.title} />
        </section>
      ) : null}

      <section className="card">
        <h2 className="t-title">{tc.positionsTitle}</h2>
        {(positions ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={positionColumns} rows={positions ?? []} caption={tc.positionsTitle} />}
      </section>

      <Modal open={assign !== null} onClose={() => setAssign(null)} title={tc.assignTitle + " — " + (assign?.position.code ?? "")} closeLabel={t.common.cancel} busy={busy}>
        {assign ? (
          <form onSubmit={(e) => void submitAssign(e)}>
            <p className="t-caption">{tc.exceptionHint}</p>
            <div className="form-grid">
              <Input id="as-worker" label={tc.workerId} value={assign.employmentId} onChange={(e) => setAssign({ ...assign, employmentId: e.target.value })} />
              <Input id="as-fte" label={tc.fte} type="number" step="0.5" value={assign.fte} onChange={(e) => setAssign({ ...assign, fte: e.target.value })} />
              <Input id="as-cc" label={tc.costCenter} value={assign.costCenter} onChange={(e) => setAssign({ ...assign, costCenter: e.target.value })} />
              <Input id="as-from" label={tc.effectiveFrom} type="date" value={assign.effectiveFrom} onChange={(e) => setAssign({ ...assign, effectiveFrom: e.target.value })} />
              <Input id="as-exc" label={tc.exceptionReason} value={assign.exceptionReason} onChange={(e) => setAssign({ ...assign, exceptionReason: e.target.value })} />
            </div>
            <p><Button type="submit" loading={busy}>{tc.assign}</Button></p>
          </form>
        ) : null}
      </Modal>

      <Modal open={pending !== null} onClose={() => setPending(null)} title={tc.exceptionReason} closeLabel={t.common.cancel} busy={busy}>
        {pending ? (
          <form onSubmit={(e) => { e.preventDefault(); void submitDecision(true); }}>
            <p className="t-caption" aria-live="polite">{pending.employmentId} · {pending.exceptionReason ?? ""}</p>
            <Input id="exc-reason" label={tc.exceptionReason} value={decisionReason} onChange={(e) => setDecisionReason(e.target.value)} />
            <p className="inline-flex" style={{ gap: "0.5rem" }}>
              <Button type="submit" loading={busy}>{t.people.leave.approve}</Button>
              <Button type="button" variant="secondary" onClick={() => void submitDecision(false)}>{t.people.leave.reject}</Button>
            </p>
          </form>
        ) : null}
      </Modal>
    </div>
  );
}
