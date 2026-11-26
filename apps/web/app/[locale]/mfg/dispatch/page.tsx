"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchDispatchList, fetchCapacityLoad, type DispatchRow, type CapacityLoadRow } from "@/lib/services/mfg-deferred";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge, Button, EmptyState, Input, LoadingSkeleton, Select, Table, Tabs,
  type TabItem, type Column,
} from "@/components/uikit";
import { hasPermission, deferredActionError, useMfgMe } from "../_lib/helpers";
import { PageShell, useToasts } from "../_lib/screen";

export default function DispatchPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.mfg.dispatch;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = useMfgMe(token);
  const { toasts, push, dismiss } = useToasts();

  const [tab, setTab] = useState("list");
  const [dispatch, setDispatch] = useState<DispatchRow | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [wcFilter, setWcFilter] = useState("");
  const [load, setLoad] = useState<CapacityLoadRow | null>(null);
  const [loadBusy, setLoadBusy] = useState(false);
  const [bucket, setBucket] = useState("day");
  const [banner, setBanner] = useState<string | null>(null);

  const loadDispatch = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    setErrorCode(null);
    const res = await fetchDispatchList(token, wcFilter || undefined);
    if (res.ok) { setDispatch(res.data); setPhase("success"); }
    else { setDispatch(null); setErrorCode(res.code); setPhase("error"); }
  }, [token, wcFilter]);
  useEffect(() => { void loadDispatch(); }, [loadDispatch]);

  async function loadCapacity() {
    if (!token) return;
    setLoadBusy(true);
    const res = await fetchCapacityLoad(token, bucket);
    setLoadBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    setLoad(res.data);
  }

  const rowColumns: Column<DispatchRow["rows"][number]>[] = [
    { key: "seq", label: tc.colOp, mono: true, render: (r) => r.seq },
    { key: "op", label: tc.colOp, mono: true, render: (r) => r.operationCode },
    { key: "wc", label: tc.colWc, mono: true, render: (r) => r.workCenterId },
    { key: "order", label: tc.colOrder, mono: true, render: (r) => r.orderNo ?? r.orderId },
    { key: "state", label: tc.colStatus, render: (r) => (r.dispatchable
      ? <Badge tone="success">{tc.dispatchable}</Badge>
      : <Badge tone="danger">{tc.blockedBy}{r.blockedReason ?? ""}</Badge>) },
  ];

  const loadColumns: Column<CapacityLoadRow["rows"][number]>[] = [
    { key: "wc", label: tc.colWcCode, mono: true, render: (r) => r.workCenterCode ?? r.workCenterId },
    { key: "bucket", label: tc.bucketLabel, mono: true, render: (r) => r.bucketStart },
    { key: "load", label: tc.colLoad, mono: true, render: (r) => fmtNumber(r.loadMinutes, locale) },
    { key: "cap", label: tc.colCapacity, mono: true, render: (r) => fmtNumber(r.capacityMinutes, locale) },
    { key: "util", label: tc.colUtil, render: (r) => (
      <span className={r.utilizationPct > 100 ? "t-caption" : undefined} style={{ color: r.utilizationPct > 100 ? "var(--danger)" : undefined }}>
        {fmtNumber(r.utilizationPct, locale)}%
      </span>
    ) },
  ];

  const unscheduledColumns: Column<{ id: string; operationCode: string; workCenterId: string; reason: string | null }>[] = [
    { key: "op", label: tc.colOp, mono: true, render: (r) => r.operationCode },
    { key: "wc", label: tc.colWc, mono: true, render: (r) => r.workCenterId },
    { key: "reason", label: tc.colStatus, render: (r) => r.reason ?? "—" },
  ];

  const tabs: TabItem[] = [
    { id: "list", label: tc.title, content: (
      <div className="stack">
        <section className="card">
          <div className="form-grid">
            <Input id="dp-wc" label={tc.colWc} value={wcFilter} onChange={(e) => setWcFilter(e.target.value)} />
          </div>
          <p><Button variant="secondary" small onClick={() => void loadDispatch()} disabled={!meReady}>{tc.refreshedLabel}</Button></p>
          {dispatch === null ? null : (
            <p className="t-caption">
              {tc.scheduleLabel}: <span className="mono">{dispatch.scheduleRunId ?? "—"}</span> · {tc.publishedLabel}: <span className="mono">{dispatch.publishedAt ?? "—"}</span> · {tc.refreshedLabel}: <span className="mono">{dispatch.refreshedAt}</span>
            </p>
          )}
          {dispatch && dispatch.rows.length === 0 ? <EmptyState title={tc.empty} /> : null}
          {dispatch && dispatch.rows.length > 0 ? <Table columns={rowColumns} rows={dispatch.rows} caption={tc.title} /> : null}
          {dispatch && dispatch.unscheduled.length > 0 ? (
            <div className="stack">
              <h3 className="t-body-strong">{tc.unscheduledTitle}</h3>
              <Table columns={unscheduledColumns} rows={dispatch.unscheduled} caption={tc.unscheduledTitle} />
            </div>
          ) : null}
        </section>
      </div>
    ) },
    { id: "capacity", label: tc.loadTitle, content: (
      <div className="stack">
        <section className="card">
          <div className="form-grid">
            <Select id="dp-bucket" label={tc.bucketLabel} value={bucket} onChange={(e) => setBucket(e.target.value)} options={[{ value: "day", label: tc.bucketDay }, { value: "week", label: tc.bucketWeek }]} />
          </div>
          <p><Button onClick={() => void loadCapacity()} loading={loadBusy} disabled={!meReady}>{tc.loadTitle}</Button></p>
          {load === null ? null : (
            <div className="stack">
              <p className="t-caption">{tc.durationBasisLabel}: <span className="mono">{load.durationBasis}</span></p>
              {load.rows.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={loadColumns} rows={load.rows} caption={tc.loadTitle} />}
              {load.unscheduled.length > 0 ? (
                <div className="stack">
                  <h3 className="t-body-strong">{tc.unscheduledTitle}</h3>
                  <Table columns={unscheduledColumns} rows={load.unscheduled} caption={tc.unscheduledTitle} />
                </div>
              ) : null}
            </div>
          )}
        </section>
      </div>
    ) },
  ];

  if (!ready || (phase === "loading" && dispatch === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    return (
      <PageShell
        title={tc.title} subtitle={tc.subtitle} loading={false}
        error errorCode={errorCode} onRetry={() => void loadDispatch()}
        forbidden={errorCode === "FORBIDDEN" || errorCode === "HTTP_403" ? tc.forbidden : tc.loadFailed}
        toasts={toasts} onDismiss={dismiss} banner={null}
        loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
      />
    );
  }
  return (
    <PageShell
      title={tc.title} subtitle={tc.subtitle} loading={false} error={false} errorCode={null}
      onRetry={() => void loadDispatch()} forbidden={tc.forbidden}
      toasts={toasts} onDismiss={dismiss} banner={banner}
      loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
    >
      <Tabs tabs={tabs} activeId={tab} onChange={setTab} label={tc.title} />
    </PageShell>
  );
}
