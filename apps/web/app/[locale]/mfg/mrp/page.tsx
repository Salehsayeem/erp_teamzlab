"use client";

import { useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createMrpRun, fetchMrpRecommendations, fetchMrpActionMessages,
  convertMrpLine, decideMrpLine, publishMrpRun,
  type MrpRunRow, type MrpLineRow, type ActionMessageRow,
} from "@/lib/services/mfg-deferred";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge, Button, EmptyState, Input, LoadingSkeleton, Modal, Table, Tabs,
  type TabItem, type Column,
} from "@/components/uikit";
import { hasPermission, deferredActionError, useMfgMe } from "../_lib/helpers";
import { PageShell, useToasts } from "../_lib/screen";

type Demand = { itemSku: string; qty: string; unit: string; by: string };

export default function MrpPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.mfg.mrp;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = useMfgMe(token);
  const canMrp = hasPermission(permissions, "mfg:mrp");
  const { toasts, push, dismiss } = useToasts();

  const [tab, setTab] = useState("runs");
  const [runs, setRuns] = useState<(MrpRunRow & { lines?: MrpLineRow[] })[]>([]);
  const [run, setRun] = useState<(MrpRunRow & { lines?: MrpLineRow[] }) | null>(null);
  const [lines, setLines] = useState<MrpLineRow[] | null>(null);
  const [messages, setMessages] = useState<ActionMessageRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("success");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [banner, setBanner] = useState<string | null>(null);
  const [runBusy, setRunBusy] = useState(false);
  const [loadBusy, setLoadBusy] = useState(false);
  const [errorText, setErrorText] = useState<string | null>(null);

  const [form, setForm] = useState({ horizonFrom: "", horizonTo: "", minLot: "", multiple: "", safetyPct: "" });
  const [demands, setDemands] = useState<Demand[]>([{ itemSku: "", qty: "", unit: "EA", by: "" }]);
  const [formErrors, setFormErrors] = useState<{ horizon?: string; demand?: string }>({});
  const [convertTarget, setConvertTarget] = useState<MrpLineRow | null>(null);
  const [convertBusy, setConvertBusy] = useState(false);
  const [decideTarget, setDecideTarget] = useState<{ line: MrpLineRow; action: string } | null>(null);
  const [decideReason, setDecideReason] = useState("");
  const [decideBusy, setDecideBusy] = useState(false);
  const [publishTarget, setPublishTarget] = useState<string>("");
  const [publishOverride, setPublishOverride] = useState("");
  const [publishBusy, setPublishBusy] = useState(false);

  async function submitRun(e: FormEvent) {
    e.preventDefault();
    const errs: { horizon?: string; demand?: string } = {};
    if (!form.horizonFrom || !form.horizonTo || form.horizonTo < form.horizonFrom) errs.horizon = tc.errors.horizonInvalid;
    const firstBad = demands.find((d) => !d.itemSku.trim() || !d.qty.trim() || !Number.isFinite(Number(d.qty)) || Number(d.qty) <= 0 || !d.unit.trim());
    if (firstBad) errs.demand = tc.errors.skuRequired;
    setFormErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setRunBusy(true);
    setBanner(null);
    setErrorText(null);
    const res = await createMrpRun(token, {
      demands: demands.map((d) => ({ itemSku: d.itemSku.trim(), qty: Number(d.qty), unit: d.unit.trim(), by: d.by || null })),
      horizonFrom: form.horizonFrom, horizonTo: form.horizonTo,
      safetyStockPct: form.safetyPct ? Number(form.safetyPct) : null,
      minLotQty: form.minLot ? Number(form.minLot) : null,
      multipleQty: form.multiple ? Number(form.multiple) : null,
    });
    setRunBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.runCreated + " " + res.data.id);
    setRuns((prev) => [res.data, ...prev]);
    setRun(res.data);
    setLines(res.data.lines ?? []);
    setMessages(null);
    setTab("recs");
  }

  async function loadRecommendations(runId: string) {
    if (!token) return;
    setLoadBusy(true);
    const [rec, msg] = await Promise.all([fetchMrpRecommendations(token, runId), fetchMrpActionMessages(token, runId)]);
    setLoadBusy(false);
    setLines(rec.ok ? rec.data : []);
    setMessages(msg.ok ? msg.data : []);
  }

  async function confirmConvert() {
    if (!convertTarget || !token) return;
    setConvertBusy(true);
    setBanner(null);
    const res = await convertMrpLine(token, convertTarget.id);
    setConvertBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.convertedNote + " " + (res.data.convertedOrderId ?? res.data.convertedRequisitionRef ?? ""));
    setConvertTarget(null);
    if (run) await loadRecommendations(run.id);
  }

  async function submitDecide(e: FormEvent) {
    e.preventDefault();
    if (!decideTarget || !token) return;
    if (!decideReason.trim()) { setBanner(tc.errors.reasonRequired); return; }
    setDecideBusy(true);
    setBanner(null);
    const res = await decideMrpLine(token, decideTarget.line.id, { action: decideTarget.action, reason: decideReason.trim() });
    setDecideBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.decidedNote);
    setDecideTarget(null);
    setDecideReason("");
    if (run) await loadRecommendations(run.id);
  }

  async function confirmPublish() {
    if (!publishTarget || !token) return;
    setPublishBusy(true);
    setBanner(null);
    const res = await publishMrpRun(token, publishTarget, publishOverride.trim() || null);
    setPublishBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.publishedNote);
    setPublishTarget("");
    setPublishOverride("");
  }

  function modifiersOf(line: MrpLineRow): string {
    try {
      const parsed: unknown = JSON.parse(line.modifiersJson || "[]");
      if (!Array.isArray(parsed)) return "—";
      return parsed.map((m) => {
        const rec = m as Record<string, unknown>;
        return String(rec.kind ?? rec.type ?? "?") + "→" + String(rec.resultingQty ?? rec.qty ?? "?");
      }).join(", ") || "—";
    } catch { return line.modifiersJson || "—"; }
  }

  const lineColumns: Column<MrpLineRow>[] = [
    { key: "sku", label: tc.colSku, mono: true, render: (l) => l.itemSku },
    { key: "lvl", label: tc.colLevel, mono: true, render: (l) => l.explosionLevel },
    { key: "path", label: tc.colPath, mono: true, render: (l) => l.parentPath || "—" },
    { key: "gross", label: tc.colGross, mono: true, render: (l) => fmtNumber(l.grossQty, locale) },
    { key: "net", label: tc.colNet, mono: true, render: (l) => fmtNumber(l.netQty, locale) },
    { key: "by", label: tc.colBy, mono: true, render: (l) => l.requiredBy ?? (l.exceptionCode ? "—" : "—") },
    { key: "exc", label: tc.colException, render: (l) => (l.exceptionCode ? <Badge tone="warning">{l.exceptionCode}</Badge> : "—") },
    { key: "mods", label: tc.modifiersLabel, mono: true, render: (l) => modifiersOf(l) },
    { key: "status", label: tc.colStatus, render: (l) => <Badge tone={l.conversionStatus === "Converted" ? "success" : l.conversionStatus === "Pending" ? "warning" : "muted"}>{l.conversionStatus}</Badge> },
    { key: "actions", label: tc.convert, render: (l) => (
      canMrp !== false && meReady && l.conversionStatus === "Pending" ? (
        <div className="row-actions">
          <Button small onClick={() => setConvertTarget(l)}>{tc.convert}</Button>
          <Button small variant="secondary" onClick={() => { setDecideTarget({ line: l, action: "reject" }); setDecideReason(""); }}>{tc.reject}</Button>
          <Button small variant="secondary" onClick={() => { setDecideTarget({ line: l, action: "defer" }); setDecideReason(""); }}>{tc.defer}</Button>
        </div>
      ) : null
    ) },
  ];

  const msgColumns: Column<ActionMessageRow>[] = [
    { key: "code", label: tc.colException, mono: true, render: (m) => m.code },
    { key: "msg", label: tc.messagesTitle, render: (m) => m.message },
    { key: "state", label: tc.colStatus, render: (m) => <Badge tone={m.state === "Open" ? "warning" : "muted"}>{m.state}</Badge> },
  ];

  const tabs: TabItem[] = [
    { id: "runs", label: tc.title, content: (
      <div className="stack">
        <section className="card">
          <h2 className="t-title">{tc.runTitle}</h2>
          <form onSubmit={(e) => void submitRun(e)}>
            <h3 className="t-body-strong">{tc.demandsTitle}</h3>
            {demands.map((d, i) => (
              <div className="form-grid" key={i}>
                <Input id={"mrp-sku-" + i} label={tc.demandSku} value={d.itemSku} onChange={(e) => setDemands(demands.map((x, j) => (j === i ? { ...x, itemSku: e.target.value } : x)))} />
                <Input id={"mrp-qty-" + i} label={tc.demandQty} type="number" min="0" step="any" value={d.qty} onChange={(e) => setDemands(demands.map((x, j) => (j === i ? { ...x, qty: e.target.value } : x)))} />
                <Input id={"mrp-unit-" + i} label={tc.demandUnit} value={d.unit} onChange={(e) => setDemands(demands.map((x, j) => (j === i ? { ...x, unit: e.target.value } : x)))} />
                <Input id={"mrp-by-" + i} label={tc.colBy} type="date" value={d.by} onChange={(e) => setDemands(demands.map((x, j) => (j === i ? { ...x, by: e.target.value } : x)))} />
              </div>
            ))}
            <div className="row-actions">
              <Button type="button" variant="secondary" small onClick={() => setDemands([...demands, { itemSku: "", qty: "", unit: "EA", by: "" }])}>{tc.addDemand}</Button>
            </div>
            <div className="form-grid">
              <Input id="mrp-from" label={tc.horizonFrom} type="date" value={form.horizonFrom} onChange={(e) => setForm({ ...form, horizonFrom: e.target.value })} />
              <Input id="mrp-to" label={tc.horizonTo} type="date" value={form.horizonTo} onChange={(e) => setForm({ ...form, horizonTo: e.target.value })} error={formErrors.horizon} />
              <Input id="mrp-minlot" label={tc.minLot} type="number" min="0" step="any" value={form.minLot} onChange={(e) => setForm({ ...form, minLot: e.target.value })} />
              <Input id="mrp-mult" label={tc.multiple} type="number" min="0" step="any" value={form.multiple} onChange={(e) => setForm({ ...form, multiple: e.target.value })} />
              <Input id="mrp-safety" label={tc.safetyPct} type="number" min="0" step="any" value={form.safetyPct} onChange={(e) => setForm({ ...form, safetyPct: e.target.value })} />
            </div>
            {formErrors.demand ? <p className="t-caption" role="alert">{formErrors.demand}</p> : null}
            <p><Button type="submit" loading={runBusy} disabled={!meReady || canMrp === false}>{tc.runAction}</Button></p>
          </form>
        </section>
        <section className="card">
          {runs.length === 0 && errorText === null ? <EmptyState title={tc.empty} /> : null}
          {runs.length === 0 ? null : (
            <div className="stack">
              {runs.map((r) => (
                <div className="row-actions" key={r.id}>
                  <button type="button" className="sidebar__link" style={{ inlineSize: "auto", padding: 0 }} onClick={() => { setRun(r); setLines(r.lines ?? null); setMessages(null); setTab("recs"); }}>
                    <span className="mono">{r.id}</span>
                  </button>
                  <Badge tone={r.status === "Completed" ? "success" : r.status === "Failed" ? "danger" : "warning"}>{r.status}</Badge>
                  {r.failureDetail ? <span className="t-caption">{tc.failedLabel} {r.failureDetail}</span> : null}
                  <Button small variant="secondary" onClick={() => { setPublishTarget(r.id); setPublishOverride(""); }}>{tc.publishRun}</Button>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>
    ) },
    { id: "recs", label: tc.recommendationsTitle, content: (
      <section className="card">
        {run === null ? <EmptyState title={tc.empty} /> : (
          <div className="stack">
            <div className="row-actions">
              <p className="t-caption mono">{run.id}</p>
              <Button small variant="secondary" loading={loadBusy} onClick={() => void loadRecommendations(run.id)}>{tc.recommendationsTitle}</Button>
              <Button small onClick={() => { setPublishTarget(run.id); setPublishOverride(""); }}>{tc.publishRun}</Button>
            </div>
            {lines === null ? <EmptyState title={tc.empty} /> : lines.length === 0 ? <EmptyState title={tc.empty} /> : (
              <Table columns={lineColumns} rows={lines} caption={tc.recommendationsTitle} />
            )}
          </div>
        )}
      </section>
    ) },
    { id: "messages", label: tc.messagesTitle, content: (
      <section className="card">
        {run === null ? <EmptyState title={tc.empty} /> : messages === null ? <EmptyState title={tc.noMessages} /> : messages.length === 0 ? <p className="t-caption">{tc.noMessages}</p> : (
          <Table columns={msgColumns} rows={messages} caption={tc.messagesTitle} />
        )}
      </section>
    ) },
  ];

  if (!ready || phase === "loading") return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    return (
      <PageShell
        title={tc.title} subtitle={tc.subtitle} loading={false}
        error errorCode={errorCode} onRetry={() => { setPhase("success"); }}
        forbidden={errorCode === "FORBIDDEN" || errorCode === "HTTP_403" ? tc.forbidden : tc.loadFailed}
        toasts={toasts} onDismiss={dismiss} banner={null}
        loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
      />
    );
  }
  return (
    <PageShell
      title={tc.title} subtitle={tc.subtitle} loading={false} error={false} errorCode={null}
      onRetry={() => { setPhase("success"); }} forbidden={tc.forbidden}
      toasts={toasts} onDismiss={dismiss} banner={banner}
      loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
    >
      <Tabs tabs={tabs} activeId={tab} onChange={setTab} label={tc.title} />
      <Modal open={convertTarget !== null} onClose={() => setConvertTarget(null)} title={tc.convertConfirmTitle} closeLabel={t.common.cancel} busy={convertBusy}>
        <p>{tc.convertConfirm}</p>
        {convertTarget ? <p className="t-caption mono">{convertTarget.itemSku} × {convertTarget.netQty}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setConvertTarget(null)}>{t.common.cancel}</Button>
          <Button loading={convertBusy} onClick={() => void confirmConvert()}>{tc.convert}</Button>
        </div>
      </Modal>
      <Modal open={decideTarget !== null} onClose={() => setDecideTarget(null)} title={decideTarget?.action === "reject" ? tc.reject : tc.defer} closeLabel={t.common.cancel} busy={decideBusy}>
        <form onSubmit={(e) => void submitDecide(e)}>
          {decideTarget ? <p className="t-caption mono">{decideTarget.line.itemSku}</p> : null}
          <Input id="mrp-decide-reason" label={tc.decisionReason} value={decideReason} onChange={(e) => setDecideReason(e.target.value)} />
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setDecideTarget(null)}>{t.common.cancel}</Button>
            <Button type="submit" loading={decideBusy}>{decideTarget?.action === "reject" ? tc.reject : tc.defer}</Button>
          </div>
        </form>
      </Modal>
      <Modal open={publishTarget !== ""} onClose={() => setPublishTarget("")} title={tc.publishConfirmTitle} closeLabel={t.common.cancel} busy={publishBusy}>
        <p>{tc.publishConfirm}</p>
        <Input id="mrp-pub-ovr" label={tc.staleOverride} value={publishOverride} onChange={(e) => setPublishOverride(e.target.value)} />
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setPublishTarget("")}>{t.common.cancel}</Button>
          <Button loading={publishBusy} onClick={() => void confirmPublish()}>{tc.publishRun}</Button>
        </div>
      </Modal>
    </PageShell>
  );
}
