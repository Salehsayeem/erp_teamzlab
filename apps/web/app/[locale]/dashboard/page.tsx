"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchMe, fetchSuppliers, fetchPurchaseOrders, fetchCount, type Me } from "@/lib/services/api";
import { fmtNumber } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { ErrorState, LoadingSkeleton } from "@/components/uikit";
import { DashboardCharts } from "@/components/dashboard-charts";

export default function DashboardPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [me, setMe] = useState<Me | null>(null);
  const [counts, setCounts] = useState<{ suppliers: number; pos: number; audit: number; outbox: number } | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [meRes, supRes, poRes, auditRes, outboxRes] = await Promise.all([
      fetchMe(token),
      fetchSuppliers(token),
      fetchPurchaseOrders(token),
      fetchCount(token, "/api/v1/audit-events"),
      fetchCount(token, "/api/v1/outbox"),
    ]);
    if (!meRes.ok) {
      setPhase("error");
      return;
    }
    setMe(meRes.data);
    setCounts({
      suppliers: supRes.ok ? supRes.data.length : 0,
      pos: poRes.ok ? poRes.data.length : 0,
      audit: auditRes.ok ? auditRes.data.length : 0,
      outbox: outboxRes.ok ? outboxRes.data.length : 0,
    });
    setPhase("success");
  }, [token]);

  useEffect(() => { void load(); }, [load]);

  if (!ready || phase === "loading") return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={t.common.networkError} retryLabel={t.common.retry} onRetry={() => void load()} />;
  if (!me || !counts) return null;
  return (
    <div className="stack">
      <h1 className="t-display">{t.dashboard.title}</h1>
      <p className="t-caption">
        {t.dashboard.signedInAs}: <span className="t-body-strong">{me.user.name}</span> · {t.dashboard.role}: <span className="t-body-strong">{me.role}</span> · {t.dashboard.entity}: <span className="t-body-strong">{me.legalEntity.name}</span>
      </p>
      <div className="tiles">
        {([
          [t.dashboard.tiles.suppliers, counts.suppliers],
          [t.dashboard.tiles.purchaseOrders, counts.pos],
          [t.dashboard.tiles.auditEvents, counts.audit],
          [t.dashboard.tiles.outbox, counts.outbox],
        ] as const).map(([label, value]) => (
          <div key={label} className="card">
            <p className="tile__label">{label}</p>
            <p className="tile__value">{fmtNumber(value, locale)}</p>
          </div>
        ))}
      </div>
      <DashboardCharts token={token} locale={locale} currency={me.legalEntity.currency} />
      <section>
        <h2 className="t-title">{t.dashboard.workAreas}</h2>
        <div className="stack">
          <Link href={`/${locale}/procurement/suppliers`} className="card">
            {t.nav.suppliers}
          </Link>
          <Link className="card" href={`/${locale}/procurement/purchase-orders`}>
            {t.nav.purchaseOrders}
          </Link>
          <Link className="card" href={`/${locale}/finance/journals`}>
            {t.nav.journals}
          </Link>
        </div>
      </section>
      <section>
        <h2 className="t-title">{t.nav.inventory}</h2>
        <div className="stack">
          <Link className="card" href={`/${locale}/inventory`}>
            {t.nav.stockOverview}
          </Link>
          <Link className="card" href={`/${locale}/inventory/warehouses`}>
            {t.nav.warehouses}
          </Link>
          <Link className="card" href={`/${locale}/inventory/transfers`}>
            {t.nav.transfers}
          </Link>
          <Link className="card" href={`/${locale}/inventory/counts`}>
            {t.nav.counts}
          </Link>
          <Link className="card" href={`/${locale}/inventory/valuation`}>
            {t.nav.valuation}
          </Link>
        </div>
      </section>
    </div>
  );
}
