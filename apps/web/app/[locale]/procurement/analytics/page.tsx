"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchSpend, fetchDemand, fetchSourcingAnalytics, type AnalyticsMetric } from "@/lib/services/procurement";
import { fmtMoney } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Button, ErrorState, Input, LoadingSkeleton, EmptyState } from "@/components/uikit";

type Phase = "loading" | "error" | "success";

export default function AnalyticsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [spend, setSpend] = useState<AnalyticsMetric | null>(null);
  const [demand, setDemand] = useState<AnalyticsMetric | null>(null);
  const [sourcing, setSourcing] = useState<AnalyticsMetric | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [groupBy, setGroupBy] = useState("supplier");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [sp, dm, so] = await Promise.all([fetchSpend(token, groupBy, from || null, to || null), fetchDemand(token), fetchSourcingAnalytics(token)]);
    if (sp.ok && dm.ok && so.ok) { setSpend(sp.data); setDemand(dm.data); setSourcing(so.data); setPhase("success"); }
    else { setSpend(null); setPhase("error"); }
  }, [token, groupBy, from, to]);
  useEffect(() => { void load(); }, [load]);

  if (!ready || (phase === "loading" && spend === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={t.procurement.analytics.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const metricTable = (m: AnalyticsMetric) => (
    <div className="table-scroll">
      <table className="table">
        <thead>
          <tr>
            <th scope="col">{t.procurement.analytics.key}</th>
            <th scope="col">{t.procurement.analytics.amount}</th>
            <th scope="col">{t.procurement.analytics.count}</th>
          </tr>
        </thead>
        <tbody>
          {m.rows.map((r) => (
            <tr key={r.key}>
              <td className="mono">{r.key}</td>
              <td className="mono">{r.amount ?? "—"}</td>
              <td className="mono">{r.count ?? "—"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.procurement.analytics.title}</h1>
        <p className="t-caption">{t.procurement.analytics.subtitle}</p>
      </div>
      <section className="card">
        <h2 className="t-title">{t.procurement.analytics.spend}</h2>
        <form onSubmit={(e) => { e.preventDefault(); void load(); }} className="stack">
          <div className="form-grid">
            <div className="field">
              <label htmlFor="an-group">{t.procurement.analytics.groupBy}</label>
              <select id="an-group" className="input" value={groupBy} onChange={(e) => setGroupBy(e.target.value)}>
                {["supplier", "period"].map((g) => <option key={g} value={g}>{g}</option>)}
              </select>
            </div>
            <Input id="an-from" label={t.procurement.analytics.from} type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            <Input id="an-to" label={t.procurement.analytics.to} type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small>{t.procurement.analytics.apply}</Button>
          </div>
        </form>
        <p className="t-caption">{t.procurement.analytics.definition}: {spend?.definition ?? "—"}</p>
        {spend && spend.rows.length > 0 ? metricTable(spend) : <EmptyState title={t.procurement.analytics.empty} />}
        <p className="t-caption mono">{t.procurement.analytics.total}: {spend?.total ?? "—"}</p>
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.analytics.demand}</h2>
        <p className="t-caption">{demand?.definition ?? "—"}</p>
        {demand && demand.rows.length > 0 ? metricTable(demand) : <EmptyState title={t.procurement.analytics.empty} />}
        <p className="t-caption">{t.procurement.analytics.currencyNote}</p>
        <p className="t-caption mono">{t.procurement.analytics.total}: {demand?.total ?? "—"}</p>
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.analytics.sourcing}</h2>
        <p className="t-caption">{sourcing?.definition ?? "—"}</p>
        {sourcing && sourcing.rows.length > 0 ? metricTable(sourcing) : <EmptyState title={t.procurement.analytics.empty} />}
        <p className="t-caption mono">{t.procurement.analytics.total}: {sourcing?.total ?? "—"}</p>
      </section>
    </div>
  );
}
