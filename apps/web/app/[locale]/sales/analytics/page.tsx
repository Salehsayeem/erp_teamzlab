"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { getSalesAnalytics, type SalesAnalyticsReport } from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { salesActionError, SalesMeError, useSalesMe } from "../_lib/helpers";
import { fmtMoney, fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  LoadingSkeleton,
  Select,
} from "@/components/uikit";

const DOMAINS = ["crm", "orders", "billing"];
const GROUPINGS = ["", "owner", "customer", "status"];

export default function SalesAnalyticsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.analytics;
  const { token, ready } = useAuth(locale);
  const { meFailed, retryMe } = useSalesMe(token);

  const [domain, setDomain] = useState("crm");
  const [groupBy, setGroupBy] = useState("");
  const [report, setReport] = useState<SalesAnalyticsReport | null>(null);
  const [phase, setPhase] = useState<"idle" | "loading" | "error" | "success">("idle");
  const [errorCode, setErrorCode] = useState<string | null>(null);

  const load = useCallback(async (d: string, g: string) => {
    if (!token) return;
    setPhase("loading");
    setErrorCode(null);
    const res = await getSalesAnalytics(token, d, g || undefined);
    if (res.ok) {
      setReport(res.data);
      setPhase("success");
    } else {
      setReport(null);
      setErrorCode(res.code);
      setPhase("error");
    }
  }, [token]);

  useEffect(() => { void load(domain, groupBy); }, [load, domain, groupBy]);

  if (!ready) return <LoadingSkeleton rows={6} />;

  const forbidden = errorCode === "FORBIDDEN";
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}

      <section className="card">
        <div className="form-grid">
          <Select
            id="an-domain"
            label={tc.domainLabel}
            value={domain}
            onChange={(e) => setDomain(e.target.value)}
            options={DOMAINS.map((d) => ({ value: d, label: tc.domains[d as keyof typeof tc.domains] }))}
          />
          <Select
            id="an-group"
            label={tc.groupBy}
            value={groupBy}
            onChange={(e) => setGroupBy(e.target.value)}
            options={GROUPINGS.map((g) => ({ value: g, label: g === "" ? tc.rowsColumns.group : tc.groupByOptions[g as keyof typeof tc.groupByOptions] }))}
          />
        </div>
      </section>

      {phase === "loading" ? <LoadingSkeleton rows={5} /> : null}
      {phase === "error" ? (
        forbidden ? (
          <div className="state state--error" role="alert"><p className="state__title">{tc.forbidden}</p></div>
        ) : (
          <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load(domain, groupBy)} />
        )
      ) : null}
      {phase === "success" && report ? (
        <section className="card">
          <p className="t-caption">{tc.definitionLabel}: {report.definition}</p>
          {report.rows.length === 0 ? <EmptyState title={tc.noRows} /> : (
            <div className="table-scroll">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">{tc.rowsColumns.group}</th>
                    <th scope="col">{tc.rowsColumns.count}</th>
                    <th scope="col">{tc.rowsColumns.total}</th>
                    <th scope="col">{tc.rowsColumns.docs}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.rows.map((r) => (
                    <tr key={r.groupKey}>
                      <td data-label={tc.rowsColumns.group} className="mono">{r.groupKey}</td>
                      <td data-label={tc.rowsColumns.count} className="mono">{fmtNumber(r.count, locale)}</td>
                      <td data-label={tc.rowsColumns.total} className="mono">{fmtMoney(r.total, locale, "BDT")}</td>
                      <td data-label={tc.rowsColumns.docs}>
                        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                          {r.docIds.map((id) => <Badge key={id} tone="muted">{id}</Badge>)}
                          {r.docIds.length === 0 ? "—" : null}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      ) : null}
    </div>
  );
}
