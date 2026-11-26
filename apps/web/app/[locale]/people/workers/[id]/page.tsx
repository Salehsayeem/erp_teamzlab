"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  addCompensation,
  addPayGroupMembership,
  fetchCompensations,
  fetchPayGroups,
  fetchPayHistory,
  fetchWorkers,
  transitionWorker,
  type CompensationsResponse,
  type PayGroupRow,
  type PayHistoryRow,
  type Worker,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtDate, fmtMoney, fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  ErrorState,
  Input,
  LoadingSkeleton,
  Select,
  Table,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { fmtDay, peopleActionError, peopleStatusLabel, peopleTone } from "../../_lib/helpers";
import { WorkerDeferredBlocks } from "./_blocks";

// WorkerService's legal map mirrored client-side so only legal moves render;
// the server re-checks and answers WORKER_TRANSITION_ERROR otherwise.
const LEGAL_MOVES: Record<string, string[]> = {
  Draft: ["Prehire"],
  Prehire: ["Active"],
  Active: ["OnLeave", "Suspended", "Terminated"],
  OnLeave: ["Active", "Suspended", "Terminated"],
  Suspended: ["Active", "Terminated"],
  Terminated: [],
};

type CompForm = { amount: string; currency: string; payBasis: string; effectiveFrom: string; reason: string };
const EMPTY_COMP: CompForm = { amount: "", currency: "BDT", payBasis: "Monthly", effectiveFrom: "", reason: "" };

type MembershipForm = { payGroupId: string; effectiveFrom: string };

export default function WorkerDetailPage() {
  const params = useParams<{ locale: string; id: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.workerDetail;
  const { token, ready } = useAuth(locale);

  const [worker, setWorker] = useState<Worker | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [notFound, setNotFound] = useState(false);

  const [comp, setComp] = useState<CompensationsResponse | null>(null);
  const [groups, setGroups] = useState<PayGroupRow[] | null>(null);
  const [history, setHistory] = useState<PayHistoryRow[] | null>(null);
  const [historyPhase, setHistoryPhase] = useState<"loading" | "error" | "success">("loading");

  const [move, setMove] = useState("");
  const [moveDate, setMoveDate] = useState("");
  const [moveReason, setMoveReason] = useState("");
  const [moveError, setMoveError] = useState<string | null>(null);
  const [moving, setMoving] = useState(false);

  const [compForm, setCompForm] = useState<CompForm>(EMPTY_COMP);
  const [compErrors, setCompErrors] = useState<Partial<Record<keyof CompForm, string>>>({});
  const [compAdding, setCompAdding] = useState(false);

  const [memForm, setMemForm] = useState<MembershipForm>({ payGroupId: "", effectiveFrom: "" });
  const [memError, setMemError] = useState<string | null>(null);
  const [memJoining, setMemJoining] = useState(false);

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
    const res = await fetchWorkers(token);
    if (!res.ok) {
      setPhase("error");
      return;
    }
    const found = res.data.find((w) => w.id === params.id);
    if (!found) {
      setNotFound(true);
      setPhase("success");
      return;
    }
    setWorker(found);
    setPhase("success");
  }, [token, params.id]);
  useEffect(() => { void load(); }, [load]);

  const loadSide = useCallback(async () => {
    if (!token) return;
    void fetchCompensations(token, params.id).then((res) => { if (res.ok) setComp(res.data); });
    void fetchPayGroups(token).then((res) => { if (res.ok) setGroups(res.data); });
    setHistoryPhase("loading");
    void fetchPayHistory(token, params.id).then((res) => {
      if (res.ok) {
        setHistory(res.data);
        setHistoryPhase("success");
      } else setHistoryPhase("error");
    });
  }, [token, params.id]);
  useEffect(() => { void loadSide(); }, [loadSide]);

  async function submitMove(e: FormEvent) {
    e.preventDefault();
    setMoveError(null);
    if (!worker || !move || !token) return;
    if (move === "Terminated" && !moveReason.trim()) {
      setMoveError(tc.reasonRequired);
      return;
    }
    if (!moveDate) {
      setMoveError(tc.dateRequired);
      return;
    }
    setMoving(true);
    const res = await transitionWorker(token, worker.id, {
      status: move,
      effectiveDate: moveDate,
      reason: moveReason.trim() || null,
    });
    setMoving(false);
    if (!res.ok) {
      setMoveError(peopleActionError(t, res.code, res.message));
      return;
    }
    setWorker(res.data);
    pushToast("success", tc.transitioned);
    setMove("");
    setMoveReason("");
  }

  async function submitComp(e: FormEvent) {
    e.preventDefault();
    const errs: Partial<Record<keyof CompForm, string>> = {};
    const amount = Number(compForm.amount);
    if (!compForm.amount.trim() || !Number.isFinite(amount) || amount <= 0) errs.amount = tc.amountRequired;
    if (!compForm.effectiveFrom) errs.effectiveFrom = tc.dateRequired;
    if (!compForm.reason.trim()) errs.reason = t.people.time.reasonRequired;
    setCompErrors(errs);
    if (Object.keys(errs).length > 0 || !worker || !token) return;
    setCompAdding(true);
    const res = await addCompensation(token, worker.id, {
      amount,
      currency: compForm.currency.trim() || "BDT",
      payBasis: compForm.payBasis,
      effectiveFrom: compForm.effectiveFrom,
      reason: compForm.reason.trim(),
    });
    setCompAdding(false);
    if (!res.ok) {
      setActionError(peopleActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", `${tc.compAdded} ${res.data.id}`);
    setCompForm(EMPTY_COMP);
    void fetchCompensations(token, worker.id).then((r) => { if (r.ok) setComp(r.data); });
  }

  async function submitMembership(e: FormEvent) {
    e.preventDefault();
    setMemError(null);
    if (!memForm.payGroupId || !token || !worker) return;
    if (!memForm.effectiveFrom) {
      setMemError(tc.dateRequired);
      return;
    }
    setMemJoining(true);
    const res = await addPayGroupMembership(token, worker.id, {
      payGroupId: memForm.payGroupId,
      effectiveFrom: memForm.effectiveFrom,
    });
    setMemJoining(false);
    if (!res.ok) {
      setMemError(peopleActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.joined);
    setMemForm({ payGroupId: "", effectiveFrom: "" });
    void fetchPayGroups(token).then((r) => { if (r.ok) setGroups(r.data); });
  }

  const legalMoves = worker ? LEGAL_MOVES[worker.status] ?? [] : [];
  const moveOptions = legalMoves.map((s) => ({ value: s, label: peopleStatusLabel(t, s) }));

  const historyColumns: Column<PayHistoryRow>[] = useMemo(() => [
    { key: "run", label: tc.histColumns.run, mono: true, render: (r) => r.runId },
    { key: "period", label: tc.histColumns.period, mono: true, render: (r) => r.periodId },
    { key: "payDate", label: tc.histColumns.payDate, render: (r) => <span className="t-caption">{fmtDay(r.payDate, locale)}</span> },
    { key: "kind", label: tc.histColumns.kind, render: (r) => <Badge tone={r.kind === "reversal" ? "danger" : "muted"}>{r.kind === "reversal" ? t.people.statuses.Reversal : t.people.statuses.Original}</Badge> },
    { key: "gross", label: tc.histColumns.gross, mono: true, render: (r) => fmtMoney(r.gross, locale, r.currency) },
    { key: "deductions", label: tc.histColumns.deductions, mono: true, render: (r) => fmtMoney(r.deductions, locale, r.currency) },
    { key: "net", label: tc.histColumns.net, mono: true, render: (r) => fmtMoney(r.net, locale, r.currency) },
  ], [t, tc, locale]);

  const compColumns: Column<CompensationsResponse["events"][number]>[] = useMemo(() => [
    { key: "id", label: tc.evtColumns.id, mono: true, render: (e) => e.id },
    { key: "amount", label: tc.evtColumns.amount, mono: true, render: (e) => fmtMoney(e.amount, locale, e.currency) },
    { key: "basis", label: tc.evtColumns.basis, render: (e) => peopleStatusLabel(t, e.payBasis) },
    { key: "from", label: tc.evtColumns.from, render: (e) => <span className="t-caption">{fmtDay(e.effectiveFrom, locale)}</span> },
    { key: "to", label: tc.evtColumns.to, render: (e) => <span className="t-caption">{e.effectiveTo ? fmtDay(e.effectiveTo, locale) : "—"}</span> },
    { key: "reason", label: tc.evtColumns.reason, render: (e) => e.reason },
  ], [t, tc, locale]);

  if (!ready || phase === "loading") return <LoadingSkeleton rows={7} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  if (notFound || !worker) {
    return (
      <div className="stack">
        <div className="page-header"><h1 className="t-display">{tc.metaTitle}</h1></div>
        <div className="state state--error" role="alert"><p className="state__title">{tc.notFound}</p></div>
      </div>
    );
  }

  return (
    <div className="stack">
      <p className="t-caption"><Link href={`/${locale}/people/workers`}>{tc.backToList}</Link></p>
      <div className="page-header">
        <h1 className="t-display">{worker.fullName}</h1>
        <Badge tone={peopleTone(worker.status)}>{peopleStatusLabel(t, worker.status)}</Badge>
        <span className="t-caption mono">{worker.workerNo}</span>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.metaTitle}</h2>
        <div className="form-grid">
          <MetaRow label={tc.workerNo} value={worker.workerNo} />
          <MetaRow label={tc.category} value={worker.categoryCode} />
          <MetaRow label={tc.hireDate} value={fmtDay(worker.hireDate, locale)} />
          <MetaRow label={tc.weeklyHours} value={fmtNumber(worker.weeklyPlannedHours, locale)} />
          <MetaRow label={tc.manager} value={worker.managerUserId ?? "—"} mono />
          <MetaRow label={tc.location} value={worker.locationId ?? "—"} mono />
          <MetaRow label={tc.email} value={worker.email ?? "—"} />
          <MetaRow label={tc.phone} value={worker.phone ?? "—"} />
          <MetaRow label={tc.nationalId} value={worker.nationalId ?? "—"} mono />
          <MetaRow label={tc.preferredName} value={worker.preferredName ?? "—"} />
          {worker.terminationDate ? <MetaRow label={tc.terminatedOn} value={fmtDay(worker.terminationDate, locale)} /> : null}
          {worker.terminationReason ? <MetaRow label={tc.terminationReason} value={worker.terminationReason} /> : null}
          <MetaRow label={tc.createdBy} value={worker.createdBy + " (" + fmtDate(worker.createdAt, locale) + ")"} mono />
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{tc.lifecycleTitle}</h2>
        {legalMoves.length === 0 ? <p className="t-caption">{tc.noMoves}</p> : (
          <form onSubmit={(e) => void submitMove(e)}>
            <div className="form-grid">
              <Select id="wd-move" label={tc.transitionTo} value={move} onChange={(e) => setMove(e.target.value)} options={moveOptions} placeholder={tc.transitionTo} />
              <Input id="wd-move-date" label={tc.effectiveDate} type="date" value={moveDate} onChange={(e) => setMoveDate(e.target.value)} />
              <Input id="wd-move-reason" label={tc.reason} value={moveReason} onChange={(e) => setMoveReason(e.target.value)} />
            </div>
            <p><Button type="submit" loading={moving} disabled={!move || !moveDate}>{tc.transition}</Button></p>
          </form>
        )}
        {moveError ? <p className="banner" role="alert">{moveError}</p> : null}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.compensationTitle}</h2>
        <p className="t-caption">{tc.compensationHint}</p>
        <p className="t-body-strong">
          {tc.asOfLabel}: {comp?.asOf.amount != null
            ? fmtMoney(comp.asOf.amount, locale, comp.asOf.currency ?? "BDT") + " · " + peopleStatusLabel(t, comp.asOf.payBasis ?? "")
            : tc.asOfNone}
        </p>
        {comp && comp.events.length > 0 ? (
          <Table columns={compColumns} rows={comp.events} caption={tc.eventsTitle} />
        ) : <p className="t-caption">{tc.noEvents}</p>}
        <form onSubmit={(e) => void submitComp(e)}>
          <h3 className="t-body-strong">{tc.addCompTitle}</h3>
          <div className="form-grid">
            <Input id="wd-amount" label={tc.amount} type="number" min="0" step="any" value={compForm.amount} onChange={(e) => setCompForm({ ...compForm, amount: e.target.value })} error={compErrors.amount} />
            <Input id="wd-currency" label={tc.currency} value={compForm.currency} onChange={(e) => setCompForm({ ...compForm, currency: e.target.value })} hint={tc.currencyHint} />
            <Select
              id="wd-basis" label={tc.payBasis} value={compForm.payBasis}
              onChange={(e) => setCompForm({ ...compForm, payBasis: e.target.value })}
              options={["Monthly", "Hourly"].map((b) => ({ value: b, label: peopleStatusLabel(t, b) }))}
            />
            <Input id="wd-efffrom" label={tc.effectiveFrom} type="date" value={compForm.effectiveFrom} onChange={(e) => setCompForm({ ...compForm, effectiveFrom: e.target.value })} error={compErrors.effectiveFrom} />
            <Input id="wd-reason" label={tc.compReason} value={compForm.reason} onChange={(e) => setCompForm({ ...compForm, reason: e.target.value })} error={compErrors.reason} />
          </div>
          <p><Button type="submit" loading={compAdding}>{tc.addComp}</Button></p>
        </form>
      </section>

      <section className="card">
        <h2 className="t-title">{tc.membershipTitle}</h2>
        <p className="t-caption">{tc.membershipHint}</p>
        {groups && groups.length > 0 ? (
          <form onSubmit={(e) => void submitMembership(e)}>
            <div className="form-grid">
              <Select
                id="wd-group" label={tc.payGroup} value={memForm.payGroupId}
                onChange={(e) => setMemForm({ ...memForm, payGroupId: e.target.value })}
                options={groups.filter((g) => g.active).map((g) => ({ value: g.id, label: g.code + " — " + g.name }))}
                placeholder={tc.payGroup}
              />
              <Input id="wd-mem-from" label={tc.memberFrom} type="date" value={memForm.effectiveFrom} onChange={(e) => setMemForm({ ...memForm, effectiveFrom: e.target.value })} />
            </div>
            <p><Button type="submit" loading={memJoining} disabled={!memForm.payGroupId}>{tc.join}</Button></p>
          </form>
        ) : <p className="t-caption">{tc.noGroups}</p>}
        {memError ? <p className="banner" role="alert">{memError}</p> : null}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.historyTitle}</h2>
        <p className="t-caption">{tc.historyHint}</p>
        {historyPhase === "loading" ? <LoadingSkeleton rows={3} /> : null}
        {historyPhase === "success" && history && history.length > 0 ? (
          <Table columns={historyColumns} rows={history} caption={tc.historyTitle} />
        ) : null}
        {historyPhase === "success" && history && history.length === 0 ? <p className="t-caption">{tc.noHistory}</p> : null}
      </section>

      {token ? <WorkerDeferredBlocks workerId={worker.id} token={token} locale={locale} /> : null}
    </div>
  );
}

function MetaRow({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <p className="t-caption">
      <span className="t-body-strong">{label}: </span>
      <span className={mono ? "mono" : undefined}>{value}</span>
    </p>
  );
}
