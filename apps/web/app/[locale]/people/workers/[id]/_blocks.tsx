"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { getDictionary, type Locale } from "@/lib/i18n";
import {
  fetchCredentials, fetchGarnishments, fetchLeaveBalance, fetchLeaveTypes, fetchOnboardingTasks,
  generateOnboardingTasks,
  type CredentialRow, type GarnishmentRow, type LeaveBalance, type LeaveTypeRow, type OnboardingTaskWithOverdue,
} from "@/lib/services/people";
import { fmtDay, peopleActionError, peopleStatusLabel, peopleTone } from "../../_lib/helpers";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";

type Loadable<T> = { phase: "loading" | "error" | "success"; data: T | null };

export function WorkerDeferredBlocks({ workerId, token, locale }: { workerId: string; token: string; locale: Locale }) {
  const t = getDictionary(locale);
  const tc = t.people.workerDetail;
  const [tasks, setTasks] = useState<Loadable<OnboardingTaskWithOverdue[]>>({ phase: "loading", data: null });
  const [creds, setCreds] = useState<Loadable<CredentialRow[]>>({ phase: "loading", data: null });
  const [garns, setGarns] = useState<Loadable<GarnishmentRow[]>>({ phase: "loading", data: null });
  const [types, setTypes] = useState<LeaveTypeRow[] | null>(null);
  const [balance, setBalance] = useState<LeaveBalance | null>(null);
  const [balanceError, setBalanceError] = useState<string | null>(null);
  const [balanceTypeId, setBalanceTypeId] = useState("");
  const [generating, setGenerating] = useState(false);
  const [blockError, setBlockError] = useState<string | null>(null);

  const loadTasks = useCallback(async () => {
    const res = await fetchOnboardingTasks(token, workerId);
    setTasks(res.ok ? { phase: "success", data: res.data } : { phase: "error", data: null });
  }, [token, workerId]);
  useEffect(() => { void loadTasks(); }, [loadTasks]);

  const loadCreds = useCallback(async () => {
    const res = await fetchCredentials(token, workerId);
    setCreds(res.ok ? { phase: "success", data: res.data } : { phase: "error", data: null });
  }, [token, workerId]);
  useEffect(() => { void loadCreds(); }, [loadCreds]);

  const loadGarns = useCallback(async () => {
    const res = await fetchGarnishments(token);
    const mine = res.ok ? res.data.filter((g) => g.employmentId === workerId) : null;
    setGarns(res.ok ? { phase: "success", data: mine } : { phase: "error", data: null });
  }, [token, workerId]);
  useEffect(() => { void loadGarns(); }, [loadGarns]);

  useEffect(() => {
    void fetchLeaveTypes(token).then((res) => { if (res.ok) setTypes(res.data); });
  }, [token]);

  async function loadBalance() {
    setBalanceError(null);
    const res = await fetchLeaveBalance(token, workerId, balanceTypeId.trim() || null, null);
    if (res.ok) setBalance(res.data);
    else setBalanceError(peopleActionError(t, res.code, res.message));
  }

  async function generate() {
    setGenerating(true); setBlockError(null);
    const res = await generateOnboardingTasks(token, workerId);
    setGenerating(false);
    if (!res.ok) { setBlockError(peopleActionError(t, res.code, res.message)); return; }
    void loadTasks();
  }

  const taskColumns: Column<OnboardingTaskWithOverdue>[] = useMemo(() => [
    { key: "task", label: tc.obColumns.task, render: (x) => x.name },
    { key: "due", label: tc.obColumns.due, mono: true, render: (x) => fmtDay(x.dueDate, locale) },
    { key: "status", label: tc.obColumns.status, render: (x) => (
        <span className="inline-flex" style={{ gap: "0.25rem" }}>
          <Badge tone={peopleTone(x.status)}>{peopleStatusLabel(t, x.status)}</Badge>
          {x.overdue ? <Badge tone="danger">{tc.overdueBadge}</Badge> : null}
        </span>
      ) },
  ], [tc, t, locale]);

  const credColumns: Column<CredentialRow>[] = useMemo(() => [
    { key: "code", label: tc.credColumns.code, mono: true, render: (c) => c.code + " — " + c.name },
    { key: "expires", label: tc.credColumns.expires, mono: true, render: (c) => (c.expiresOn ? fmtDay(c.expiresOn, locale) : "—") },
    { key: "status", label: tc.credColumns.status, render: (c) => <Badge tone={peopleTone(c.effectiveStatus)}>{peopleStatusLabel(t, c.effectiveStatus)}</Badge> },
  ], [tc, t, locale]);

  const garnColumns: Column<GarnishmentRow>[] = useMemo(() => [
    { key: "kind", label: tc.garnColumns.kind, mono: true, render: (g) => g.kind },
    { key: "prio", label: tc.garnColumns.priority, mono: true, render: (g) => String(g.priority) },
    { key: "amount", label: tc.garnColumns.amount, mono: true, render: (g) => String(g.amountPerPeriod) },
    { key: "status", label: tc.garnColumns.status, render: (g) => <Badge tone={peopleTone(g.status)}>{peopleStatusLabel(t, g.status)}</Badge> },
  ], [tc, t]);

  function Block({ title, phase, retry, empty, children }: {
    title: string; phase: "loading" | "error" | "success"; retry: () => void; empty: string; children: React.ReactNode;
  }) {
    return (
      <section className="card">
        <h2 className="t-title">{title}</h2>
        {phase === "loading" ? <LoadingSkeleton rows={2} /> : null}
        {phase === "error" ? <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={retry} /> : null}
        {phase === "success" ? (children ?? <EmptyState title={empty} />) : null}
        {blockError ? <p className="banner" role="alert">{blockError}</p> : null}
      </section>
    );
  }

  return (
    <>
      <Block title={tc.onboardingTitle} phase={tasks.phase} retry={() => void loadTasks()} empty={tc.obEmpty}>
        {tasks.data !== null && tasks.data.length > 0 ? (
          <>
            <p><Button variant="secondary" onClick={() => void generate()} loading={generating}>{tc.obGenerate}</Button></p>
            <Table columns={taskColumns} rows={tasks.data} caption={tc.onboardingTitle} />
          </>
        ) : (
          <p><Button variant="secondary" onClick={() => void generate()} loading={generating}>{tc.obGenerate}</Button></p>
        )}
      </Block>

      <Block title={tc.credentialsTitle} phase={creds.phase} retry={() => void loadCreds()} empty={tc.credEmpty}>
        {creds.data !== null && creds.data.length > 0 ? <Table columns={credColumns} rows={creds.data} caption={tc.credentialsTitle} /> : null}
      </Block>

      <Block title={tc.leaveBalanceTitle} phase="success" retry={() => undefined} empty={tc.balanceEmpty}>
        <div className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
          <div>
            <label htmlFor="wd-lb-type" className="t-caption">{tc.leaveType}</label>
            <select id="wd-lb-type" className="input" value={balanceTypeId} onChange={(e) => setBalanceTypeId(e.target.value)}>
              <option value="">—</option>
              {(types ?? []).map((ty) => <option key={ty.id} value={ty.id}>{ty.code}</option>)}
            </select>
          </div>
          <Button variant="secondary" onClick={() => void loadBalance()}>{tc.loadBalance}</Button>
        </div>
        {balanceError ? <p className="banner" role="alert">{balanceError}</p> : null}
        {balance ? (
          <p className="banner" aria-live="polite">{tc.leaveBalance}: <span className="mono">{balance.balance}</span></p>
        ) : null}
      </Block>

      <Block title={tc.garnishmentsTitle} phase={garns.phase} retry={() => void loadGarns()} empty={tc.garnEmpty}>
        {garns.data !== null && garns.data.length > 0 ? <Table columns={garnColumns} rows={garns.data} caption={tc.garnishmentsTitle} /> : null}
      </Block>
    </>
  );
}
