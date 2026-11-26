"use client";

import { useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  reportLaborTime, fetchLaborTime, approveLaborTime,
  reportCounterEvent, fetchCounterEvents,
  reportDowntimeEvent, fetchDowntimeEvents,
  fetchWipProjection, transferWip,
  proposeCorrection, fetchCorrections, approveCorrection,
  type LaborRow, type CounterRow, type DowntimeRow, type WipProjectionRow, type CorrectionRow,
} from "@/lib/services/mfg-deferred";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge, Button, EmptyState, Input, LoadingSkeleton, Table, Tabs,
  type TabItem, type Column,
} from "@/components/uikit";
import { hasPermission, deferredActionError, mfgStatusLabel, useMfgMe } from "../_lib/helpers";
import { PageShell, useToasts } from "../_lib/screen";

export default function ShopFloorPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.mfg.shopfloor;
  const { token, ready } = useAuth(locale);
  const { permissions, meId, meReady } = useMfgMe(token);
  const canShop = hasPermission(permissions, "mfg:shopfloor");
  const { toasts, push, dismiss } = useToasts();

  const [tab, setTab] = useState("reporting");
  const [phase, setPhase] = useState<"loading" | "error" | "success">("success");
  const [errorCode] = useState<string | null>(null);
  const [banner, setBanner] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [orderId, setOrderId] = useState("");

  const [labor, setLabor] = useState<LaborRow[] | null>(null);
  const [laborForm, setLaborForm] = useState({ workerEmploymentId: "", startAt: "", endAt: "" });
  const [laborErrors, setLaborErrors] = useState<{ worker?: string; time?: string; order?: string }>({});
  const [counters, setCounters] = useState<CounterRow[] | null>(null);
  const [counterForm, setCounterForm] = useState({ sourceEventId: "", counterValue: "", workCenterId: "" });
  const [counterErrors, setCounterErrors] = useState<{ source?: string; value?: string; order?: string }>({});
  const [downtime, setDowntime] = useState<DowntimeRow[] | null>(null);
  const [downForm, setDownForm] = useState({ workCenterId: "", startAt: "", endAt: "", reasonCode: "" });
  const [downErrors, setDownErrors] = useState<{ wc?: string; time?: string; reason?: string; order?: string }>({});

  const [wip, setWip] = useState<WipProjectionRow | null>(null);
  const [wipForm, setWipForm] = useState({ fromSeq: "", toSeq: "", qty: "", deviationReason: "" });
  const [wipErrors, setWipErrors] = useState<{ qty?: string; seq?: string; order?: string }>({});
  const [corrections, setCorrections] = useState<CorrectionRow[] | null>(null);
  const [corrForm, setCorrForm] = useState({ targetId: "", reason: "", compensatingJson: "{}" });
  const [corrErrors, setCorrErrors] = useState<{ target?: string; reason?: string; json?: string }>({});

  function requireOrder(): string | null {
    const id = orderId.trim();
    if (!id) {
      setBanner(tc.errors.orderRequired);
      return null;
    }
    return id;
  }

  async function loadAll() {
    if (!token) return;
    const id = requireOrder();
    if (!id) return;
    setBusy(true);
    const [l, c, d, w] = await Promise.all([
      fetchLaborTime(token, id), fetchCounterEvents(token, id), fetchDowntimeEvents(token, id), fetchWipProjection(token, id),
    ]);
    setBusy(false);
    setLabor(l.ok ? l.data : null);
    setCounters(c.ok ? c.data : null);
    setDowntime(d.ok ? d.data : null);
    setWip(w.ok ? w.data : null);
  }

  async function submitLabor(e: FormEvent) {
    e.preventDefault();
    const id = requireOrder();
    if (!id || !token) { setLaborErrors((prev) => ({ ...prev, order: tc.errors.orderRequired })); return; }
    const errs: { worker?: string; time?: string } = {};
    if (!laborForm.workerEmploymentId.trim()) errs.worker = tc.errors.workerRequired;
    if (!laborForm.startAt || !laborForm.endAt || laborForm.endAt <= laborForm.startAt) errs.time = tc.errors.timeInvalid;
    setLaborErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setBanner(null);
    const res = await reportLaborTime(token, id, { workerEmploymentId: laborForm.workerEmploymentId.trim(), startAt: laborForm.startAt, endAt: laborForm.endAt });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.reportedNote);
    await loadAll();
  }

  async function approveLabor(entryId: string) {
    const id = requireOrder();
    if (!id || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await approveLaborTime(token, id, entryId);
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.laborApprovedNote);
    await loadAll();
  }

  async function submitCounter(e: FormEvent) {
    e.preventDefault();
    const id = requireOrder();
    if (!id || !token) { setCounterErrors({ order: tc.errors.orderRequired }); return; }
    const value = Number(counterForm.counterValue);
    const errs: { source?: string; value?: string } = {};
    if (!counterForm.sourceEventId.trim()) errs.source = tc.errors.sourceRequired;
    if (!counterForm.counterValue.trim() || !Number.isFinite(value) || value < 0) errs.value = tc.errors.valueInvalid;
    setCounterErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setBanner(null);
    const res = await reportCounterEvent(token, id, { sourceEventId: counterForm.sourceEventId.trim(), counterValue: value, workCenterId: counterForm.workCenterId.trim() || null });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", res.data.rollover ? tc.rolloverNote : tc.counterReportedNote);
    setCounterForm({ sourceEventId: "", counterValue: "", workCenterId: counterForm.workCenterId });
    await loadAll();
  }

  async function submitDowntime(e: FormEvent) {
    e.preventDefault();
    const id = requireOrder();
    if (!id || !token) { setDownErrors({ order: tc.errors.orderRequired }); return; }
    const errs: { wc?: string; time?: string; reason?: string } = {};
    if (!downForm.workCenterId.trim()) errs.wc = tc.errors.wcRequired;
    if (!downForm.startAt || !downForm.endAt || downForm.endAt <= downForm.startAt) errs.time = tc.errors.timeInvalid;
    if (!downForm.reasonCode.trim()) errs.reason = tc.errors.reasonRequired;
    setDownErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setBanner(null);
    const res = await reportDowntimeEvent(token, id, { workCenterId: downForm.workCenterId.trim(), startAt: downForm.startAt, endAt: downForm.endAt, reasonCode: downForm.reasonCode.trim(), note: null });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.downtimeReportedNote);
    setDownForm({ workCenterId: downForm.workCenterId, startAt: "", endAt: "", reasonCode: "" });
    await loadAll();
  }

  async function submitTransfer(e: FormEvent) {
    e.preventDefault();
    const id = requireOrder();
    if (!id || !token) { setWipErrors({ order: tc.errors.orderRequired }); return; }
    const from = Number(wipForm.fromSeq);
    const to = Number(wipForm.toSeq);
    const qty = Number(wipForm.qty);
    const errs: { seq?: string; qty?: string } = {};
    if (!wipForm.fromSeq.trim() || !Number.isInteger(from) || from < 1 || !wipForm.toSeq.trim() || !Number.isInteger(to) || to < 1) errs.seq = tc.errors.seqInvalid;
    if (!wipForm.qty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    setWipErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setBanner(null);
    const res = await transferWip(token, id, { fromSeq: from, toSeq: to, qty, deviationReason: wipForm.deviationReason.trim() || null });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.transferNote);
    setWipForm({ fromSeq: "", toSeq: "", qty: "", deviationReason: "" });
    await loadAll();
  }

  async function submitCorrection(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const errs: { target?: string; reason?: string; json?: string } = {};
    if (!corrForm.targetId.trim()) errs.target = tc.errors.targetRequired;
    if (!corrForm.reason.trim()) errs.reason = tc.errors.reasonRequired;
    try { JSON.parse(corrForm.compensatingJson); } catch { errs.json = tc.errors.reasonRequired; }
    setCorrErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setBanner(null);
    const res = await proposeCorrection(token, { targetType: "labor_time", targetId: corrForm.targetId.trim(), reason: corrForm.reason.trim(), compensatingJson: corrForm.compensatingJson });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.correctionProposedNote + " " + res.data.id);
    setCorrForm({ targetId: "", reason: "", compensatingJson: "{}" });
    const list = await fetchCorrections(token);
    setCorrections(list.ok ? list.data : null);
  }

  async function approveCorrectionById(correctionId: string) {
    if (!token) return;
    setBusy(true);
    setBanner(null);
    const res = await approveCorrection(token, correctionId);
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.correctionApprovedNote);
    const list = await fetchCorrections(token);
    setCorrections(list.ok ? list.data : null);
  }

  const laborColumns: Column<LaborRow>[] = [
    { key: "worker", label: tc.colWorker, mono: true, render: (l) => l.workerEmploymentId },
    { key: "from", label: tc.colFrom, mono: true, render: (l) => l.startAt },
    { key: "to", label: tc.colTo, mono: true, render: (l) => l.endAt },
    { key: "min", label: tc.colMinutes, mono: true, render: (l) => fmtNumber(l.minutes, locale) },
    { key: "status", label: tc.colStatus, render: (l) => <Badge tone={l.status === "Approved" ? "success" : l.status === "Rejected" ? "danger" : "warning"}>{mfgStatusLabel(t, l.status)}</Badge> },
    { key: "act", label: tc.approveEntry, render: (l) => (
      l.status === "Reported" && canShop !== false && meReady && l.workerEmploymentId !== meId ? (
        <Button small variant="secondary" loading={busy} onClick={() => void approveLabor(l.id)}>{tc.approveEntry}</Button>
      ) : null
    ) },
  ];

  const counterColumns: Column<CounterRow>[] = [
    { key: "src", label: tc.colSource, mono: true, render: (c) => c.sourceEventId },
    { key: "read", label: tc.colReading, mono: true, render: (c) => fmtNumber(c.counterValue, locale) },
    { key: "delta", label: tc.colDelta, mono: true, render: (c) => fmtNumber(c.deltaQty, locale) + (c.rollover ? " ⚠" : "") },
  ];

  const downColumns: Column<DowntimeRow>[] = [
    { key: "wc", label: tc.downtimeWc, mono: true, render: (d) => d.workCenterId },
    { key: "from", label: tc.colFrom, mono: true, render: (d) => d.startAt },
    { key: "to", label: tc.colTo, mono: true, render: (d) => d.endAt },
    { key: "reason", label: tc.colReason, mono: true, render: (d) => d.reasonCode },
  ];

  const correctionColumns: Column<CorrectionRow>[] = [
    { key: "id", label: tc.targetEntry, mono: true, render: (c) => c.id },
    { key: "target", label: tc.targetEntry, mono: true, render: (c) => c.targetType + " " + c.targetId },
    { key: "reason", label: tc.correctionReason, render: (c) => c.reason },
    { key: "status", label: tc.colStatus, render: (c) => <Badge tone={c.status === "Approved" ? "success" : "warning"}>{mfgStatusLabel(t, c.status)}</Badge> },
    { key: "act", label: tc.approveCorrection, render: (c) => (
      c.status === "Proposed" && canShop !== false && meReady ? (
        <Button small variant="secondary" loading={busy} onClick={() => void approveCorrectionById(c.id)}>{tc.approveCorrection}</Button>
      ) : null
    ) },
  ];

  const tabs: TabItem[] = [
    { id: "reporting", label: tc.laborTitle, content: (
      <div className="stack">
        <section className="card">
          <div className="form-grid">
            <Input id="sf-order" label={tc.orderLabel} value={orderId} onChange={(e) => setOrderId(e.target.value)} />
          </div>
          <p><Button variant="secondary" small loading={busy} onClick={() => void loadAll()} disabled={!meReady}>{tc.report}</Button></p>
        </section>
        <section className="card">
          <h2 className="t-title">{tc.laborTitle}</h2>
          <form onSubmit={(e) => void submitLabor(e)}>
            <div className="form-grid">
              <Input id="sf-worker" label={tc.workerId} value={laborForm.workerEmploymentId} onChange={(e) => setLaborForm({ ...laborForm, workerEmploymentId: e.target.value })} error={laborErrors.worker} />
              <Input id="sf-lstart" label={tc.startAt} type="datetime-local" value={laborForm.startAt} onChange={(e) => setLaborForm({ ...laborForm, startAt: e.target.value })} />
              <Input id="sf-lend" label={tc.endAt} type="datetime-local" value={laborForm.endAt} onChange={(e) => setLaborForm({ ...laborForm, endAt: e.target.value })} error={laborErrors.time} />
            </div>
            <p><Button type="submit" small loading={busy} disabled={!meReady || canShop === false}>{tc.report}</Button></p>
          </form>
          {labor === null ? null : labor.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={laborColumns} rows={labor} caption={tc.laborTitle} />}
        </section>
        <section className="card">
          <h2 className="t-title">{tc.countersTitle}</h2>
          <form onSubmit={(e) => void submitCounter(e)}>
            <div className="form-grid">
              <Input id="sf-csrc" label={tc.sourceEvent} value={counterForm.sourceEventId} onChange={(e) => setCounterForm({ ...counterForm, sourceEventId: e.target.value })} error={counterErrors.source} />
              <Input id="sf-cval" label={tc.counterValue} type="number" min="0" step="any" value={counterForm.counterValue} onChange={(e) => setCounterForm({ ...counterForm, counterValue: e.target.value })} error={counterErrors.value} />
              <Input id="sf-cwc" label={tc.downtimeWc} value={counterForm.workCenterId} onChange={(e) => setCounterForm({ ...counterForm, workCenterId: e.target.value })} />
            </div>
            <p><Button type="submit" small loading={busy} disabled={!meReady || canShop === false}>{tc.report}</Button></p>
          </form>
          {counters === null ? null : counters.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={counterColumns} rows={counters} caption={tc.countersTitle} />}
        </section>
        <section className="card">
          <h2 className="t-title">{tc.downtimeTitle}</h2>
          <form onSubmit={(e) => void submitDowntime(e)}>
            <div className="form-grid">
              <Input id="sf-dwc" label={tc.downtimeWc} value={downForm.workCenterId} onChange={(e) => setDownForm({ ...downForm, workCenterId: e.target.value })} error={downErrors.wc} />
              <Input id="sf-dstart" label={tc.downtimeFrom} type="datetime-local" value={downForm.startAt} onChange={(e) => setDownForm({ ...downForm, startAt: e.target.value })} />
              <Input id="sf-dend" label={tc.downtimeTo} type="datetime-local" value={downForm.endAt} onChange={(e) => setDownForm({ ...downForm, endAt: e.target.value })} error={downErrors.time} />
              <Input id="sf-dreason" label={tc.downtimeReason} value={downForm.reasonCode} onChange={(e) => setDownForm({ ...downForm, reasonCode: e.target.value })} error={downErrors.reason} />
            </div>
            <p><Button type="submit" small loading={busy} disabled={!meReady || canShop === false}>{tc.report}</Button></p>
          </form>
          {downtime === null ? null : downtime.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={downColumns} rows={downtime} caption={tc.downtimeTitle} />}
        </section>
      </div>
    ) },
    { id: "wip", label: tc.wipTitle, content: (
      <div className="stack">
        <section className="card">
          <h2 className="t-title">{tc.wipTitle}</h2>
          <div className="form-grid">
            <Input id="sf-wip-order" label={tc.orderLabel} value={orderId} onChange={(e) => setOrderId(e.target.value)} />
          </div>
          <p><Button variant="secondary" small loading={busy} onClick={() => void loadAll()} disabled={!meReady}>{tc.report}</Button></p>
          {wip === null ? null : (
            <div className="tiles">
              {[
                { label: tc.wipDebits, value: wip.wipDebits },
                { label: tc.wipCredits, value: wip.wipCredits },
                { label: tc.wipRemainder, value: wip.wipRemainder },
              ].map((tile) => (
                <div className="kpi" key={tile.label}>
                  <p className="kpi__label">{tile.label}</p>
                  <p className="kpi__value">{fmtNumber(tile.value, locale)}</p>
                </div>
              ))}
            </div>
          )}
          {wip ? <p className="t-caption">{tc.watermark}: <span className="mono">{wip.watermark ?? "—"}</span> · {wip.computedAt}</p> : null}
        </section>
        <section className="card">
          <h2 className="t-title">{tc.transferTitle}</h2>
          <form onSubmit={(e) => void submitTransfer(e)}>
            <div className="form-grid">
              <Input id="sf-wfrom" label={tc.fromStep} type="number" min="1" step="1" value={wipForm.fromSeq} onChange={(e) => setWipForm({ ...wipForm, fromSeq: e.target.value })} error={wipErrors.seq} />
              <Input id="sf-wto" label={tc.toStep} type="number" min="1" step="1" value={wipForm.toSeq} onChange={(e) => setWipForm({ ...wipForm, toSeq: e.target.value })} />
              <Input id="sf-wqty" label={tc.transferQty} type="number" min="0" step="any" value={wipForm.qty} onChange={(e) => setWipForm({ ...wipForm, qty: e.target.value })} error={wipErrors.qty} />
              <Input id="sf-wdev" label={tc.deviationReason} value={wipForm.deviationReason} onChange={(e) => setWipForm({ ...wipForm, deviationReason: e.target.value })} />
            </div>
            <p><Button type="submit" small loading={busy} disabled={!meReady || canShop === false}>{tc.transferTitle}</Button></p>
          </form>
        </section>
      </div>
    ) },
    { id: "corrections", label: tc.correctionsTitle, content: (
      <div className="stack">
        <section className="card">
          <h2 className="t-title">{tc.correctionsTitle}</h2>
          <p className="t-caption">{tc.correctionReason}</p>
          <form onSubmit={(e) => void submitCorrection(e)}>
            <div className="form-grid">
              <Input id="sf-ctarget" label={tc.targetEntry} value={corrForm.targetId} onChange={(e) => setCorrForm({ ...corrForm, targetId: e.target.value })} error={corrErrors.target} />
              <Input id="sf-creason" label={tc.correctionReason} value={corrForm.reason} onChange={(e) => setCorrForm({ ...corrForm, reason: e.target.value })} error={corrErrors.reason} />
            </div>
            <Input id="sf-cjson" label={tc.compensating} value={corrForm.compensatingJson} onChange={(e) => setCorrForm({ ...corrForm, compensatingJson: e.target.value })} error={corrErrors.json} />
            <p><Button type="submit" small loading={busy} disabled={!meReady || canShop === false}>{tc.proposeCorrection}</Button></p>
          </form>
          {corrections === null ? null : corrections.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={correctionColumns} rows={corrections} caption={tc.correctionsTitle} />}
        </section>
      </div>
    ) },
  ];

  if (!ready || phase === "loading") return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    return (
      <PageShell
        title={tc.title} subtitle={tc.subtitle} loading={false}
        error errorCode={errorCode} onRetry={() => setPhase("success")}
        forbidden={errorCode === "FORBIDDEN" || errorCode === "HTTP_403" ? tc.forbidden : tc.loadFailed}
        toasts={toasts} onDismiss={dismiss} banner={null}
        loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
      />
    );
  }
  return (
    <PageShell
      title={tc.title} subtitle={tc.subtitle} loading={false} error={false} errorCode={null}
      onRetry={() => setPhase("success")} forbidden={tc.forbidden}
      toasts={toasts} onDismiss={dismiss} banner={banner}
      loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
    >
      <Tabs tabs={tabs} activeId={tab} onChange={setTab} label={tc.title} />
    </PageShell>
  );
}
