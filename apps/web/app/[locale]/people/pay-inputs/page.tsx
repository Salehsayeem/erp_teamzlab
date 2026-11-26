"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createBenefitPlan, createContributionRule, createEnrollment, createGarnishment, createTaxElection,
  fetchBenefitPlans, fetchContributionRules, fetchGarnishments, fetchTaxElections, postProviderFeed,
  reconcileContribution, releaseGarnishment, verifyTaxElection,
  type BenefitPlanRow, type ContributionRuleRow, type GarnishmentRow, type TaxElectionRow,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Table, ToastRegion, type ToastItem } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../_lib/helpers";

export default function PayInputsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.payinputs;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canWrite = hasPermission(permissions, "payinputs:write");
  const canRelease = hasPermission(permissions, "payroll:release");

  const [tab, setTab] = useState<"elections" | "contributions" | "garnishments" | "benefits">("elections");
  const [elections, setElections] = useState<TaxElectionRow[] | null>(null);
  const [rules, setRules] = useState<ContributionRuleRow[] | null>(null);
  const [orders, setOrders] = useState<GarnishmentRow[] | null>(null);
  const [plans, setPlans] = useState<BenefitPlanRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [election, setElection] = useState({ employmentId: "", kind: "TAX-DECL", value: "", effectiveFrom: "" });
  const [rule, setRule] = useState({ code: "", name: "", party: "Employee", ratePct: "5", payCodeId: "", effectiveFrom: "" });
  const [garn, setGarn] = useState({ employmentId: "", kind: "Court", priority: "1", amountPerPeriod: "", courtRef: "", effectiveFrom: "" });
  const [plan, setPlan] = useState({ code: "", name: "", employeePct: "2", payCodeId: "" });
  const [evidence, setEvidence] = useState<{ order: GarnishmentRow | null; value: string }>({ order: null, value: "" });
  const [recon, setRecon] = useState<{ ruleId: string; runId: string }>({ ruleId: "", runId: "" });
  const [reconResult, setReconResult] = useState<{ expected: number; posted: number; reconciles: boolean } | null>(null);
  const [enroll, setEnroll] = useState({ employmentId: "", planId: "", effectiveFrom: "" });
  const [enrolledId, setEnrolledId] = useState("");
  const [feed, setFeed] = useState({ period: "", amount: "", sourceEventId: "" });
  const [feedNote, setFeedNote] = useState<string | null>(null);

  const push = (tone: ToastItem["tone"], message: string) => setToasts((p) => [...p, { id: crypto.randomUUID(), tone, message }]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [e, c, g, b] = await Promise.all([
      fetchTaxElections(token), fetchContributionRules(token), fetchGarnishments(token), fetchBenefitPlans(token),
    ]);
    if (e.ok && c.ok && g.ok && b.ok) {
      setElections(e.data); setRules(c.data); setOrders(g.data); setPlans(b.data);
      setPhase("success");
    } else setPhase("error");
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function run(fn: () => Promise<{ ok: boolean; code?: string; message?: string; data?: unknown }>, label: string) {
    setBusy(true); setFormError(null);
    const res = await fn();
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code ?? "ERROR", res.message ?? "")); return; }
    push("success", label);
    void load();
  }

  async function submitRecon(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    await run(async () => {
      const res = await reconcileContribution(token, recon.ruleId.trim(), recon.runId.trim());
      if (res.ok) setReconResult(res.data);
      return res;
    }, recon.ruleId.trim());
  }

  async function submitEnroll(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    await run(async () => {
      const res = await createEnrollment(token, { employmentId: enroll.employmentId.trim(), planId: enroll.planId.trim(), effectiveFrom: enroll.effectiveFrom });
      if (res.ok) setEnrolledId(res.data.id);
      return res;
    }, enroll.employmentId.trim());
  }

  async function submitFeed(e: FormEvent) {
    e.preventDefault();
    if (!token || !enrolledId) return;
    setFeedNote(null);
    await run(async () => {
      const res = await postProviderFeed(token, enrolledId, { period: feed.period.trim(), amount: Number(feed.amount) || 0, sourceEventId: feed.sourceEventId.trim() });
      if (res.ok) setFeedNote(res.data.duplicate ? tc.duplicate : tc.postFeed);
      return res;
    }, feed.period.trim());
  }

  function onConfirmRelease() {
    if (!token || !evidence.order || !evidence.value.trim()) return;
    void run(() => releaseGarnishment(token, evidence.order!.id, evidence.value.trim()), evidence.order!.id);
    setEvidence({ order: null, value: "" });
  }

  const electionColumns: Column<TaxElectionRow>[] = useMemo(() => [
    { key: "worker", label: tc.columns.worker, mono: true, render: (x) => x.employmentId },
    { key: "kind", label: tc.columns.kind, mono: true, render: (x) => x.kind },
    { key: "status", label: tc.columns.status, render: (x) => <Badge tone={peopleTone(x.status)}>{peopleStatusLabel(t, x.status)}</Badge> },
    { key: "actions", label: "", render: (x) => x.status === "Draft" && canWrite === true ? (
        <Button variant="secondary" onClick={() => void run(() => verifyTaxElection(token!, x.id), x.id)}>{tc.verify}</Button>
      ) : null },
  ], [tc, t, canWrite, token, run]);

  const ruleColumns: Column<ContributionRuleRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.code, mono: true, render: (x) => x.code },
    { key: "name", label: tc.columns.rate, render: (x) => x.name },
    { key: "rate", label: tc.columns.rate, mono: true, render: (x) => x.ratePct + "%" },
    { key: "paycode", label: tc.payCodeId, mono: true, render: (x) => x.payCodeId },
  ], [tc]);

  const garnColumns: Column<GarnishmentRow>[] = useMemo(() => [
    { key: "worker", label: tc.columns.worker, mono: true, render: (x) => x.employmentId },
    { key: "prio", label: tc.columns.priority, mono: true, render: (x) => String(x.priority) },
    { key: "amount", label: tc.columns.amount, mono: true, render: (x) => x.amountPerPeriod },
    { key: "status", label: tc.columns.status, render: (x) => <Badge tone={peopleTone(x.status)}>{peopleStatusLabel(t, x.status)}</Badge> },
    { key: "actions", label: "", render: (x) => x.status === "Active" && canRelease === true ? (
        <Button variant="secondary" onClick={() => setEvidence({ order: x, value: "" })}>{tc.release}</Button>
      ) : null },
  ], [tc, t, canRelease]);

  const planColumns: Column<BenefitPlanRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.code, mono: true, render: (x) => x.code },
    { key: "name", label: tc.columns.plan, render: (x) => x.name },
    { key: "provider", label: tc.columns.provider, mono: true, render: (x) => x.providerCode ?? "—" },
    { key: "pct", label: tc.employeePct, mono: true, render: (x) => x.employeePct + "%" },
  ], [tc]);

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
        {(["elections", "contributions", "garnishments", "benefits"] as const).map((k) => (
          <Button key={k} variant={tab === k ? "primary" : "secondary"} onClick={() => setTab(k)}>
            {k === "elections" ? tc.tabElections : k === "contributions" ? tc.tabContributions : k === "garnishments" ? tc.tabGarnishments : tc.tabBenefits}
          </Button>
        ))}
      </nav>

      {tab === "elections" ? (
        <section className="card">
          <h2 className="t-title">{tc.electionFormTitle}</h2>
          {canWrite === true ? (
            <form onSubmit={(e) => { e.preventDefault(); void run(() => createTaxElection(token!, { employmentId: election.employmentId.trim(), kind: election.kind.trim(), value: election.value.trim() || null, effectiveFrom: election.effectiveFrom }), election.employmentId); }}>
              <div className="form-grid">
                <Input id="pi-e-worker" label={tc.workerId} value={election.employmentId} onChange={(e) => setElection({ ...election, employmentId: e.target.value })} />
                <Input id="pi-e-kind" label={tc.kind} value={election.kind} onChange={(e) => setElection({ ...election, kind: e.target.value })} />
                <Input id="pi-e-value" label={tc.value} value={election.value} onChange={(e) => setElection({ ...election, value: e.target.value })} />
                <Input id="pi-e-from" label={tc.columns.from} type="date" value={election.effectiveFrom} onChange={(e) => setElection({ ...election, effectiveFrom: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createElection}</Button></p>
            </form>
          ) : null}
          {(elections ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={electionColumns} rows={elections ?? []} caption={tc.tabElections} />}
        </section>
      ) : null}

      {tab === "contributions" ? (
        <section className="card">
          <h2 className="t-title">{tc.contributionFormTitle}</h2>
          {canWrite === true ? (
            <form onSubmit={(e) => { e.preventDefault(); void run(() => createContributionRule(token!, { code: rule.code.trim(), name: rule.name.trim(), party: rule.party, ratePct: Number(rule.ratePct) || 0, payCodeId: rule.payCodeId.trim(), effectiveFrom: rule.effectiveFrom }), rule.code); }}>
              <div className="form-grid">
                <Input id="pi-c-code" label={tc.columns.code} value={rule.code} onChange={(e) => setRule({ ...rule, code: e.target.value })} />
                <Input id="pi-c-name" label={tc.columns.rate} value={rule.name} onChange={(e) => setRule({ ...rule, name: e.target.value })} />
                <Input id="pi-c-rate" label={tc.rate} type="number" value={rule.ratePct} onChange={(e) => setRule({ ...rule, ratePct: e.target.value })} />
                <Input id="pi-c-code2" label={tc.payCodeId} value={rule.payCodeId} onChange={(e) => setRule({ ...rule, payCodeId: e.target.value })} />
                <Input id="pi-c-from" label={tc.columns.from} type="date" value={rule.effectiveFrom} onChange={(e) => setRule({ ...rule, effectiveFrom: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createRule}</Button></p>
            </form>
          ) : null}
          {(rules ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={ruleColumns} rows={rules ?? []} caption={tc.tabContributions} />}
          {canWrite === true ? (
            <form onSubmit={(e) => void submitRecon(e)} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end", marginTop: "1rem" }}>
              <Input id="pi-c-recon-rule" label={tc.columns.code} value={recon.ruleId} onChange={(e) => setRecon({ ...recon, ruleId: e.target.value })} />
              <Input id="pi-c-recon-run" label={tc.runId} value={recon.runId} onChange={(e) => setRecon({ ...recon, runId: e.target.value })} />
              <Button type="submit" loading={busy}>{tc.reconcile}</Button>
            </form>
          ) : null}
          {reconResult ? (
            <p className="banner" aria-live="polite">{tc.expected}: <span className="mono">{reconResult.expected}</span> · {tc.posted}: <span className="mono">{reconResult.posted}</span> · {reconResult.reconciles ? tc.reconciles : tc.outOfSync}</p>
          ) : null}
        </section>
      ) : null}

      {tab === "garnishments" ? (
        <section className="card">
          <h2 className="t-title">{tc.garnishmentFormTitle}</h2>
          <p className="t-caption">{tc.releaseHint}</p>
          {canWrite === true ? (
            <form onSubmit={(e) => { e.preventDefault(); void run(() => createGarnishment(token!, { employmentId: garn.employmentId.trim(), kind: garn.kind.trim(), priority: Number(garn.priority) || 1, amountPerPeriod: Number(garn.amountPerPeriod) || 0, courtRef: garn.courtRef.trim(), effectiveFrom: garn.effectiveFrom }), garn.employmentId); }}>
              <div className="form-grid">
                <Input id="pi-g-worker" label={tc.workerId} value={garn.employmentId} onChange={(e) => setGarn({ ...garn, employmentId: e.target.value })} />
                <Input id="pi-g-prio" label={tc.priority} type="number" value={garn.priority} onChange={(e) => setGarn({ ...garn, priority: e.target.value })} />
                <Input id="pi-g-amount" label={tc.amountPerPeriod} type="number" value={garn.amountPerPeriod} onChange={(e) => setGarn({ ...garn, amountPerPeriod: e.target.value })} />
                <Input id="pi-g-ref" label={tc.courtRef} value={garn.courtRef} onChange={(e) => setGarn({ ...garn, courtRef: e.target.value })} />
                <Input id="pi-g-from" label={tc.columns.from} type="date" value={garn.effectiveFrom} onChange={(e) => setGarn({ ...garn, effectiveFrom: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createOrder}</Button></p>
            </form>
          ) : null}
          {(orders ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={garnColumns} rows={orders ?? []} caption={tc.tabGarnishments} />}
        </section>
      ) : null}

      {tab === "benefits" ? (
        <section className="card">
          <h2 className="t-title">{tc.planFormTitle}</h2>
          {canWrite === true ? (
            <form onSubmit={(e) => { e.preventDefault(); void run(() => createBenefitPlan(token!, { code: plan.code.trim(), name: plan.name.trim(), employeePct: Number(plan.employeePct) || 0, payCodeId: plan.payCodeId.trim() }), plan.code); }}>
              <div className="form-grid">
                <Input id="pi-b-code" label={tc.columns.code} value={plan.code} onChange={(e) => setPlan({ ...plan, code: e.target.value })} />
                <Input id="pi-b-name" label={tc.columns.plan} value={plan.name} onChange={(e) => setPlan({ ...plan, name: e.target.value })} />
                <Input id="pi-b-pct" label={tc.employeePct} type="number" value={plan.employeePct} onChange={(e) => setPlan({ ...plan, employeePct: e.target.value })} />
                <Input id="pi-b-paycode" label={tc.payCodeId} value={plan.payCodeId} onChange={(e) => setPlan({ ...plan, payCodeId: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createPlan}</Button></p>
            </form>
          ) : null}
          {(plans ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={planColumns} rows={plans ?? []} caption={tc.tabBenefits} />}
          {canWrite === true ? (
            <>
              <h3 className="t-title" style={{ marginTop: "1rem" }}>{tc.enrollmentFormTitle}</h3>
              <form onSubmit={(e) => void submitEnroll(e)}>
                <div className="form-grid">
                  <Input id="pi-en-worker" label={tc.workerId} value={enroll.employmentId} onChange={(e) => setEnroll({ ...enroll, employmentId: e.target.value })} />
                  <Input id="pi-en-plan" label={tc.planId} value={enroll.planId} onChange={(e) => setEnroll({ ...enroll, planId: e.target.value })} />
                  <Input id="pi-en-from" label={tc.columns.from} type="date" value={enroll.effectiveFrom} onChange={(e) => setEnroll({ ...enroll, effectiveFrom: e.target.value })} />
                </div>
                <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.enroll}</Button></p>
              </form>
              {enrolledId ? (
                <>
                  <p className="t-caption mono">{enrolledId}</p>
                  <h3 className="t-title">{tc.feedTitle}</h3>
                  <form onSubmit={(e) => void submitFeed(e)}>
                    <div className="form-grid">
                      <Input id="pi-feed-period" label={tc.period} value={feed.period} onChange={(e) => setFeed({ ...feed, period: e.target.value })} />
                      <Input id="pi-feed-amount" label={tc.feedAmount} type="number" step="0.01" value={feed.amount} onChange={(e) => setFeed({ ...feed, amount: e.target.value })} />
                      <Input id="pi-feed-src" label={tc.sourceEventId} value={feed.sourceEventId} onChange={(e) => setFeed({ ...feed, sourceEventId: e.target.value })} />
                    </div>
                    <p><Button type="submit" loading={busy}>{tc.postFeed}</Button></p>
                  </form>
                  {feedNote ? <p className="banner" aria-live="polite">{feedNote}</p> : null}
                </>
              ) : null}
            </>
          ) : null}
        </section>
      ) : null}

      <Modal open={evidence.order !== null} onClose={() => setEvidence({ order: null, value: "" })} title={tc.release + " — " + (evidence.order?.id ?? "")} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-caption">{tc.releaseHint}</p>
        <Input id="pi-evidence" label={tc.evidence} value={evidence.value} onChange={(e) => setEvidence({ order: evidence.order, value: e.target.value })} />
        <p><Button onClick={onConfirmRelease} loading={busy}>{tc.release}</Button></p>
      </Modal>
    </div>
  );
}
