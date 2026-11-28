"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Dict, type Locale } from "@/lib/i18n";
import {
  closeFiscalPeriod,
  completeCloseTask,
  fetchCloseChecklists,
  fetchFiscalPeriods,
  naCloseTask,
  type CloseTaskRow,
  type FiscalPeriodRow,
} from "@/lib/services/finance";
import { useAuth } from "@/hooks/useAuth";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  Select,
  Table,
  Tabs,
  type BadgeTone,
} from "@/components/uikit";

type Phase = "loading" | "error" | "success";

// API error codes -> errors.<CODE> catalog lines; the API's English detail rides along.
function catalogError(t: Dict, code: string, message: string, fallback: string): string {
  if (code === "NETWORK") return t.common.networkError;
  const line = (t.errors as unknown as Record<string, string>)[code];
  return line ? line + ": " + message : fallback + ": " + message;
}

const TASK_TONES: Record<string, BadgeTone> = {
  Pending: "warning",
  Completed: "success",
  Waived: "danger",
  NotApplicable: "muted",
};

// Task table, hoisted to module level (P1 review 2): an inline component was recreated every
// render, remounting the tabs (and losing focus) whenever the gate panel state changed.
function TaskRows({ rows, f, onPick, onNa }: {
  rows: CloseTaskRow[];
  f: Dict["fin"]["close"];
  onPick: (r: CloseTaskRow) => void;
  onNa: (r: CloseTaskRow) => void;
}) {
  if (rows.length === 0) return <EmptyState title={f.empty} />;
  return (
    <Table
      caption={f.title}
      rows={rows}
      columns={[
        { key: "id", label: f.columns.id, mono: true, render: (r) => r.id },
        { key: "task", label: f.columns.task, render: (r) => (
          <span>
            {r.title}{" "}
            {r.mandatory
              ? <Badge tone="danger">{f.mandatory}</Badge>
              : <span className="t-caption">{f.optional}</span>}
          </span>
        ) },
        { key: "period", label: f.columns.period, mono: true, render: (r) => r.periodId },
        { key: "due", label: f.columns.due, mono: true, render: (r) => r.dueDate ?? "—" },
        { key: "state", label: f.columns.state, render: (r) => (
          <Badge tone={TASK_TONES[r.status] ?? "muted"}>{r.status}</Badge>
        ) },
        { key: "evidence", label: f.columns.evidence, render: (r) => {
          if (r.status === "Completed") return <span className="t-caption">{f.evidenceDone.replace("{ref}", r.evidenceRef ?? "")}</span>;
          if (r.status === "Waived") return <span className="t-caption">{f.waivedDone.replace("{reason}", r.waiverReason ?? "")}</span>;
          if (r.status === "NotApplicable") return <span className="t-caption">{f.naDone.replace("{reason}", r.naReason ?? "")}</span>;
          return "—";
        } },
        { key: "actions", label: f.columns.actions, render: (r) =>
          r.status === "Pending" ? (
            <div className="row-actions">
              <Button onClick={() => onPick(r)}>{f.completeAction}</Button>
              <Button variant="secondary" onClick={() => onNa(r)}>{f.naAction}</Button>
            </div>
          ) : null },
      ]}
    />
  );
}

// Finance close (062/063): the checklist dashboard buckets tasks overdue / blocked / open /
// complete (Tabs with roving arrows), completes with evidence or an authorized waiver, marks
// N/A with a reason, and the gate-close panel lists the blocking task ids before confirming.
// All four states render explicitly; errors surface in-modal / in-panel - never hidden.
export default function CloseChecklistsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const f = t.fin.close;
  const { token, ready } = useAuth(locale);
  const [tasks, setTasks] = useState<CloseTaskRow[] | null>(null);
  const [periods, setPeriods] = useState<FiscalPeriodRow[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [tab, setTab] = useState("overdue");

  const [completing, setCompleting] = useState<CloseTaskRow | null>(null);
  const [completeForm, setCompleteForm] = useState({ evidence: "", waiver: false, waiverReason: "" });
  const [completeError, setCompleteError] = useState<string | null>(null);
  const [completeBusy, setCompleteBusy] = useState(false);

  const [naTarget, setNaTarget] = useState<CloseTaskRow | null>(null);
  const [naReason, setNaReason] = useState("");
  const [naError, setNaError] = useState<string | null>(null);
  const [naBusy, setNaBusy] = useState(false);

  const [gatePeriod, setGatePeriod] = useState("");
  const [gateTasks, setGateTasks] = useState<CloseTaskRow[] | null>(null);
  const [gateTasksError, setGateTasksError] = useState<string | null>(null);
  const [gateWaive, setGateWaive] = useState(false);
  const [gateReason, setGateReason] = useState("");
  const [gateError, setGateError] = useState<string | null>(null);
  const [gateBusy, setGateBusy] = useState(false);

  const today = new Date().toISOString().slice(0, 10);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [list, per] = await Promise.all([fetchCloseChecklists(token), fetchFiscalPeriods(token)]);
    if (list.ok && per.ok) {
      setTasks(list.data);
      setPeriods(per.data);
      setPhase("success");
    } else {
      setTasks(null);
      setPeriods(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  // 062 dashboard buckets (disjoint): terminal states are complete; Pending splits into
  // overdue (past due date), blocked (depends on an unresolved dependency), open (the rest)
  const buckets = useMemo(() => {
    const rows = tasks ?? [];
    const byId = new Map(rows.map((row) => [row.id, row]));
    const unresolved = (id: string | null) => {
      const dep = id ? byId.get(id) : undefined;
      return dep ? dep.status === "Pending" : false;
    };
    const isOverdue = (r: CloseTaskRow) => r.dueDate !== null && r.dueDate < today;
    const isBlocked = (r: CloseTaskRow) => r.dependsOnTaskId !== null && unresolved(r.dependsOnTaskId);
    return {
      overdue: rows.filter((r) => r.status === "Pending" && isOverdue(r)),
      blocked: rows.filter((r) => r.status === "Pending" && !isOverdue(r) && isBlocked(r)),
      open: rows.filter((r) => r.status === "Pending" && !isOverdue(r) && !isBlocked(r)),
      complete: rows.filter((r) => r.status !== "Pending"),
    };
  }, [tasks, today]);

  async function refreshGateTasks(periodId: string) {
    if (!token) return;
    setGateTasks(null);
    setGateTasksError(null);
    const res = await fetchCloseChecklists(token, periodId);
    if (res.ok) setGateTasks(res.data);
    else setGateTasksError(f.gateLoadFailed);
  }

  async function confirmComplete() {
    if (!token || !completing) return;
    setCompleteError(null);
    const evidence = completeForm.evidence.trim();
    const waiverReason = completeForm.waiver ? completeForm.waiverReason.trim() : "";
    if (!evidence && !waiverReason) { setCompleteError(f.completeNeeds); return; }
    if (evidence && waiverReason) { setCompleteError(f.completeNeeds); return; }
    setCompleteBusy(true);
    const res = await completeCloseTask(token, completing.periodId, completing.id,
      waiverReason ? { waiverReason } : { evidenceRef: evidence });
    setCompleteBusy(false);
    if (!res.ok) { setCompleteError(catalogError(t, res.code, res.message, f.actionFailed)); return; }
    setCompleting(null);
    await load();
  }

  async function confirmNa() {
    if (!token || !naTarget) return;
    setNaError(null);
    if (!naReason.trim()) { setNaError(f.naNeeds); return; }
    setNaBusy(true);
    const res = await naCloseTask(token, naTarget.periodId, naTarget.id, { reason: naReason.trim() });
    setNaBusy(false);
    if (!res.ok) { setNaError(catalogError(t, res.code, res.message, f.actionFailed)); return; }
    setNaTarget(null);
    setNaReason("");
    await load();
  }

  async function confirmGateClose() {
    if (!token || !gatePeriod) return;
    setGateError(null);
    if (gateWaive && !gateReason.trim()) { setGateError(f.gateNeedsReason); return; }
    setGateBusy(true);
    const res = await closeFiscalPeriod(token, gatePeriod, {
      reason: gateReason.trim() || undefined,
      waiveBlockers: gateWaive || undefined,
    });
    setGateBusy(false);
    if (!res.ok) { setGateError(catalogError(t, res.code, res.message, f.actionFailed)); return; }
    setGatePeriod("");
    setGateTasks(null);
    setGateWaive(false);
    setGateReason("");
    await load();
  }

  const gateBlockers = (gateTasks ?? []).filter((r) => r.status === "Pending" && r.mandatory);

  function openComplete(r: CloseTaskRow) {
    setCompleting(r);
    setCompleteForm({ evidence: "", waiver: false, waiverReason: "" });
    setCompleteError(null);
  }

  function openNa(r: CloseTaskRow) {
    setNaTarget(r);
    setNaReason("");
    setNaError(null);
  }

  if (!ready || phase === "loading") return <LoadingSkeleton rows={7} label={t.common.loading} />;
  if (phase === "error") return <ErrorState message={f.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{f.title}</h1>
        <p className="t-caption">{f.subtitle}</p>
      </div>

      <Tabs
        label={f.title}
        activeId={tab}
        onChange={setTab}
        tabs={[
          { id: "overdue", label: `${f.tabs.overdue} (${buckets.overdue.length})`, content: <TaskRows rows={buckets.overdue} f={f} onPick={openComplete} onNa={openNa} /> },
          { id: "blocked", label: `${f.tabs.blocked} (${buckets.blocked.length})`, content: <TaskRows rows={buckets.blocked} f={f} onPick={openComplete} onNa={openNa} /> },
          { id: "open", label: `${f.tabs.open} (${buckets.open.length})`, content: <TaskRows rows={buckets.open} f={f} onPick={openComplete} onNa={openNa} /> },
          { id: "complete", label: `${f.tabs.complete} (${buckets.complete.length})`, content: <TaskRows rows={buckets.complete} f={f} onPick={openComplete} onNa={openNa} /> },
        ]}
      />

      <section className="card">
        <h2 className="t-title">{f.gateTitle}</h2>
        <div className="form">
          <Select
            id="gate-period"
            label={f.gatePeriodLabel}
            value={gatePeriod}
            placeholder="—"
            options={(periods ?? []).filter((p) => p.status !== "Closed").map((p) => ({
              value: p.id,
              label: `${p.id} · ${p.name} (${p.startDate} → ${p.endDate})`,
            }))}
            onChange={(e) => {
              setGatePeriod(e.target.value);
              setGateTasks(null);
              setGateWaive(false);
              setGateReason("");
              setGateError(null);
              if (e.target.value) void refreshGateTasks(e.target.value);
            }}
          />
          {gateTasks !== null ? (
            gateBlockers.length === 0 ? (
              <p className="t-caption">{f.gateNoBlockers}</p>
            ) : (
              <div className="stack">
                <p className="t-caption">{f.gateBlockersTitle}</p>
                <ul>
                  {gateBlockers.map((r) => (
                    <li key={r.id} className="mono">{r.id} · {r.title}</li>
                  ))}
                </ul>
                <label className="field" htmlFor="gate-waive">
                  <span>
                    <input
                      id="gate-waive"
                      type="checkbox"
                      checked={gateWaive}
                      onChange={(e) => setGateWaive(e.target.checked)}
                    />
                    {" "}{f.gateWaiveLabel}
                  </span>
                </label>
                {gateWaive ? (
                  <p className="field__error" role="alert">{f.gateWaiveWarning}</p>
                ) : null}
                {gateWaive ? (
                  <Input
                    id="gate-reason"
                    label={f.gateReasonLabel}
                    value={gateReason}
                    onChange={(e) => setGateReason(e.target.value)}
                  />
                ) : null}
                {gateError ? <p className="field__error" role="alert">{gateError}</p> : null}
                <div>
                  <Button variant="danger" loading={gateBusy} disabled={gateBusy} onClick={() => void confirmGateClose()}>
                    {f.gateCloseAction}
                  </Button>
                </div>
              </div>
            )
          ) : null}
          {gateTasksError ? <p className="field__error" role="alert">{gateTasksError}</p> : null}
          {gateTasks === null && gatePeriod === "" ? (
            <p className="t-caption">{f.empty}</p>
          ) : null}
        </div>
      </section>

      <Modal
        open={completing !== null}
        onClose={() => setCompleting(null)}
        title={f.completeTitle}
        closeLabel={t.common.cancel}
        busy={completeBusy}
      >
        {completing ? (
          <div className="stack">
            <p>{f.completeBody
              .replace("{id}", completing.id)
              .replace("{title}", completing.title)
              .replace("{period}", completing.periodId)}</p>
            {!completeForm.waiver ? (
              <Input
                id="task-evidence"
                label={f.evidenceLabel}
                hint={f.evidenceHint}
                className="input mono"
                value={completeForm.evidence}
                onChange={(e) => setCompleteForm({ ...completeForm, evidence: e.target.value })}
              />
            ) : null}
            <label className="field" htmlFor="task-waiver">
              <span>
                <input
                  id="task-waiver"
                  type="checkbox"
                  checked={completeForm.waiver}
                  onChange={(e) => setCompleteForm({ ...completeForm, waiver: e.target.checked })}
                />
                {" "}{f.waiverLabel}
              </span>
            </label>
            {completeForm.waiver ? (
              <Input
                id="task-waiver-reason"
                label={f.waiverReasonLabel}
                value={completeForm.waiverReason}
                onChange={(e) => setCompleteForm({ ...completeForm, waiverReason: e.target.value })}
              />
            ) : null}
            {completeError ? <p className="field__error" role="alert">{completeError}</p> : null}
            <div className="row-actions">
              <Button variant="secondary" onClick={() => setCompleting(null)}>{t.common.cancel}</Button>
              <Button loading={completeBusy} disabled={completeBusy} onClick={() => void confirmComplete()}>
                {f.completeAction}
              </Button>
            </div>
          </div>
        ) : null}
      </Modal>

      <Modal
        open={naTarget !== null}
        onClose={() => setNaTarget(null)}
        title={f.naTitle}
        closeLabel={t.common.cancel}
        busy={naBusy}
      >
        {naTarget ? (
          <div className="stack">
            <p>{f.naBody
              .replace("{id}", naTarget.id)
              .replace("{title}", naTarget.title)
              .replace("{period}", naTarget.periodId)}</p>
            <Input
              id="na-reason"
              label={f.naReasonLabel}
              value={naReason}
              onChange={(e) => setNaReason(e.target.value)}
              error={naError ?? undefined}
            />
            {naError ? <p className="field__error" role="alert">{naError}</p> : null}
            <div className="row-actions">
              <Button variant="secondary" onClick={() => setNaTarget(null)}>{t.common.cancel}</Button>
              <Button variant="danger" loading={naBusy} disabled={naBusy} onClick={() => void confirmNa()}>
                {f.naAction}
              </Button>
            </div>
          </div>
        ) : null}
      </Modal>
    </div>
  );
}
