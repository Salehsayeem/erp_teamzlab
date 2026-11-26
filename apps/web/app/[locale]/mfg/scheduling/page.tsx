"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createScheduleRun, fetchScheduleRuns, fetchSchedulePlacements, fetchScheduleConflicts,
  publishScheduleRun, createScenario, movePlacement,
  ingestMaintenanceEvent, fetchScheduleImpacts, resolveScheduleImpact,
  type ScheduleRunListRow, type PlacementRow,
} from "@/lib/services/mfg-deferred";
import { fetchProductionOrders } from "@/lib/services/mfg";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge, Button, EmptyState, Input, LoadingSkeleton, Modal, Select, Table, Tabs,
  type TabItem, type Column,
} from "@/components/uikit";
import { hasPermission, deferredActionError, mfgTone, mfgStatusLabel, useMfgMe } from "../_lib/helpers";
import { PageShell, useToasts } from "../_lib/screen";

export default function SchedulingPage() {
  const params = useParams<{ locale: string }>();
  const pageLocale: Locale = isLocale(params.locale) ? params.locale : "en";
  const locale = pageLocale;
  const t = getDictionary(locale);
  const tc = t.mfg.sched;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = useMfgMe(token);
  const canSchedule = hasPermission(permissions, "mfg:schedule");
  const { toasts, push, dismiss } = useToasts();

  const [tab, setTab] = useState("board");
  const [runs, setRuns] = useState<ScheduleRunListRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [orders, setOrders] = useState<{ id: string; orderNo: string }[]>([]);
  const [banner, setBanner] = useState<string | null>(null);

  const [runForm, setRunForm] = useState({ orderId: "", mode: "Finite", horizonFrom: "", horizonTo: "", calendarVersion: "CAL-2026", algorithmVersion: "FWD-1" });
  const [runErrors, setRunErrors] = useState<{ order?: string; horizon?: string }>({});
  const [runBusy, setRunBusy] = useState(false);
  const [selectedRun, setSelectedRun] = useState("");
  const [placements, setPlacements] = useState<PlacementRow[] | null>(null);
  const [conflicts, setConflicts] = useState<{ code: string; severity: string; detail: string; resolved: boolean }[] | null>(null);
  const [publishTarget, setPublishTarget] = useState("");
  const [publishOverride, setPublishOverride] = useState("");
  const [publishing, setPublishing] = useState(false);

  const [scenForm, setScenForm] = useState({ name: "", baseRunId: "", frozenFrom: "", frozenTo: "" });
  const [scenErrors, setScenErrors] = useState<{ name?: string; run?: string }>({});
  const [scenBusy, setScenBusy] = useState(false);
  const [moveForm, setMoveForm] = useState({ placementId: "", newStartAt: "", overrideReason: "" });
  const [moveErrors, setMoveErrors] = useState<{ placement?: string; start?: string }>({});
  const [moveBusy, setMoveBusy] = useState(false);

  // MF7: maintenance outages + planner review
  const [maintForm, setMaintForm] = useState({ sourceEventId: "", revision: "1", resourceCode: "", windowStart: "", windowEnd: "", reason: "" });
  const [maintErrors, setMaintErrors] = useState<{ source?: string; resource?: string; window?: string }>({});
  const [maintBusy, setMaintBusy] = useState(false);
  const [impacts, setImpacts] = useState<{ rows: { placementId: string; orderId: string; workCenterId: string; overlapMinutes: number; disposition: string }[] } | null>(null);
  const [impactOutage, setImpactOutage] = useState("");
  const [resBusy, setResBusy] = useState(false);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    setErrorCode(null);
    const res = await fetchScheduleRuns(token);
    if (res.ok) { setRuns(res.data); setPhase("success"); }
    else { setRuns(null); setErrorCode(res.code); setPhase("error"); }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchProductionOrders(token).then((res) => {
      if (!cancelled && res.ok) setOrders(res.data.map((o) => ({ id: o.id, orderNo: o.orderNo })));
    });
    return () => { cancelled = true; };
  }, [token]);

  useEffect(() => {
    if (!token || !selectedRun) { setPlacements(null); setConflicts(null); return; }
    let cancelled = false;
    void (async () => {
      const [p, c] = await Promise.all([fetchSchedulePlacements(token, selectedRun), fetchScheduleConflicts(token, selectedRun)]);
      if (!cancelled) { setPlacements(p.ok ? p.data : []); setConflicts(c.ok ? c.data : []); }
    })();
    return () => { cancelled = true; };
  }, [token, selectedRun]);

  async function submitRun(e: FormEvent) {
    e.preventDefault();
    const errs: { order?: string; horizon?: string } = {};
    if (!runForm.orderId) errs.order = tc.errors.orderRequired;
    if (!runForm.horizonFrom || !runForm.horizonTo || runForm.horizonTo < runForm.horizonFrom) errs.horizon = tc.errors.horizonInvalid;
    setRunErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setRunBusy(true);
    setBanner(null);
    const res = await createScheduleRun(token, runForm);
    setRunBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.runCreated + " " + res.data.id);
    setSelectedRun(res.data.id);
    setRunForm({ ...runForm, orderId: "" });
    await load();
  }

  async function confirmPublish() {
    if (!publishTarget || !token) return;
    setPublishing(true);
    setBanner(null);
    const res = await publishScheduleRun(token, publishTarget, publishOverride.trim() || null);
    setPublishing(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.publishedNote);
    setPublishTarget("");
    setPublishOverride("");
  }

  async function submitScenario(e: FormEvent) {
    e.preventDefault();
    const errs: { name?: string; run?: string } = {};
    if (!scenForm.name.trim()) errs.name = tc.errors.nameRequired;
    if (!scenForm.baseRunId) errs.run = tc.errors.runRequired;
    setScenErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setScenBusy(true);
    setBanner(null);
    const res = await createScenario(token, { name: scenForm.name.trim(), baseRunId: scenForm.baseRunId, frozenFrom: scenForm.frozenFrom || null, frozenTo: scenForm.frozenTo || null });
    setScenBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.scenarioCreated);
    setScenForm({ name: "", baseRunId: scenForm.baseRunId, frozenFrom: "", frozenTo: "" });
  }

  async function submitMove(e: FormEvent) {
    e.preventDefault();
    const errs: { placement?: string; start?: string } = {};
    if (!moveForm.placementId) errs.placement = tc.errors.placementRequired;
    if (!moveForm.newStartAt) errs.start = tc.errors.startRequired;
    setMoveErrors(errs);
    if (Object.keys(errs).length > 0 || !token || !selectedRun) return;
    setMoveBusy(true);
    setBanner(null);
    const res = await movePlacement(token, selectedRun, {
      placementId: moveForm.placementId, newStartAt: moveForm.newStartAt,
      overrideReason: moveForm.overrideReason.trim() || null, overrideApprover: null,
    });
    setMoveBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.scenarioCreated);
    setMoveForm({ placementId: "", newStartAt: "", overrideReason: "" });
    if (selectedRun) {
      const p = await fetchSchedulePlacements(token, selectedRun);
      if (p.ok) setPlacements(p.data);
    }
  }

  async function submitMaintenance(e: FormEvent) {
    e.preventDefault();
    const rev = Number(maintForm.revision);
    const errs: { source?: string; resource?: string; window?: string } = {};
    if (!maintForm.sourceEventId.trim()) errs.source = tc.errors.sourceRequired;
    if (!maintForm.resourceCode.trim()) errs.resource = tc.errors.resourceRequired;
    if (!maintForm.windowStart || !maintForm.windowEnd || maintForm.windowEnd <= maintForm.windowStart) errs.window = tc.errors.windowInvalid;
    setMaintErrors(errs);
    if (Object.keys(errs).length > 0 || !token || !Number.isInteger(rev) || rev < 1) return;
    setMaintBusy(true);
    setBanner(null);
    const res = await ingestMaintenanceEvent(token, {
      sourceEventId: maintForm.sourceEventId.trim(), revision: rev, resourceCode: maintForm.resourceCode.trim(),
      assetId: null, windowStart: maintForm.windowStart, windowEnd: maintForm.windowEnd, reason: maintForm.reason.trim() || null,
    });
    setMaintBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.ingestedNote);
    setImpactOutage(maintForm.sourceEventId.trim());
    setMaintForm({ sourceEventId: "", revision: "1", resourceCode: "", windowStart: "", windowEnd: "", reason: "" });
  }

  async function loadImpacts() {
    if (!impactOutage.trim() || !token) return;
    const res = await fetchScheduleImpacts(token, impactOutage.trim());
    setImpacts(res.ok ? res.data : { rows: [] });
  }

  async function resolveImpact(row: { orderId: string; workCenterId: string }, disposition: string) {
    if (!impactOutage.trim() || !token) return;
    setResBusy(true);
    setBanner(null);
    const res = await resolveScheduleImpact(token, {
      outageEventId: impactOutage.trim(), scheduleRunId: selectedRun || "", orderId: row.orderId,
      workCenterId: row.workCenterId, disposition, reason: null,
    });
    setResBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.resolvedNote);
    await loadImpacts();
  }

  const runColumns: Column<ScheduleRunListRow>[] = [
    { key: "id", label: tc.colOp, mono: true, render: (r) => (
      <button type="button" className="sidebar__link" style={{ inlineSize: "auto", padding: 0 }} onClick={() => setSelectedRun(r.id)}>{r.id}</button>
    ) },
    { key: "order", label: tc.orderId, mono: true, render: (r) => orders.find((o) => o.id === r.orderId)?.orderNo ?? r.orderId },
    { key: "mode", label: tc.mode, render: (r) => <Badge tone={r.mode === "Finite" ? "success" : "warning"}>{r.mode === "Finite" ? tc.modeFinite : tc.modeInfinite}</Badge> },
    { key: "status", label: tc.colStatus, render: (r) => <Badge tone={mfgTone(r.status)}>{mfgStatusLabel(t, r.status)}</Badge> },
    { key: "horizon", label: tc.horizonFrom + " — " + tc.horizonTo, mono: true, render: (r) => r.horizonFrom + " → " + r.horizonTo },
    { key: "pub", label: tc.publish, render: (r) => (canSchedule !== false && meReady ? <Button small onClick={() => setPublishTarget(r.id)}>{tc.publish}</Button> : null) },
  ];

  const placementColumns: Column<PlacementRow>[] = [
    { key: "seq", label: tc.colSeq, mono: true, render: (p) => p.seq },
    { key: "op", label: tc.colOp, mono: true, render: (p) => p.operationCode },
    { key: "wc", label: tc.colWc, mono: true, render: (p) => p.workCenterId },
    { key: "start", label: tc.colStart, mono: true, render: (p) => p.startAt ?? "—" },
    { key: "end", label: tc.colEnd, mono: true, render: (p) => p.endAt ?? "—" },
    { key: "state", label: tc.colStatus, render: (p) => (p.scheduled ? <Badge tone="success">{tc.scheduled}</Badge> : <Badge tone="warning">{tc.unscheduled}{p.unscheduledReason ? ": " + p.unscheduledReason : ""}</Badge>) },
  ];

  const orderOptions = orders.map((o) => ({ value: o.id, label: o.orderNo }));
  const placementOptions = (placements ?? []).filter((p) => p.scheduled).map((p) => ({ value: p.id, label: "#" + p.seq + " " + p.operationCode }));
  const runOptions = (runs ?? []).map((r) => ({ value: r.id, label: r.id }));
  const tabs: TabItem[] = [
    { id: "board", label: tc.placementsTitle, content: (
      <div className="stack">
        <section className="card">
          <h2 className="t-title">{tc.runTitle}</h2>
          <form onSubmit={(e) => void submitRun(e)}>
            <div className="form-grid">
              <Select id="sc-order" label={tc.orderId} value={runForm.orderId} onChange={(e) => setRunForm({ ...runForm, orderId: e.target.value })} options={orderOptions} placeholder={tc.orderId} error={runErrors.order} />
              <Select id="sc-mode" label={tc.mode} value={runForm.mode} onChange={(e) => setRunForm({ ...runForm, mode: e.target.value })} options={[{ value: "Finite", label: tc.modeFinite }, { value: "Infinite", label: tc.modeInfinite }]} />
              <Input id="sc-from" label={tc.horizonFrom} type="date" value={runForm.horizonFrom} onChange={(e) => setRunForm({ ...runForm, horizonFrom: e.target.value })} />
              <Input id="sc-to" label={tc.horizonTo} type="date" value={runForm.horizonTo} onChange={(e) => setRunForm({ ...runForm, horizonTo: e.target.value })} error={runErrors.horizon} />
              <Input id="sc-cal" label={tc.calendarVersion} value={runForm.calendarVersion} onChange={(e) => setRunForm({ ...runForm, calendarVersion: e.target.value })} />
              <Input id="sc-alg" label={tc.algorithmVersion} value={runForm.algorithmVersion} onChange={(e) => setRunForm({ ...runForm, algorithmVersion: e.target.value })} />
            </div>
            <p><Button type="submit" loading={runBusy} disabled={!meReady || canSchedule === false}>{tc.createRun}</Button></p>
          </form>
        </section>
        <section className="card">
          {(runs ?? []).length === 0 ? <EmptyState title={tc.empty} /> : (
            <Table columns={runColumns} rows={runs ?? []} caption={tc.title} />
          )}
        </section>
        <section className="card">
          <h2 className="t-title">{tc.placementsTitle}{selectedRun ? <span className="mono"> — {selectedRun}</span> : null}</h2>
          {placements === null ? <p className="t-caption">{tc.empty}</p> : placements.length === 0 ? <EmptyState title={tc.empty} /> : (
            <Table columns={placementColumns} rows={placements} caption={tc.placementsTitle} />
          )}
          <h3 className="t-body-strong">{tc.conflictsTitle}</h3>
          {conflicts === null ? null : conflicts.length === 0 ? <p className="t-caption">{tc.noConflicts}</p> : (
            <ul>
              {conflicts.map((c, i) => (
                <li key={i} className="t-caption">
                  <Badge tone={c.severity === "Hard" ? "danger" : "warning"}>{c.code}</Badge> {c.detail}{c.resolved ? " ✓" : ""}
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
    ) },
    { id: "scenarios", label: tc.scenarioTitle, content: (
      <div className="stack">
        <section className="card">
          <h2 className="t-title">{tc.scenarioTitle}</h2>
          <form onSubmit={(e) => void submitScenario(e)}>
            <div className="form-grid">
              <Input id="scn-name" label={tc.scenarioName} value={scenForm.name} onChange={(e) => setScenForm({ ...scenForm, name: e.target.value })} error={scenErrors.name} />
              <Select id="scn-run" label={tc.runTitle} value={scenForm.baseRunId} onChange={(e) => setScenForm({ ...scenForm, baseRunId: e.target.value })} options={runOptions} placeholder={tc.runTitle} error={scenErrors.run} />
              <Input id="scn-ff" label={tc.frozenFrom} type="datetime-local" value={scenForm.frozenFrom} onChange={(e) => setScenForm({ ...scenForm, frozenFrom: e.target.value })} />
              <Input id="scn-ft" label={tc.frozenTo} type="datetime-local" value={scenForm.frozenTo} onChange={(e) => setScenForm({ ...scenForm, frozenTo: e.target.value })} />
            </div>
            <p><Button type="submit" small loading={scenBusy} disabled={!meReady || canSchedule === false}>{tc.createScenario}</Button></p>
          </form>
          <form onSubmit={(e) => void submitMove(e)}>
            <h3 className="t-body-strong">{tc.placementsTitle}</h3>
            <div className="form-grid">
              <Select id="scn-place" label={tc.colOp} value={moveForm.placementId} onChange={(e) => setMoveForm({ ...moveForm, placementId: e.target.value })} options={placementOptions} placeholder={tc.colOp} error={moveErrors.placement} />
              <Input id="scn-start" label={tc.colStart} type="datetime-local" value={moveForm.newStartAt} onChange={(e) => setMoveForm({ ...moveForm, newStartAt: e.target.value })} error={moveErrors.start} />
              <Input id="scn-ovr" label={tc.overrideReason} value={moveForm.overrideReason} onChange={(e) => setMoveForm({ ...moveForm, overrideReason: e.target.value })} />
            </div>
            <p><Button type="submit" small loading={moveBusy} disabled={!meReady || canSchedule === false || !selectedRun}>{tc.createScenario}</Button></p>
          </form>
        </section>
        <section className="card">
          <h2 className="t-title">{tc.maintenanceTitle}</h2>
          <form onSubmit={(e) => void submitMaintenance(e)}>
            <div className="form-grid">
              <Input id="mt-src" label={tc.maintSource} value={maintForm.sourceEventId} onChange={(e) => setMaintForm({ ...maintForm, sourceEventId: e.target.value })} error={maintErrors.source} />
              <Input id="mt-rev" label={tc.maintRevision} type="number" min="1" step="1" value={maintForm.revision} onChange={(e) => setMaintForm({ ...maintForm, revision: e.target.value })} />
              <Input id="mt-res" label={tc.maintResource} value={maintForm.resourceCode} onChange={(e) => setMaintForm({ ...maintForm, resourceCode: e.target.value })} error={maintErrors.resource} />
              <Input id="mt-from" label={tc.maintFrom} type="datetime-local" value={maintForm.windowStart} onChange={(e) => setMaintForm({ ...maintForm, windowStart: e.target.value })} />
              <Input id="mt-to" label={tc.maintTo} type="datetime-local" value={maintForm.windowEnd} onChange={(e) => setMaintForm({ ...maintForm, windowEnd: e.target.value })} error={maintErrors.window} />
              <Input id="mt-reason" label={tc.overrideReason} value={maintForm.reason} onChange={(e) => setMaintForm({ ...maintForm, reason: e.target.value })} />
            </div>
            <p><Button type="submit" small loading={maintBusy} disabled={!meReady || canSchedule === false}>{tc.ingestMaintenance}</Button></p>
          </form>
          <div className="form-grid">
            <Input id="mt-impact" label={tc.outageLabel} value={impactOutage} onChange={(e) => setImpactOutage(e.target.value)} />
          </div>
          <p><Button variant="secondary" small onClick={() => void loadImpacts()} disabled={!meReady}>{tc.loadImpacts}</Button></p>
          {impacts === null ? null : impacts.rows.length === 0 ? <p className="t-caption">{tc.noConflicts}</p> : (
            <Table
              columns={[
                { key: "place", label: tc.colOp, mono: true, render: (r) => r.placementId },
                { key: "order", label: tc.orderId, mono: true, render: (r) => r.orderId },
                { key: "wc", label: tc.colWc, mono: true, render: (r) => r.workCenterId },
                { key: "overlap", label: tc.colOverlap, mono: true, render: (r) => fmtNumber(r.overlapMinutes, locale) },
                { key: "disp", label: tc.colDisposition, render: (r) => (
                  <div className="row-actions">
                    <span className="t-caption">{r.disposition === "None" ? "—" : r.disposition}</span>
                    {canSchedule !== false && meReady ? (
                      <>
                        <Button small variant="secondary" loading={resBusy} onClick={() => void resolveImpact(r, "rescheduled")}>{tc.reschedule}</Button>
                        <Button small variant="secondary" loading={resBusy} onClick={() => void resolveImpact(r, "planner_exception")}>{tc.plannerException}</Button>
                      </>
                    ) : null}
                  </div>
                ) },
              ]}
              rows={impacts.rows}
              caption={tc.impactsTitle}
            />
          )}
        </section>
      </div>
    ) },
  ];

  if (!ready || (phase === "loading" && runs === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    return (
      <PageShell
        title={tc.title} subtitle={tc.subtitle} loading={false}
        error errorCode={errorCode} onRetry={() => void load()}
        forbidden={errorCode === "FORBIDDEN" || errorCode === "HTTP_403" ? tc.forbidden : tc.loadFailed}
        toasts={toasts} onDismiss={dismiss} banner={null}
        loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
      />
    );
  }
  return (
    <PageShell
      title={tc.title} subtitle={tc.subtitle} loading={false} error={false} errorCode={null}
      onRetry={() => void load()} forbidden={tc.forbidden}
      toasts={toasts} onDismiss={dismiss} banner={banner}
      loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
    >
      <Tabs tabs={tabs} activeId={tab} onChange={setTab} label={tc.title} />
      <Modal open={publishTarget !== ""} onClose={() => setPublishTarget("")} title={tc.publishConfirmTitle} closeLabel={t.common.cancel} busy={publishing}>
        <p>{tc.publishConfirm}</p>
        {publishTarget ? <p className="t-caption mono">{publishTarget}</p> : null}
        <Input id="sc-pub-ovr" label={tc.overrideReason} value={publishOverride} onChange={(e) => setPublishOverride(e.target.value)} />
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setPublishTarget("")}>{t.common.cancel}</Button>
          <Button loading={publishing} onClick={() => void confirmPublish()}>{tc.publish}</Button>
        </div>
      </Modal>
    </PageShell>
  );
}
