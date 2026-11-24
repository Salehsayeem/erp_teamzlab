"use client";

import { useCallback, useMemo } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchValuation, type ValuationRow } from "@/lib/services/api";
import { fmtMoney, fmtNumber } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { useListResource } from "@/hooks/useListResource";
import { EmptyState, ErrorState, LoadingSkeleton, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";

// 403 (missing journals:read) is an expected outcome for non-finance roles —
// it renders an explicit permission state instead of the generic retry error.
function isForbidden(code: string | null): boolean {
  return code === "FORBIDDEN" || code === "HTTP_403";
}

export default function ValuationPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const fetcher = useCallback((tok: string) => fetchValuation(tok), []);
  const { data, phase, errorCode, load } = useListResource<ValuationRow>(token, fetcher);

  const rows = data ?? [];
  const total = useMemo(() => rows.reduce((sum, r) => sum + r.value, 0), [rows]);

  const columns: Column<ValuationRow>[] = [
    { key: "itemSku", label: t.inventory.valuation.columns.itemSku, mono: true, render: (r) => r.itemSku },
    { key: "qty", label: t.inventory.valuation.columns.qty, mono: true, render: (r) => fmtNumber(r.qtyOnHand, locale) },
    { key: "unitCost", label: t.inventory.valuation.columns.unitCost, mono: true, render: (r) => fmtMoney(r.unitCost, locale, "BDT") },
    { key: "value", label: t.inventory.valuation.columns.value, mono: true, render: (r) => fmtMoney(r.value, locale, "BDT") },
  ];

  if (!ready || (phase === "loading" && data === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error") {
    if (isForbidden(errorCode)) {
      return (
        <div className="stack">
          <div className="page-header">
            <h1 className="t-display">{t.inventory.valuation.title}</h1>
            <p className="t-caption">{t.inventory.valuation.subtitle}</p>
          </div>
          <div className="state state--error" role="alert">
            <p className="state__title">{t.inventory.valuation.forbidden}</p>
          </div>
        </div>
      );
    }
    return <ErrorState message={t.inventory.valuation.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  }
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.inventory.valuation.title}</h1>
        <p className="t-caption">{t.inventory.valuation.subtitle}</p>
      </div>
      <section className="card">
        {rows.length === 0 ? (
          <EmptyState title={t.inventory.valuation.empty} />
        ) : (
          <>
            <Table<ValuationRow> columns={columns} rows={rows} caption={t.inventory.valuation.title} />
            <p className="t-body-strong">
              {t.inventory.valuation.totalValue}: <span className="mono">{fmtMoney(total, locale, "BDT")}</span>
            </p>
          </>
        )}
      </section>
    </div>
  );
}
