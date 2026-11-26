"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createSnapshot,
  getForecast,
  listSnapshots,
  submitForecast,
  type ForecastReport,
  type ForecastSnapshotRow,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { hasPermission, salesActionError, SalesMeError, useSalesMe } from "../_lib/helpers";
import { localStatus, localTone } from "../_lib/deferred";
import { fmtDate, fmtMoney, fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Select,
  ToastRegion,
  type ToastItem,
  type BadgeTone,
} from "@/components/uikit";

const TONE_MAP: Record<string, BadgeTone> = {
  Complete: "success",
  Incomplete: "warning",
};

const PERIOD_RE = /^\d{4}-\d{2}$/;

export default function ForecastsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.forecast;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady, meFailed, retryMe } = useSalesMe(token);

  const [periodInput, setPeriodInput] = useState("");
  const [report, setReport] = useState<ForecastReport | null>(null);
  const [snapshots, setSnapshots] = useState<ForecastSnapshotRow[] | null>(null);
  const [phase, setPhase] = useState<"idle" | "loading" | "error" | "success">("idle");
  const [errorCode, setErrorCode] = useState<string | null>(null);

  const [owner, setOwner] = useState("");
  const [category, setCategory] = useState("Pipeline");
  const [overrideValue, setOverrideValue] = useState("");
  const [subErrors, setSubErrors] = useState<{ owner?: string; period?: string }>({});
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const [freezeBusy, setFreezeBusy] = useState(false);
  const [freezeError, setFreezeError] = useState<string | null>(null);
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  function pushToast(tone: ToastItem["tone"], message: string) {
    setToasts((prev) => [...prev, { id: crypto.randomUUID(), tone, message }]);
  }
  function dismissToast(id: string) {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }

  const load = useCallback(async (period: string) => {
    if (!token || !PERIOD_RE.test(period)) return;
    setPhase("loading");
    setErrorCode(null);
    const res = await getForecast(token, period);
    if (!res.ok) {
      setReport(null);
      setSnapshots(null);
      setErrorCode(res.code);
      setPhase("error");
      return;
    }
    setReport(res.data);
    const snapRes = await listSnapshots(token, period);
    setSnapshots(snapRes.ok ? snapRes.data : null);
    setPhase("success");
  }, [token]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const period = report?.period ?? periodInput;
    const errs: { owner?: string; period?: string } = {};
    if (!owner.trim()) errs.owner = tc.ownerRequired;
    if (!PERIOD_RE.test(period)) errs.period = tc.periodInvalid;
    setSubErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setSubmitting(true);
    setSubmitError(null);
    const num = Number(overrideValue);
    const res = await submitForecast(token, {
      owner: owner.trim(),
      period,
      category,
      ...(overrideValue.trim() && Number.isFinite(num) ? { overrideValue: num } : {}),
    });
    setSubmitting(false);
    if (!res.ok) {
      setSubmitError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.submitted + " " + res.data.version);
    await load(period);
  }

  async function freeze() {
    if (!token) return;
    const period = report?.period ?? periodInput;
    if (!PERIOD_RE.test(period)) return;
    setFreezeBusy(true);
    setFreezeError(null);
    const res = await createSnapshot(token, period);
    setFreezeBusy(false);
    if (!res.ok) {
      setFreezeError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.snapshotCreated + " " + res.data.id);
    const snapRes = await listSnapshots(token, period);
    setSnapshots(snapRes.ok ? snapRes.data : null);
  }

  const writeGate = hasPermission(permissions, "forecasts:write");

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}

      <section className="card">
        <form onSubmit={(e) => { e.preventDefault(); if (PERIOD_RE.test(periodInput)) void load(periodInput); }}>
          <div className="form-grid">
            <Input id="fc-period" label={tc.period} hint={tc.periodHint} value={periodInput} onChange={(e) => setPeriodInput(e.target.value)} placeholder="2026-11" />
            <div className="field">
              <Button type="submit" disabled={!PERIOD_RE.test(periodInput)}>{tc.load}</Button>
            </div>
          </div>
        </form>
      </section>

      {phase === "loading" ? <LoadingSkeleton rows={6} /> : null}
      {phase === "error" ? <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => periodInput && void load(periodInput)} /> : null}

      {phase === "success" && report ? (
        <>
          <div className="tiles">
            <div className="kpi">
              <p className="kpi__label">{tc.bucketTiles.unweighted}</p>
              <p className="kpi__value">{fmtMoney(report.unweighted.total, locale, "BDT")}</p>
              <p className="kpi__delta">{tc.countLabel}: {fmtNumber(report.unweighted.count, locale)}</p>
            </div>
            <div className="kpi">
              <p className="kpi__label">{tc.bucketTiles.weighted}</p>
              <p className="kpi__value">{fmtMoney(report.weighted.total, locale, "BDT")}</p>
            </div>
            <div className="kpi">
              <p className="kpi__label">{tc.bucketTiles.override}</p>
              <p className="kpi__value">{fmtMoney(report.override.total, locale, "BDT")}</p>
              <p className="kpi__delta">{tc.countLabel}: {fmtNumber(report.override.count, locale)}</p>
            </div>
            <div className="kpi">
              <p className="kpi__label">{tc.bucketTiles.unclassified}</p>
              <p className="kpi__value">{fmtNumber(report.unclassified.count, locale)}</p>
            </div>
          </div>

          <section className="card">
            <h2 className="t-title">{tc.submissionsTitle}</h2>
            {report.submissions.length === 0 ? (
              <p className="t-caption">{tc.noSubmissions}</p>
            ) : (
              <div className="table-scroll">
                <table className="table">
                  <thead>
                    <tr>
                      <th scope="col">{tc.submissionColumns.owner}</th>
                      <th scope="col">{tc.submissionColumns.category}</th>
                      <th scope="col">{tc.submissionColumns.version}</th>
                      <th scope="col">{tc.submissionColumns.override}</th>
                      <th scope="col">{tc.submissionColumns.submittedAt}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.submissions.map((s) => (
                      <tr key={s.id}>
                        <td data-label={tc.submissionColumns.owner} className="mono">{s.owner}</td>
                        <td data-label={tc.submissionColumns.category}>{s.category}</td>
                        <td data-label={tc.submissionColumns.version} className="mono">{s.version}</td>
                        <td data-label={tc.submissionColumns.override} className="mono">{s.overrideValue === null ? tc.noOverride : fmtMoney(s.overrideValue, locale, "BDT")}</td>
                        <td data-label={tc.submissionColumns.submittedAt}><span className="t-caption">{fmtDate(s.submittedAt, locale)}</span></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="card">
            <div className="row-actions" style={{ justifyContent: "space-between" }}>
              <h2 className="t-title">{tc.snapshotsTitle}</h2>
              <Button small disabled={!meReady || writeGate === false || freezeBusy} loading={freezeBusy} onClick={() => void freeze()}>{tc.createSnapshot}</Button>
            </div>
            {freezeError ? <p className="banner" role="alert">{freezeError}</p> : null}
            {(snapshots ?? []).length === 0 ? (
              <p className="t-caption">{tc.noSnapshots}</p>
            ) : (
              <div className="table-scroll">
                <table className="table table--lines">
                  <thead>
                    <tr>
                      <th scope="col">{tc.snapshotColumns.id}</th>
                      <th scope="col">{tc.snapshotColumns.status}</th>
                      <th scope="col">{tc.snapshotColumns.unweighted}</th>
                      <th scope="col">{tc.liveTotalLabel}</th>
                      <th scope="col">{tc.snapshotColumns.generatedAt}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(snapshots ?? []).map((snap) => {
                      const snapTotal = snap.payload?.unweightedTotal ?? null;
                      return (
                        <tr key={snap.id}>
                          <td data-label={tc.snapshotColumns.id} className="mono">{snap.id}</td>
                          <td data-label={tc.snapshotColumns.status}>
                            <Badge tone={localTone(TONE_MAP, snap.status)}>{localStatus(t, tc.statusLabels, snap.status)}</Badge>
                            {snap.status === "Incomplete" ? <p className="t-caption" role="status">{tc.snapshotIncompleteNote}</p> : null}
                          </td>
                          <td data-label={tc.snapshotColumns.unweighted} className="mono">{snapTotal === null ? "—" : fmtMoney(snapTotal, locale, "BDT")}</td>
                          <td data-label={tc.liveTotalLabel} className="mono">{fmtMoney(report.unweighted.total, locale, "BDT")}</td>
                          <td data-label={tc.snapshotColumns.generatedAt}><span className="t-caption">{fmtDate(snap.generatedAt, locale)}</span></td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="card">
            <h2 className="t-title">{tc.submitTitle}</h2>
            <form onSubmit={(e) => void submit(e)}>
              <div className="form-grid">
                <Input id="fc-owner" label={tc.owner} value={owner} onChange={(e) => setOwner(e.target.value)} error={subErrors.owner} />
                <Select
                  id="fc-category"
                  label={tc.category}
                  value={category}
                  onChange={(e) => setCategory(e.target.value)}
                  options={[
                    { value: "Pipeline", label: tc.categoryOptions.Pipeline },
                    { value: "BestCase", label: tc.categoryOptions.BestCase },
                    { value: "Commit", label: tc.categoryOptions.Commit },
                  ]}
                />
                <Input id="fc-override" label={tc.overrideValue} type="number" min="0" step="any" value={overrideValue} onChange={(e) => setOverrideValue(e.target.value)} />
              </div>
              {subErrors.period ? <p className="field__error">{subErrors.period}</p> : null}
              {submitError ? <p className="banner" role="alert">{submitError}</p> : null}
              <p><Button type="submit" loading={submitting} disabled={writeGate === false}>{tc.submit}</Button></p>
            </form>
          </section>
        </>
      ) : null}

      {phase === "idle" ? <EmptyState title={tc.periodHint} /> : null}
    </div>
  );
}
