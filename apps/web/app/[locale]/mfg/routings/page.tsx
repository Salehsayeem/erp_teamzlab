"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  fetchRoutings, createRouting, approveRouting, addRoutingOperation,
  fetchWorkCenters, createWorkCenter, fetchCalendar, addCalendarWindow,
  type RoutingRow, type WorkCenterRow, type CalendarRow,
} from "@/lib/services/mfg-deferred";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge, Button, EmptyState, Input, LoadingSkeleton, Select, Table, Tabs, Modal,
  type TabItem, type Column,
} from "@/components/uikit";
import { hasPermission, deferredActionError, mfgStatusLabel, mfgTone, useMfgMe } from "../_lib/helpers";
import { PageShell, useToasts } from "../_lib/screen";

const TIME_UNITS = ["MIN", "HOUR", "DAY"];

export default function RoutingsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.mfg.routing;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = useMfgMe(token);
  const canWrite = hasPermission(permissions, "mfg:write");
  const canApprove = hasPermission(permissions, "boms:approve");
  const { toasts, push, dismiss } = useToasts();

  const [tab, setTab] = useState("routings");
  const [routings, setRoutings] = useState<RoutingRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [workCenters, setWorkCenters] = useState<WorkCenterRow[]>([]);
  const [banner, setBanner] = useState<string | null>(null);

  // routing draft form
  const [rSku, setRSku] = useState("");
  const [rRev, setRRev] = useState("1");
  const [rFrom, setRFrom] = useState("");
  const [rTo, setRTo] = useState("");
  const [rErrors, setRErrors] = useState<{ sku?: string; rev?: string; from?: string }>({});
  const [creating, setCreating] = useState(false);
  const [approveTarget, setApproveTarget] = useState<RoutingRow | null>(null);
  const [approving, setApproving] = useState(false);

  // operation form (attached to the expanded routing)
  const [openId, setOpenId] = useState<string | null>(null);
  const [opForm, setOpForm] = useState({ operationCode: "", workCenterId: "", requiredCapabilities: "", timeUnit: "MIN", setupMin: "10", perUnitMin: "2", seq: "10", isZeroDuration: false });
  const [opErrors, setOpErrors] = useState<{ code?: string; wc?: string; caps?: string; seq?: string }>({});
  const [opBusy, setOpBusy] = useState(false);

  // work centers tab
  const [wcForm, setWcForm] = useState({ code: "", name: "", capabilities: "", active: true });
  const [wcErrors, setWcErrors] = useState<{ code?: string; name?: string; caps?: string }>({});
  const [wcBusy, setWcBusy] = useState(false);
  const [calWcId, setCalWcId] = useState("");
  const [cal, setCal] = useState<CalendarRow[] | null>(null);
  const [calForm, setCalForm] = useState({ windowStart: "", windowEnd: "", isAvailable: true, source: "manual" });
  const [calErrors, setCalErrors] = useState<{ win?: string; wc?: string }>({});
  const [calBusy, setCalBusy] = useState(false);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    setErrorCode(null);
    const res = await fetchRoutings(token);
    if (res.ok) { setRoutings(res.data); setPhase("success"); }
    else { setRoutings(null); setErrorCode(res.code); setPhase("error"); }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchWorkCenters(token).then((res) => { if (!cancelled && res.ok) setWorkCenters(res.data); });
    return () => { cancelled = true; };
  }, [token]);

  async function submitRouting(e: FormEvent) {
    e.preventDefault();
    const rev = Number(rRev);
    const errs: { sku?: string; rev?: string; from?: string } = {};
    if (!rSku.trim()) errs.sku = tc.errors.skuRequired;
    if (!rRev.trim() || !Number.isInteger(rev) || rev < 1) errs.rev = tc.errors.revisionInvalid;
    if (!rFrom) errs.from = tc.errors.fromRequired;
    setRErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setCreating(true);
    setBanner(null);
    const res = await createRouting(token, { outputSku: rSku.trim(), revision: rev, effectiveFrom: rFrom, effectiveTo: rTo || null });
    setCreating(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.created + " " + res.data.id);
    setRSku(""); setRRev("1"); setRFrom(""); setRTo("");
    await load();
  }

  async function confirmApprove() {
    if (!approveTarget || !token) return;
    setApproving(true);
    setBanner(null);
    const res = await approveRouting(token, approveTarget.id, null);
    setApproving(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.approvedNote);
    setApproveTarget(null);
    await load();
  }

  async function submitOp(e: FormEvent) {
    e.preventDefault();
    if (!openId || !token) return;
    const seq = Number(opForm.seq);
    const errs: { code?: string; wc?: string; caps?: string; seq?: string } = {};
    if (!opForm.operationCode.trim()) errs.code = tc.errors.codeRequired;
    if (!opForm.workCenterId) errs.wc = tc.errors.wcRequired;
    if (!opForm.requiredCapabilities.trim()) errs.caps = tc.errors.capsRequired;
    if (!opForm.seq.trim() || !Number.isInteger(seq) || seq < 1) errs.seq = tc.errors.seqInvalid;
    setOpErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setOpBusy(true);
    setBanner(null);
    const res = await addRoutingOperation(token, openId, {
      operationCode: opForm.operationCode.trim(), workCenterId: opForm.workCenterId,
      requiredCapabilities: opForm.requiredCapabilities.trim(), timeUnit: opForm.timeUnit,
      setupMin: Number(opForm.setupMin) || 0, perUnitMin: Number(opForm.perUnitMin) || 0,
      seq, isZeroDuration: opForm.isZeroDuration,
    });
    setOpBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.opAdded);
    setOpForm({ operationCode: "", workCenterId: opForm.workCenterId, requiredCapabilities: "", timeUnit: opForm.timeUnit, setupMin: "10", perUnitMin: "2", seq: String(seq + 10), isZeroDuration: false });
    await load();
  }

  async function submitWorkCenter(e: FormEvent) {
    e.preventDefault();
    const errs: { code?: string; name?: string; caps?: string } = {};
    if (!wcForm.code.trim()) errs.code = tc.errors.wcCodeRequired;
    if (!wcForm.name.trim()) errs.name = tc.errors.nameRequired;
    if (!wcForm.capabilities.trim()) errs.caps = tc.errors.capsRequired;
    setWcErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setWcBusy(true);
    setBanner(null);
    const res = await createWorkCenter(token, { code: wcForm.code.trim(), name: wcForm.name.trim(), capabilities: wcForm.capabilities.trim(), active: wcForm.active });
    setWcBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.wcCreated);
    setWcForm({ code: "", name: "", capabilities: "", active: true });
    const fresh = await fetchWorkCenters(token);
    if (fresh.ok) setWorkCenters(fresh.data);
  }

  async function loadCalendar(id: string) {
    setCalWcId(id);
    setCal(null);
    if (!id || !token) return;
    const res = await fetchCalendar(token, id);
    setCal(res.ok ? res.data : []);
  }

  async function submitWindow(e: FormEvent) {
    e.preventDefault();
    if (!calWcId || !token) { setCalErrors({ wc: tc.errors.wcRequired }); return; }
    if (!calForm.windowStart || !calForm.windowEnd || calForm.windowEnd <= calForm.windowStart) {
      setCalErrors({ win: tc.errors.windowInvalid });
      return;
    }
    setCalErrors({});
    setCalBusy(true);
    setBanner(null);
    const res = await addCalendarWindow(token, calWcId, calForm);
    setCalBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.windowAdded);
    setCalForm({ windowStart: "", windowEnd: "", isAvailable: true, source: "manual" });
    await loadCalendar(calWcId);
  }

  const opColumns = (ops: RoutingRow["operations"]): Column<RoutingRow["operations"][number]>[] => [
    { key: "seq", label: tc.seq, mono: true, render: (o) => o.seq },
    { key: "code", label: tc.operationCode, mono: true, render: (o) => o.operationCode },
    { key: "wc", label: tc.workCenter, mono: true, render: (o) => o.workCenterId },
    { key: "caps", label: tc.capabilities, mono: true, render: (o) => o.requiredCapabilities },
    { key: "time", label: tc.setupMin + " / " + tc.perUnitMin, mono: true, render: (o) => fmtNumber(o.setupMin, locale) + " / " + fmtNumber(o.perUnitMin, locale) + " " + o.timeUnit },
    { key: "zero", label: tc.zeroDuration, render: (o) => (o.isZeroDuration ? <Badge tone="muted">tc.zeroDuration</Badge> : "—") },
  ];

  const routingColumns: Column<RoutingRow>[] = useMemo(() => [
    { key: "id", label: tc.columns.routing, mono: true, render: (r) => (
      <button type="button" className="sidebar__link" style={{ inlineSize: "auto", padding: 0 }} onClick={() => setOpenId(openId === r.id ? null : r.id)}>
        {r.id}
      </button>
    ) },
    { key: "sku", label: tc.columns.sku, mono: true, render: (r) => r.outputSku },
    { key: "rev", label: tc.columns.revision, mono: true, render: (r) => r.revision },
    { key: "status", label: tc.columns.status, render: (r) => <Badge tone={mfgTone(r.status)}>{mfgStatusLabel(t, r.status)}</Badge> },
    { key: "ops", label: tc.columns.ops, mono: true, render: (r) => r.operations.length },
    { key: "actions", label: tc.approve, render: (r) => (
      r.status === "Draft" && canApprove !== false && meReady ? (
        <Button small onClick={() => setApproveTarget(r)}>{tc.approve}</Button>
      ) : null
    ) },
  ], [t, tc, canApprove, meReady, openId]);

  const wcColumns: Column<WorkCenterRow>[] = useMemo(() => [
    { key: "code", label: tc.wcColumns.code, mono: true, render: (w) => w.code },
    { key: "name", label: tc.wcColumns.name, render: (w) => w.name },
    { key: "caps", label: tc.wcColumns.capabilities, mono: true, render: (w) => w.capabilities },
    { key: "active", label: tc.wcColumns.active, render: (w) => (w.active ? <Badge tone="success">{tc.wcActive}</Badge> : <Badge tone="muted">{tc.outage}</Badge>) },
  ], [tc]);

  const calColumns: Column<CalendarRow>[] = useMemo(() => [
    { key: "start", label: tc.windowStart, mono: true, render: (c) => c.windowStart },
    { key: "end", label: tc.windowEnd, mono: true, render: (c) => c.windowEnd },
    { key: "avail", label: tc.available + "/" + tc.outage, render: (c) => (c.isAvailable ? <Badge tone="success">{tc.available}</Badge> : <Badge tone="danger">{tc.outage}</Badge>) },
    { key: "src", label: tc.operationCode, render: (c) => c.source },
  ], [tc]);

  const wcOptions = workCenters.map((w) => ({ value: w.id, label: w.code + " — " + w.name }));
  const tabs: TabItem[] = [
    { id: "routings", label: tc.title, content: (
      <div className="stack">
        <section className="card">
          <h2 className="t-title">{tc.createTitle}</h2>
          <form onSubmit={(e) => void submitRouting(e)}>
            <div className="form-grid">
              <Input id="rt-sku" label={tc.outputSku} value={rSku} onChange={(e) => setRSku(e.target.value)} error={rErrors.sku} />
              <Input id="rt-rev" label={tc.revision} type="number" min="1" step="1" value={rRev} onChange={(e) => setRRev(e.target.value)} error={rErrors.rev} />
              <Input id="rt-from" label={tc.effectiveFrom} type="date" value={rFrom} onChange={(e) => setRFrom(e.target.value)} error={rErrors.from} />
              <Input id="rt-to" label={tc.effectiveTo} type="date" value={rTo} onChange={(e) => setRTo(e.target.value)} />
            </div>
            <p><Button type="submit" loading={creating} disabled={!meReady || canWrite === false}>{tc.create}</Button></p>
          </form>
        </section>

        <section className="card">
          {(routings ?? []).length === 0 ? <EmptyState title={tc.empty} /> : (
            <div className="stack">
              <Table columns={routingColumns} rows={routings ?? []} caption={tc.title} />
              {openId !== null ? (() => {
                const open = (routings ?? []).find((r) => r.id === openId);
                if (!open) return null;
                return (
                  <div className="stack">
                    <h3 className="t-body-strong">{tc.opTitle} — <span className="mono">{open.id}</span></h3>
                    {open.operations.length > 0 ? <Table columns={opColumns(open.operations)} rows={open.operations} caption={tc.opTitle} /> : <p className="t-caption">{tc.errors.codeRequired}</p>}
                    <form onSubmit={(e) => void submitOp(e)}>
                      <div className="form-grid">
                        <Input id="rt-op-code" label={tc.operationCode} value={opForm.operationCode} onChange={(e) => setOpForm({ ...opForm, operationCode: e.target.value })} error={opErrors.code} />
                        <Select id="rt-op-wc" label={tc.workCenter} value={opForm.workCenterId} onChange={(e) => setOpForm({ ...opForm, workCenterId: e.target.value })} options={wcOptions} placeholder={tc.workCenter} error={opErrors.wc} />
                        <Input id="rt-op-caps" label={tc.capabilities} value={opForm.requiredCapabilities} onChange={(e) => setOpForm({ ...opForm, requiredCapabilities: e.target.value })} error={opErrors.caps} />
                        <Select id="rt-op-unit" label={tc.timeUnit} value={opForm.timeUnit} onChange={(e) => setOpForm({ ...opForm, timeUnit: e.target.value })} options={TIME_UNITS.map((u) => ({ value: u, label: u }))} />
                        <Input id="rt-op-setup" label={tc.setupMin} type="number" min="0" step="any" value={opForm.setupMin} onChange={(e) => setOpForm({ ...opForm, setupMin: e.target.value })} />
                        <Input id="rt-op-perunit" label={tc.perUnitMin} type="number" min="0" step="any" value={opForm.perUnitMin} onChange={(e) => setOpForm({ ...opForm, perUnitMin: e.target.value })} />
                        <Input id="rt-op-seq" label={tc.seq} type="number" min="1" step="1" value={opForm.seq} onChange={(e) => setOpForm({ ...opForm, seq: e.target.value })} error={opErrors.seq} />
                      </div>
                      <p className="t-caption">
                        <label htmlFor="rt-op-zero"><input id="rt-op-zero" type="checkbox" checked={opForm.isZeroDuration} onChange={(e) => setOpForm({ ...opForm, isZeroDuration: e.target.checked })} /> {tc.zeroDuration}</label>
                      </p>
                      <p><Button type="submit" small loading={opBusy} disabled={!meReady || canWrite === false}>{tc.addOp}</Button></p>
                    </form>
                  </div>
                );
              })() : null}
            </div>
          )}
        </section>
      </div>
    ) },
    { id: "work-centers", label: tc.workCentersTitle, content: (
      <div className="stack">
        <section className="card">
          <h2 className="t-title">{tc.workCentersTitle}</h2>
          <form onSubmit={(e) => void submitWorkCenter(e)}>
            <div className="form-grid">
              <Input id="wc-code" label={tc.wcCode} value={wcForm.code} onChange={(e) => setWcForm({ ...wcForm, code: e.target.value })} error={wcErrors.code} />
              <Input id="wc-name" label={tc.wcName} value={wcForm.name} onChange={(e) => setWcForm({ ...wcForm, name: e.target.value })} error={wcErrors.name} />
              <Input id="wc-caps" label={tc.wcCapabilities} value={wcForm.capabilities} onChange={(e) => setWcForm({ ...wcForm, capabilities: e.target.value })} error={wcErrors.caps} />
            </div>
            <p><Button type="submit" small loading={wcBusy} disabled={!meReady || canWrite === false}>{tc.wcCreate}</Button></p>
          </form>
          {workCenters.length > 0 ? <Table columns={wcColumns} rows={workCenters} caption={tc.workCentersTitle} /> : null}
        </section>
        <section className="card">
          <h2 className="t-title">{tc.calendarTitle}</h2>
          <div className="form-grid">
            <Select id="wc-cal-picker" label={tc.workCentersTitle} value={calWcId} onChange={(e) => void loadCalendar(e.target.value)} options={wcOptions} placeholder={tc.workCentersTitle} error={calErrors.wc} />
          </div>
          {cal === null ? null : cal.length === 0 ? <EmptyState title={tc.empty} /> : (
            <Table columns={calColumns} rows={cal} caption={tc.calendarTitle} />
          )}
          <form onSubmit={(e) => void submitWindow(e)}>
            <div className="form-grid">
              <Input id="wc-cal-start" label={tc.windowStart} type="datetime-local" value={calForm.windowStart} onChange={(e) => setCalForm({ ...calForm, windowStart: e.target.value })} />
              <Input id="wc-cal-end" label={tc.windowEnd} type="datetime-local" value={calForm.windowEnd} onChange={(e) => setCalForm({ ...calForm, windowEnd: e.target.value })} error={calErrors.win} />
            </div>
            <p className="t-caption">
              <label htmlFor="wc-cal-avail"><input id="wc-cal-avail" type="checkbox" checked={calForm.isAvailable} onChange={(e) => setCalForm({ ...calForm, isAvailable: e.target.checked })} /> {tc.available}</label>
            </p>
            <p><Button type="submit" small loading={calBusy} disabled={!meReady || canWrite === false}>{tc.addWindow}</Button></p>
          </form>
        </section>
      </div>
    ) },
  ];

  if (!ready || (phase === "loading" && routings === null)) return <LoadingSkeleton rows={6} />;
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
      <Modal open={approveTarget !== null} onClose={() => setApproveTarget(null)} title={tc.approveConfirmTitle} closeLabel={t.common.cancel} busy={approving}>
        <p>{tc.approveConfirm}</p>
        {approveTarget ? <p className="t-caption mono">{approveTarget.id}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setApproveTarget(null)}>{t.common.cancel}</Button>
          <Button loading={approving} onClick={() => void confirmApprove()}>{tc.approve}</Button>
        </div>
      </Modal>
    </PageShell>
  );
}
