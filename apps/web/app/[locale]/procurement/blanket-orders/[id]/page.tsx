"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchBlanketOrder, type BlanketOrderDetail } from "@/lib/services/procurement";
import { fmtDate, fmtMoney } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, EmptyState, ErrorState, LoadingSkeleton, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";

// P2-2 click-through: blanket rows were dead-ends. The GET /blanket-orders/{id}
// read existed all along; this is its screen.
export default function BlanketOrderDetailPage() {
  const params = useParams<{ locale: string; id: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const bc = t.procurement.blanket;
  const { token, ready } = useAuth(locale);
  const [detail, setDetail] = useState<BlanketOrderDetail | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchBlanketOrder(token, params.id);
    if (res.ok) { setDetail(res.data); setPhase("success"); }
    else { setDetail(null); setPhase("error"); }
  }, [token, params.id]);
  useEffect(() => { void load(); }, [load]);

  const statusLabel = (s: string) =>
    (t.procurement.statuses as Record<string, string>)[s]
    ?? (t.common.status as Record<string, string>)[s]
    ?? s;

  if (!ready || (phase === "loading" && detail === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error" || !detail) return <ErrorState message={bc.detailLoadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display mono">{detail.id}</h1>
        <p className="t-caption">
          <Link href={`/${locale}/procurement/blanket-orders`}>{bc.back}</Link>
          {" · "}<Badge tone={statusTone(detail.status)}>{statusLabel(detail.status)}</Badge>
          {" · v"}{detail.version} · {detail.supplierId} · {fmtMoney(detail.remaining, locale, detail.currency)} {bc.remaining}
        </p>
      </div>

      <section className="card">
        <h2 className="t-title">{bc.title}</h2>
        <div className="form-grid">
          <p className="t-caption mono">{bc.supplier}: {detail.supplierId}</p>
          <p className="t-caption mono">{bc.currency}: {detail.currency}</p>
          <p className="t-caption mono">{bc.ceiling}: {fmtMoney(detail.ceilingAmount, locale, detail.currency)}</p>
          <p className="t-caption mono">{bc.released}: {fmtMoney(detail.releasedAmount, locale, detail.currency)}</p>
          <p className="t-caption mono">{bc.remaining}: {fmtMoney(detail.remaining, locale, detail.currency)}</p>
          <p className="t-caption mono">{bc.validFrom}: {fmtDate(detail.validFrom, locale)}</p>
          <p className="t-caption mono">{bc.validTo}: {fmtDate(detail.validTo, locale)}</p>
          <p className="t-caption mono">{bc.createdBy}: {detail.createdBy} · {fmtDate(detail.createdAt, locale)} · v{detail.version}</p>
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{bc.releases}</h2>
        {detail.releases.length === 0 ? <EmptyState title={bc.releasesEmpty} /> : (
          <div className="table-scroll">
            <table className="table table--lines">
              <thead>
                <tr>
                  <th scope="col">ID</th>
                  <th scope="col">{bc.releaseAmount}</th>
                  <th scope="col">{bc.poRef}</th>
                  <th scope="col">{bc.releasedBy}</th>
                  <th scope="col">{bc.releasedAt}</th>
                </tr>
              </thead>
              <tbody>
                {detail.releases.map((rel) => (
                  <tr key={rel.id}>
                    <td className="mono">{rel.id}</td>
                    <td className="mono">{fmtMoney(rel.amount, locale, detail.currency)}</td>
                    <td className="mono">{rel.poId ?? "—"}</td>
                    <td className="mono">{rel.releasedBy}</td>
                    <td><span className="t-caption">{fmtDate(rel.releasedAt, locale)}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
