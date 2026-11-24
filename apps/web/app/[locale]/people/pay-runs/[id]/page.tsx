"use client";

import { Fragment, useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  approvePayRun,
  calculatePayRun,
  fetchPayRuns,
  fetchPayslip,
  fetchRunPreview,
  fetchRunValidations,
  postPayRun,
  reversePayRun,
  type PayRunRow,
  type Payslip,
  type RunPreview,
  type RunValidations,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtDate, fmtMoney, fmtNumber } from "@/lib/format";
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
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { fmtDay, hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../../_lib/helpers";

export default function PayRunDetailPage() {
  const params = useParams<{ locale: string; id: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.runDetail;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady } = usePeopleMe(token);
  const canApprove = hasPermission(permissions, "payroll:approve");
  const canCalculate = hasPermission(permissions, "payroll:calculate");

  const [run, setRun] = useState<PayRunRow | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [notFound, setNotFound] = useState(false);

  const [validations, setValidations] = useState<RunValidations | null>(null);
  const [preview, setPreview] = useState<RunPreview | null>(null);
  const [slip, setSlip] = useState<Payslip | null>(null);
  const [slipError, setSlipError] = useState<string | null>(null);
  const [slipWorker, setSlipWorker] = useState("");

  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [approveOpen, setApproveOpen] = useState(false);
  const [approveReason, setApproveReason] = useState("");
  const [postOpen, setPostOpen] = useState(false);
  const [reverseOpen, setReverseOpen] = useState(false);
  const [reverseReason, setReverseReason] = useState("");
  const [fundsReleased, setFundsReleased] = useState<boolean | null>(null);
  const [fundsError, setFundsError] = useState<string | null>(null);
  const [toasts, setToasts] = useState<ToastItem[]>([]);

  function pushToast(tone: ToastItem["tone"], message: string) {
    setToasts((prev) => [...prev, { id: crypto.randomUUID(), tone, message }]);
  }
  function dismissToast(id: string) {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }

  // No GET /pay-runs/{id} yet — the run comes from the list and is matched here.
  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchPayRuns(token);
    if (!res.ok) {
      setPhase("error");
      return;
    }
    const found = res.data.find((r) => r.id === params.id) ?? null;
    if (!found) {
      setNotFound(true);
    } else {
      setRun(found);
    }
    setPhase("success");
  }, [token, params.id]);
  useEffect(() => { void load(); }, [load]);

  const loadPanels = useCallback(async () => {
    if (!token) return;
    void fetchRunValidations(token, params.id).then((res) => { if (res.ok) setValidations(res.data); });
    void fetchRunPreview(token, params.id).then((res) => {
      if (res.ok) setPreview(res.data);
      else setPreview(null);
    });
  }, [token, params.id]);
  useEffect(() => { void loadPanels(); }, [loadPanels]);

  // after any lifecycle write: refresh the run (via the list) and the panels
  async function act(runAction: () => Promise<{ ok: true; data: unknown } | { ok: false; code: string; message: string }>, note: string | null) {
    setBusy(true);
    setActionError(null);
    const res = await runAction();
    setBusy(false);
    if (!res.ok) {
      setActionError(peopleActionError(t, res.code, res.message));
      return false;
    }
    if (note !== null) pushToast("success", note);
    await load();
    await loadPanels();
    return true;
  }

  async function confirmApprove(e: FormEvent) {
    e.preventDefault();
    if (!run) return;
    const ok = await act(() => approvePayRun(token ?? "", run.id, approveReason.trim() || null), tc.approvedNote);
    if (ok) {
      setApproveOpen(false);
      setApproveReason("");
    }
  }

  async function confirmPost() {
    if (!run) return;
    const ok = await act(() => postPayRun(token ?? "", run.id), run && run.grossTotal === 0 && run.deductionTotal === 0 ? tc.noJournal : tc.postedNote);
    if (ok) setPostOpen(false);
  }

  async function confirmReverse(e: FormEvent) {
    e.preventDefault();
    if (!run) return;
    if (fundsReleased === null) {
      setFundsError(tc.fundsReleasedHint);
      return;
    }
    setFundsError(null);
    const ok = await act(
      () => reversePayRun(token ?? "", run.id, { reason: reverseReason.trim(), fundsReleased: fundsReleased === true }),
      tc.reversedNote,
    );
    if (ok) {
      setReverseOpen(false);
      setReverseReason("");
      setFundsReleased(null);
    }
  }

  // Payslip loads for the picked worker once results are approved/posted/reversed.
  useEffect(() => {
    if (!token || !run || slipWorker === "") { setSlip(null); return; }
    if (run.status !== "Approved" && run.status !== "Posted" && run.status !== "Reversed") { setSlip(null); return; }
    let cancelled = false;
    setSlipError(null);
    void fetchPayslip(token, run.id, slipWorker).then((res) => {
      if (cancelled) return;
      if (res.ok) {
        setSlip(res.data);
        setSlipError(null);
      } else {
        setSlip(null);
        setSlipError(peopleActionError(t, res.code, res.message));
      }
    });
    return () => { cancelled = true; };
  }, [token, run, slipWorker, t]);

  const isCalculator = meId !== null && run?.calculatedBy === meId;
  // Maker-checker (048): the submitter sees the note, never an enabled approve.
  const showApprove = run?.status === "Calculated" && canApprove !== false && !isCalculator;
  const showPost = run?.status === "Approved" && canApprove !== false;
  const showReverse = run?.status === "Posted" && canApprove !== false;
  const showCalculate = (run?.status === "Open" || run?.status === "Calculated") && canCalculate !== false;

  const slipWorkers = useMemo(() => {
    const rows = (preview?.workers ?? []).map((w) => ({ value: w.employmentId, label: w.name + " (" + w.workerNo + ")" }));
    return rows;
  }, [preview]);

  const previewWorkerColumns: Column<RunPreview["workers"][number]>[] = useMemo(() => [
    { key: "worker", label: tc.previewWorkers.worker, mono: true, render: (w) => w.workerNo },
    { key: "name", label: tc.previewWorkers.name, render: (w) => w.name },
    { key: "gross", label: tc.previewWorkers.gross, mono: true, render: (w) => fmtMoney(w.gross, locale, preview?.currency ?? "BDT") },
    { key: "deductions", label: tc.previewWorkers.deductions, mono: true, render: (w) => fmtMoney(w.deductions, locale, preview?.currency ?? "BDT") },
    { key: "net", label: tc.previewWorkers.net, mono: true, render: (w) => fmtMoney(w.net, locale, preview?.currency ?? "BDT") },
    {
      key: "flags", label: tc.previewWorkers.flags,
      render: (w) => (
        <span className="row-actions" style={{ justifyContent: "flex-start" }}>
          {w.excluded ? <Badge tone="danger">{tc.excludedBadge}</Badge> : null}
          {(w.flags ?? []).includes("zero-pay") ? <Badge tone="warning">{tc.zeroPayBadge}</Badge> : null}
        </span>
      ),
    },
  ], [t, tc, locale, preview]);

  const previewLineColumns: Column<RunPreview["lines"][number]>[] = useMemo(() => [
    { key: "worker", label: tc.previewLines.worker, mono: true, render: (l) => l.employmentId },
    { key: "code", label: tc.previewLines.code, mono: true, render: (l) => l.payCode },
    { key: "kind", label: tc.previewLines.kind, render: (l) => <Badge tone={peopleTone(l.kind)}>{peopleStatusLabel(t, l.kind)}</Badge> },
    { key: "hours", label: tc.previewLines.hours, mono: true, render: (l) => fmtNumber(l.hours, locale) },
    { key: "rate", label: tc.previewLines.rate, mono: true, render: (l) => fmtNumber(l.rate, locale) },
    { key: "amount", label: tc.previewLines.amount, mono: true, render: (l) => fmtMoney(l.amount, locale, l.currency) },
    { key: "source", label: tc.previewLines.source, mono: true, render: (l) => l.sourceEntryId ?? "—" },
  ], [t, tc, locale]);

  const validationColumns: Column<RunValidations["blocking"][number]>[] = useMemo(() => [
    { key: "code", label: tc.findingColumns.code, mono: true, render: (f) => f.code },
    { key: "workers", label: tc.findingColumns.workers, mono: true, render: (f) => fmtNumber(f.workerCount, locale) },
    { key: "message", label: tc.findingColumns.message, render: (f) => f.message },
    { key: "owner", label: tc.findingColumns.owner, mono: true, render: (f) => f.ownerRole ?? "—" },
  ], [tc]);

  if (!ready || phase === "loading") return <LoadingSkeleton rows={7} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  if (notFound || !run) {
    return (
      <div className="stack">
        <div className="page-header"><h1 className="t-display">{tc.overviewTitle}</h1></div>
        <div className="state state--error" role="alert"><p className="state__title">{tc.notFound}</p></div>
      </div>
    );
  }

  const currency = run.currency;
  return (
    <div className="stack">
      <p className="t-caption"><Link href={`/${locale}/people/pay-runs`}>{tc.backToList}</Link></p>
      <div className="page-header">
        <h1 className="t-display">{run.id}</h1>
        <Badge tone={peopleTone(run.status)}>{peopleStatusLabel(t, run.status)}</Badge>
        {run.reversalOfRunId ? <span className="t-caption">{tc.reversalOf}: <span className="mono">{run.reversalOfRunId}</span></span> : null}
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.overviewTitle}</h2>
        <div className="form-grid">
          <Meta label={tc.group} value={run.payGroupId} mono />
          <Meta label={tc.period} value={run.periodId} mono />
          <Meta label={tc.payDate} value={fmtDay(run.payDate, locale)} />
          <Meta label={tc.currency} value={run.currency} mono />
          <Meta label={tc.gross} value={fmtMoney(run.grossTotal, locale, currency)} mono />
          <Meta label={tc.deductions} value={fmtMoney(run.deductionTotal, locale, currency)} mono />
          <Meta label={tc.net} value={fmtMoney(run.netTotal, locale, currency)} mono />
          <Meta label={tc.calculatedBy} value={run.calculatedBy ?? "—"} mono />
          <Meta label={tc.approvedBy} value={run.approvedBy ?? "—"} mono />
          <Meta label={tc.postedBy} value={run.postedBy ?? "—"} mono />
          <Meta label={tc.journal} value={run.journalId ?? tc.noJournal} mono />
          {run.reversalReason ? <Meta label={tc.reversalReason} value={run.reversalReason} /> : null}
          {run.fundsReleased !== null ? <Meta label={tc.fundsReleased} value={run.fundsReleased ? tc.flagYes : tc.flagNo} /> : null}
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{tc.lifecycleTitle}</h2>
        <p className="t-caption">{tc.calculateHint}</p>
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          {showCalculate ? (
            <Button disabled={busy || !meReady} loading={busy} onClick={() => void act(() => calculatePayRun(token ?? "", run.id), tc.calculatedNote)}>
              {tc.calculate}
            </Button>
          ) : null}
          {run.status === "Calculated" && isCalculator ? (
            <p className="t-caption" role="status">{t.people.makerChecker}</p>
          ) : null}
          {showApprove ? (
            <Button disabled={busy || !meReady} onClick={() => setApproveOpen(true)}>{tc.approve}</Button>
          ) : null}
          {showPost ? <Button disabled={busy || !meReady} onClick={() => setPostOpen(true)}>{tc.post}</Button> : null}
          {showReverse ? <Button variant="danger" disabled={busy || !meReady} onClick={() => setReverseOpen(true)}>{tc.reverse}</Button> : null}
        </div>
        <p className="t-caption">{tc.approveHint}</p>
        <p className="t-caption">{tc.postHint}</p>
      </section>

      <section className="card">
        <div className="page-header" style={{ marginBlockEnd: 8 }}>
          <h2 className="t-title">{tc.validationsTitle}</h2>
          {validations ? (
            <Badge tone={validations.canFinalize ? "success" : "warning"}>
              {validations.canFinalize ? tc.readyBadge : tc.blockedBadge}
            </Badge>
          ) : null}
        </div>
        <p className="t-caption">{tc.validationsHint}</p>
        {validations && validations.blocking.length === 0 ? <p className="t-caption">{tc.noBlockers}</p> : null}
        {validations && validations.blocking.length > 0 ? (
          <Table columns={validationColumns} rows={validations.blocking} caption={tc.validationsTitle} />
        ) : null}
        <h3 className="t-body-strong">{tc.warningsTitle}</h3>
        {validations && validations.warnings.length === 0 ? <p className="t-caption">{tc.noWarnings}</p> : null}
        {validations && validations.warnings.length > 0 ? (
          <Table columns={validationColumns} rows={validations.warnings} caption={tc.warningsTitle} />
        ) : null}
      </section>

      {preview ? (
        <section className="card">
          <div className="page-header" style={{ marginBlockEnd: 8 }}>
            <h2 className="t-title">{tc.previewTitle}</h2>
            <Badge tone={preview.reconciles ? "success" : "danger"}>
              {preview.reconciles ? tc.reconcilesBadge : tc.notReconciledBadge}
            </Badge>
          </div>
          <p className="t-caption">{tc.previewHint}</p>
          <p className="t-caption">
            {tc.gross}: <span className="mono">{fmtMoney(preview.grossTotal, locale, preview.currency)}</span> · {tc.deductions}: <span className="mono">{fmtMoney(preview.deductionTotal, locale, preview.currency)}</span> · {tc.net}: <span className="mono">{fmtMoney(preview.netTotal, locale, preview.currency)}</span>
          </p>
          {preview.workers.length === 0 ? <EmptyState title={tc.notFound} /> : (
            <Table columns={previewWorkerColumns} rows={preview.workers} caption={tc.previewTitle} />
          )}
          {preview.lines.length > 0 ? (
            <Table columns={previewLineColumns} rows={preview.lines} caption={tc.previewLines.worker} />
          ) : null}
        </section>
      ) : null}

      <section className="card">
        <h2 className="t-title">{tc.payslipTitle}</h2>
        <p className="t-caption">{tc.payslipHint}</p>
        {slipWorkers.length === 0 ? (
          <p className="t-caption">{tc.payslipHint}</p>
        ) : (
          <Select id="slip-worker" label={tc.pickWorker} value={slipWorker} onChange={(e) => setSlipWorker(e.target.value)} options={slipWorkers} placeholder={tc.pickWorker} />
        )}
        {slipError ? <p className="banner" role="alert">{slipError}</p> : null}
        {slip ? (
          <div className="stack" style={{ marginBlockStart: 0 }}>
            <div className="page-header" style={{ marginBlockEnd: 0 }}>
              <h3 className="t-body-strong">{tc.slipFor}: {slip.name} ({slip.workerNo})</h3>
              {slip.reversed ? <Badge tone="danger">{tc.reversedMarker}</Badge> : null}
              {slip.reversalRunId ? <span className="t-caption">{tc.reversalRunLink}: <span className="mono">{slip.reversalRunId}</span></span> : null}
            </div>
            <p className="t-caption">
              {tc.slipPeriod}: {slip.period.name} ({slip.period.startDate} … {slip.period.endDate}) · {tc.slipPayDate}: {fmtDay(slip.payDate, locale)}
            </p>
            <SlipTable slip={slip} locale={locale} tc={tc} t={t} />
          </div>
        ) : null}
      </section>

      <Modal open={approveOpen} onClose={() => setApproveOpen(false)} title={tc.approveConfirmTitle} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void confirmApprove(e)}>
          <p>{tc.approveConfirm}</p>
          <Input id="approve-reason" label={t.people.time.rejectReason} value={approveReason} onChange={(e) => setApproveReason(e.target.value)} />
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setApproveOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.approve}</Button>
          </div>
        </form>
      </Modal>

      <Modal open={postOpen} onClose={() => setPostOpen(false)} title={tc.postConfirmTitle} closeLabel={t.common.cancel} busy={busy}>
        <p>{tc.postConfirm}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setPostOpen(false)}>{t.common.cancel}</Button>
          <Button loading={busy} onClick={() => void confirmPost()}>{tc.post}</Button>
        </div>
      </Modal>

      <Modal open={reverseOpen} onClose={() => setReverseOpen(false)} title={tc.reverseTitle} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void confirmReverse(e)}>
          <p>{tc.reverseConfirm}</p>
          <Input id="reverse-reason" label={tc.reverseReason} value={reverseReason} onChange={(e) => setReverseReason(e.target.value)} />
          <div className="form-grid">
            <Select
              id="reverse-funds" label={tc.reverseFundsReleased} value={fundsReleased === null ? "" : fundsReleased ? "true" : "false"}
              onChange={(e) => setFundsReleased(e.target.value === "" ? null : e.target.value === "true")}
              options={[{ value: "true", label: tc.flagYes }, { value: "false", label: tc.flagNo }]}
              placeholder={tc.reverseFundsReleased} error={fundsError ?? undefined} hint={tc.fundsReleasedHint}
            />
          </div>
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setReverseOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" variant="danger" loading={busy}>{tc.reverse}</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
}

function Meta({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <p className="t-caption">
      <span className="t-body-strong">{label}: </span>
      <span className={mono ? "mono" : undefined}>{value}</span>
    </p>
  );
}

function SlipTable({ slip, locale, tc, t }: {
  slip: Payslip;
  locale: Locale;
  tc: ReturnType<typeof getDictionary>["people"]["runDetail"];
  t: ReturnType<typeof getDictionary>;
}) {
  const columns: Column<Payslip["lines"][number]>[] = [
    { key: "code", label: tc.slipColumns.code, mono: true, render: (l) => l.payCode },
    { key: "kind", label: tc.slipColumns.kind, render: (l) => <Badge tone={peopleTone(l.kind)}>{peopleStatusLabel(t, l.kind)}</Badge> },
    { key: "hours", label: tc.slipColumns.hours, mono: true, render: (l) => fmtNumber(l.hours, locale) },
    { key: "rate", label: tc.slipColumns.rate, mono: true, render: (l) => fmtNumber(l.rate, locale) },
    { key: "amount", label: tc.slipColumns.amount, mono: true, render: (l) => fmtMoney(l.amount, locale, l.currency) },
  ];
  return (
    <div>
      <Table columns={columns} rows={slip.lines} caption={tc.slipFor} />
      <p className="t-caption">
        {tc.slipGross}: <span className="mono">{fmtMoney(slip.gross, locale, slip.currency)}</span> · {tc.slipDeductions}: <span className="mono">{fmtMoney(slip.deductions, locale, slip.currency)}</span> · <span className="t-body-strong">{tc.slipNet}: <span className="mono">{fmtMoney(slip.net, locale, slip.currency)}</span></span>
      </p>
    </div>
  );
}
