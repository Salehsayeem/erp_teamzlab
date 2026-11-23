"use client";

import { useCallback, useMemo, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchStock, type StockRow } from "@/lib/services/api";
import { fmtNumber } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { useListResource } from "@/hooks/useListResource";
import { EmptyState, ErrorState, Input, LoadingSkeleton, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";

export default function StockOverviewPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const fetcher = useCallback((tok: string) => fetchStock(tok), []);
  const { data, phase, load } = useListResource<StockRow>(token, fetcher);
  const [search, setSearch] = useState("");

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    const list = data ?? [];
    return q.length === 0 ? list : list.filter((r) => r.itemSku.toLowerCase().includes(q));
  }, [data, search]);

  const columns: Column<StockRow>[] = [
    { key: "warehouse", label: t.inventory.overview.columns.warehouse, mono: true, render: (r) => r.warehouseCode },
    { key: "location", label: t.inventory.overview.columns.location, mono: true, render: (r) => r.locationCode },
    { key: "itemSku", label: t.inventory.overview.columns.itemSku, mono: true, render: (r) => r.itemSku },
    { key: "qty", label: t.inventory.overview.columns.qty, mono: true, render: (r) => fmtNumber(r.qtyOnHand, locale) },
  ];

  if (!ready || (phase === "loading" && data === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={t.inventory.overview.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.inventory.overview.title}</h1>
        <p className="t-caption">{t.inventory.overview.subtitle}</p>
      </div>
      <section className="card">
        <Input
          id="stock-search"
          type="search"
          label={t.inventory.overview.searchLabel}
          placeholder={t.inventory.overview.searchPlaceholder}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        {rows.length === 0 ? (
          <EmptyState title={search.trim() ? t.inventory.overview.noMatch : t.inventory.overview.empty} />
        ) : (
          <Table<StockRow> columns={columns} rows={rows} caption={t.inventory.overview.title} />
        )}
      </section>
    </div>
  );
}
