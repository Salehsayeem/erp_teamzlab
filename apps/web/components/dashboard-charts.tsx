"use client";

import { useCallback, useEffect, useState } from "react";
import {
  Bar,
  BarChart,
  Cell,
  LabelList,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  type YAxisTickContentProps,
} from "recharts";
import {
  fetchReceivablesAging,
  fetchValuation,
  type AgingResponse,
  type ValuationRow,
} from "@/lib/services/api";
import { fmtMoney, fmtNumber } from "@/lib/format";
import { getDictionary, localeTag, type Locale } from "@/lib/i18n";
import { EmptyState, ErrorState, LoadingSkeleton } from "@/components/uikit";

// Chart slots in the reference order (docs/design/ui-direction.md §2 — charts 1–5).
// CSS vars, not hex: the inline SVG re-colors itself when the theme flips, so the
// component never branches on theme.
const CHART_COLORS = ["var(--primary)", "var(--secondary)", "var(--success)", "var(--warning)"];

const CHART_HEIGHT = 260;

const TOOLTIP_STYLE = {
  background: "var(--popover)",
  border: "1px solid var(--border)",
  borderRadius: "var(--radius)",
  fontSize: "12px",
  padding: "8px 12px",
} as const;

type ChartState<T> =
  | { phase: "loading" }
  | { phase: "error" }
  | { phase: "empty" }
  | { phase: "success"; data: T };

type ChartProps = { token: string | null; locale: Locale; currency: string };

function ChartFrame({
  title,
  caption,
  state,
  labels,
  onRetry,
  children,
}: {
  title: string;
  caption: string;
  state: "loading" | "error" | "empty" | "success";
  labels: { loading: string; empty: string; loadFailed: string; retry: string };
  onRetry: () => void;
  children: React.ReactNode;
}) {
  return (
    <section className="flex flex-col gap-3" aria-label={title}>
      <h3 className="t-title">{title}</h3>
      <p className="card__subtitle">{caption}</p>
      {state === "loading" ? (
        <div className="grid place-content-center" style={{ height: CHART_HEIGHT }}>
          <LoadingSkeleton rows={4} label={labels.loading} />
        </div>
      ) : state === "error" ? (
        <ErrorState message={labels.loadFailed} retryLabel={labels.retry} onRetry={onRetry} />
      ) : state === "empty" ? (
        <EmptyState title={labels.empty} />
      ) : (
        children
      )}
    </section>
  );
}

// ── AR aging donut ──────────────────────────────────────────────────────────
// Buckets read clockwise from 12 (current → oldest). Segments carry a 2px
// surface gap (paddingAngle + card-colored stroke); the legend below repeats
// every bucket's label + amount + share, so identity and values never ride on
// color alone (palette contrast relief — ui-direction chart colors are fixed).

function AgingDonut({ token, locale, currency }: ChartProps) {
  const t = getDictionary(locale);
  const [state, setState] = useState<ChartState<AgingResponse>>({ phase: "loading" });

  const load = useCallback(async () => {
    if (!token) return;
    setState({ phase: "loading" });
    const res = await fetchReceivablesAging(token);
    if (!res.ok) return setState({ phase: "error" });
    setState(res.data.totalOpen <= 0 ? { phase: "empty" } : { phase: "success", data: res.data });
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  if (state.phase !== "success") {
    return (
      <ChartFrame
        title={t.dashboard.charts.arAging.title}
        caption={t.dashboard.charts.arAging.caption}
        state={state.phase}
        labels={{
          loading: t.common.loading,
          empty: t.dashboard.charts.arAging.empty,
          loadFailed: t.dashboard.charts.arAging.loadFailed,
          retry: t.common.retry,
        }}
        onRetry={() => void load()}
      >
        {null}
      </ChartFrame>
    );
  }

  const buckets = state.data.buckets;
  const total = state.data.totalOpen;
  return (
    <ChartFrame
      title={t.dashboard.charts.arAging.title}
      caption={t.dashboard.charts.arAging.caption}
      state="success"
      labels={{
        loading: t.common.loading,
        empty: t.dashboard.charts.arAging.empty,
        loadFailed: t.dashboard.charts.arAging.loadFailed,
        retry: t.common.retry,
      }}
      onRetry={() => void load()}
    >
      <div className="relative" style={{ height: CHART_HEIGHT }}>
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={buckets}
              dataKey="amount"
              nameKey="label"
              innerRadius="64%"
              outerRadius="92%"
              paddingAngle={buckets.length > 1 ? 2 : 0}
              stroke="var(--card)"
              strokeWidth={2}
              startAngle={90}
              endAngle={-270}
              isAnimationActive={false}
            >
              {buckets.map((bucket, i) => (
                <Cell key={bucket.label} fill={CHART_COLORS[i % CHART_COLORS.length]} />
              ))}
            </Pie>
            <Tooltip
              contentStyle={TOOLTIP_STYLE}
              itemStyle={{ color: "var(--popover-foreground)" }}
              formatter={(value) => fmtMoney(Number(value), locale, currency)}
            />
          </PieChart>
        </ResponsiveContainer>
        <div className="pointer-events-none absolute inset-0 grid place-content-center text-center">
          <p className="t-caption" style={{ margin: 0 }}>
            {t.dashboard.charts.arAging.totalOpen}
          </p>
          <p style={{ margin: 0, fontSize: "22px", lineHeight: "28px", fontWeight: 700, letterSpacing: "-0.02em" }}>
            {fmtMoney(total, locale, currency)}
          </p>
        </div>
      </div>
      <ul className="flex flex-wrap gap-x-5 gap-y-2" style={{ listStyle: "none", margin: 0, padding: 0, fontSize: "12px" }}>
        {buckets.map((bucket, i) => (
          <li key={bucket.label} className="flex items-center gap-2">
            <span
              aria-hidden="true"
              style={{
                inlineSize: 10,
                blockSize: 10,
                borderRadius: 3,
                flexShrink: 0,
                background: CHART_COLORS[i % CHART_COLORS.length],
              }}
            />
            <span style={{ fontWeight: 600 }}>{bucket.label}</span>
            <span style={{ color: "var(--muted-foreground)" }}>{fmtMoney(bucket.amount, locale, currency)}</span>
            <span style={{ color: "var(--muted-foreground)" }}>
              {fmtNumber(total > 0 ? Math.round((bucket.amount / total) * 100) : 0, locale)}%
            </span>
          </li>
        ))}
      </ul>
    </ChartFrame>
  );
}

// ── Top stock bars ──────────────────────────────────────────────────────────
// Single series (value) → no legend; every bar carries its value at the tip,
// so the value-axis ticks stay hidden (marks spec: ticks only when values
// aren't labeled). Bars ≤24px, 4px rounded data-end, square at the baseline.

// Category ticks never clip: SKUs longer than the axis budget are middle-
// truncated to their prefix + tail (the tail is what distinguishes the pilot
// SKUs); the full SKU stays reachable via the native <title> and the tooltip.
// 12 chars ≈ 87px at 12px DM Sans — inside the 104px axis box.
const SKU_TICK_BUDGET = 12;

function skuTick({ x, y, payload }: YAxisTickContentProps) {
  const sku = String(payload?.value ?? "");
  const short = sku.length <= SKU_TICK_BUDGET ? sku : `${sku.slice(0, 5)}…${sku.slice(-6)}`;
  return (
    <text x={x} y={y} dy={4} textAnchor="end" fill="var(--muted-foreground)" fontSize={12}>
      <title>{sku}</title>
      {short}
    </text>
  );
}

function TopStock({ token, locale, currency }: ChartProps) {
  const t = getDictionary(locale);
  const [state, setState] = useState<ChartState<ValuationRow[]>>({ phase: "loading" });

  const load = useCallback(async () => {
    if (!token) return;
    setState({ phase: "loading" });
    const res = await fetchValuation(token);
    if (!res.ok) return setState({ phase: "error" });
    setState(res.data.length === 0 ? { phase: "empty" } : { phase: "success", data: res.data });
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  if (state.phase !== "success") {
    return (
      <ChartFrame
        title={t.dashboard.charts.topStock.title}
        caption={t.dashboard.charts.topStock.caption}
        state={state.phase}
        labels={{
          loading: t.common.loading,
          empty: t.dashboard.charts.topStock.empty,
          loadFailed: t.dashboard.charts.topStock.loadFailed,
          retry: t.common.retry,
        }}
        onRetry={() => void load()}
      >
        {null}
      </ChartFrame>
    );
  }

  const top = [...state.data].sort((a, b) => b.value - a.value).slice(0, 5);
  const compactMoney = (value: number) =>
    new Intl.NumberFormat(localeTag(locale), {
      style: "currency",
      currency,
      notation: "compact",
      maximumFractionDigits: 1,
    }).format(value);

  return (
    <ChartFrame
      title={t.dashboard.charts.topStock.title}
      caption={t.dashboard.charts.topStock.caption}
      state="success"
      labels={{
        loading: t.common.loading,
        empty: t.dashboard.charts.topStock.empty,
        loadFailed: t.dashboard.charts.topStock.loadFailed,
        retry: t.common.retry,
      }}
      onRetry={() => void load()}
    >
      <div style={{ height: CHART_HEIGHT }}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={top} layout="vertical" margin={{ top: 8, right: 88, bottom: 0, left: 0 }}>
            <XAxis type="number" hide domain={[0, (dataMax: number) => Math.ceil(dataMax * 1.15)]} />
            <YAxis
              type="category"
              dataKey="itemSku"
              width={104}
              tick={skuTick}
              axisLine={false}
              tickLine={false}
            />
            <Tooltip
              cursor={{ fill: "var(--muted)" }}
              contentStyle={TOOLTIP_STYLE}
              itemStyle={{ color: "var(--popover-foreground)" }}
              formatter={(value) => fmtMoney(Number(value), locale, currency)}
            />
            <Bar dataKey="value" fill="var(--primary)" barSize={16} radius={[0, 4, 4, 0]} isAnimationActive={false}>
              <LabelList
                dataKey="value"
                position="right"
                fill="var(--foreground)"
                fontSize={12}
                formatter={(value) => compactMoney(Number(value))}
              />
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </ChartFrame>
  );
}

// ── Card: both charts side by side (1-col mobile, 2-col ≥768px) ─────────────

export function DashboardCharts({ token, locale, currency }: ChartProps) {
  const t = getDictionary(locale);
  return (
    <section className="card" aria-labelledby="dashboard-charts-title">
      <h2 id="dashboard-charts-title" className="t-title">
        {t.dashboard.charts.title}
      </h2>
      <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
        <AgingDonut token={token} locale={locale} currency={currency} />
        <TopStock token={token} locale={locale} currency={currency} />
      </div>
    </section>
  );
}
