"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  approveFxRate,
  createCurrency,
  fetchCurrencies,
  fetchFxRates,
  type Currency,
  type FxRate,
} from "@/lib/services/finance";
import { fmtDate } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  statusTone,
  Table,
  Tabs,
} from "@/components/uikit";

type Phase = "loading" | "error" | "success";

// Finance admin: currency master (006) + FX-rate provenance and approval (008).
// Two tabs share one load pass; the approve action confirms through a Modal that
// names the exact rate id, pair, value and source before the call fires.
export default function CurrenciesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const f = t.fin.fx;
  const { token, ready } = useAuth(locale);
  const [currencies, setCurrencies] = useState<Currency[] | null>(null);
  const [rates, setRates] = useState<FxRate[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [tab, setTab] = useState("currencies");
  const [banner, setBanner] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState({ code: "", name: "", scale: "2", active: true });
  const [formError, setFormError] = useState<string | null>(null);
  const [approving, setApproving] = useState<FxRate | null>(null);
  const [approveBusy, setApproveBusy] = useState(false);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [cur, fx] = await Promise.all([fetchCurrencies(token), fetchFxRates(token)]);
    if (cur.ok && fx.ok) {
      setCurrencies(cur.data);
      setRates(fx.data);
      setPhase("success");
    } else {
      setCurrencies(null);
      setRates(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function submitCurrency() {
    if (!token) return;
    setFormError(null);
    const scale = Number(form.scale);
    if (!/^[A-Za-z]{3}$/.test(form.code)) { setFormError(f.invalidInput); return; }
    if (scale !== 0 && scale !== 2 && scale !== 3) { setFormError(f.invalidInput); return; }
    setCreating(true);
    const res = await createCurrency(token, { code: form.code.toUpperCase(), name: form.name, decimalScale: scale, active: form.active });
    setCreating(false);
    if (!res.ok) { setBanner(res.code === "NETWORK" ? t.common.networkError : `${f.actionFailed}: ${res.message}`); return; }
    setForm({ code: "", name: "", scale: "2", active: true });
    await load();
  }

  async function confirmApprove() {
    if (!token || !approving) return;
    setApproveBusy(true);
    const res = await approveFxRate(token, approving.id);
    setApproveBusy(false);
    setApproving(null);
    if (!res.ok) { setBanner(res.code === "NETWORK" ? t.common.networkError : `${f.actionFailed}: ${res.message}`); return; }
    await load();
  }

  if (!ready || phase === "loading") return <LoadingSkeleton rows={6} label={t.common.loading} />;
  if (phase === "error") return <ErrorState message={f.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{f.title}</h1>
        <p className="t-caption">{f.subtitle}</p>
      </div>
      {banner ? <p className="banner" role="alert">{banner}</p> : null}
      <Tabs
        label={f.title}
        activeId={tab}
        onChange={setTab}
        tabs={[
          {
            id: "currencies",
            label: f.currenciesTab,
            content: (
              <div className="stack">
                <section className="card">
                  {currencies !== null && currencies.length === 0 ? (
                    <EmptyState title={f.empty} />
                  ) : (
                    <Table
                      caption={f.title}
                      rows={currencies ?? []}
                      columns={[
                        { key: "code", label: f.columns.code, mono: true, render: (c) => c.code },
                        { key: "name", label: f.columns.name, render: (c) => c.name },
                        { key: "scale", label: f.columns.scale, mono: true, render: (c) => String(c.decimalScale) },
                        { key: "active", label: f.columns.active, render: (c) => (
                          <Badge tone={c.active ? "success" : "muted"}>{c.active ? f.columns.active : "—"}</Badge>
                        ) },
                      ]}
                    />
                  )}
                </section>
                <section className="card">
                  <h2 className="t-title">{f.createTitle}</h2>
                  <div className="form">
                    <Input id="cur-code" label={f.codeLabel} className="input mono" value={form.code} maxLength={3}
                      onChange={(e) => setForm({ ...form, code: e.target.value.toUpperCase() })} />
                    <Input id="cur-name" label={f.nameLabel} value={form.name}
                      onChange={(e) => setForm({ ...form, name: e.target.value })} />
                    <Input id="cur-scale" label={f.scaleLabel} hint={f.scaleHint} className="input mono" type="number" min="0" step="1"
                      value={form.scale} onChange={(e) => setForm({ ...form, scale: e.target.value })} />
                    <label className="field" htmlFor="cur-active">
                      {f.activeLabel}
                      <input id="cur-active" type="checkbox" checked={form.active}
                        onChange={(e) => setForm({ ...form, active: e.target.checked })} />
                    </label>
                    <div>
                      <Button loading={creating} disabled={creating} onClick={() => void submitCurrency()}>{f.create}</Button>
                    </div>
                  </div>
                </section>
              </div>
            ),
          },
          {
            id: "rates",
            label: f.ratesTab,
            content: (
              <section className="card">
                {rates !== null && rates.length === 0 ? (
                  <EmptyState title={f.emptyRates} />
                ) : (
                  <Table
                    caption={f.ratesTab}
                    rows={rates ?? []}
                    columns={[
                      { key: "id", label: f.columns.id, mono: true, render: (r) => r.id },
                      { key: "pair", label: f.columns.pair, mono: true, render: (r) => `${r.baseCurrency}/${r.quoteCurrency}` },
                      { key: "date", label: f.columns.date, mono: true, render: (r) => r.rateDate },
                      { key: "type", label: f.columns.type, render: (r) => r.rateType },
                      { key: "rate", label: f.columns.rate, mono: true, render: (r) => r.rate.toFixed(Math.min(r.precision, 6)) },
                      { key: "precision", label: f.columns.precision, mono: true, render: (r) => String(r.precision) },
                      { key: "source", label: f.columns.source, render: (r) => r.source },
                      { key: "retrievedAt", label: f.columns.retrievedAt, render: (r) => <span className="t-caption">{fmtDate(r.retrievedAt, locale)}</span> },
                      { key: "status", label: f.columns.status, render: (r) => <Badge tone={statusTone(r.status)}>{r.status}</Badge> },
                      { key: "approver", label: f.columns.approver, render: (r) => r.approvedBy ?? "—" },
                      { key: "actions", label: t.journals.actions, render: (r) =>
                        r.status === "Pending" ? (
                          <Button small onClick={() => setApproving(r)}>{f.approve}</Button>
                        ) : null },
                    ]}
                  />
                )}
              </section>
            ),
          },
        ]}
      />
      <Modal open={approving !== null} onClose={() => setApproving(null)} title={f.approveTitle} closeLabel={t.common.cancel} busy={approveBusy}>
        {approving ? (
          <div className="stack">
            <p>{f.approveBody
              .replace("{id}", approving.id)
              .replace("{base}", approving.baseCurrency)
              .replace("{quote}", approving.quoteCurrency)
              .replace("{rate}", approving.rate.toFixed(Math.min(approving.precision, 6)))
              .replace("{type}", approving.rateType)
              .replace("{date}", approving.rateDate)
              .replace("{source}", approving.source)}</p>
            <div className="row-actions">
              <Button variant="secondary" onClick={() => setApproving(null)}>{t.common.cancel}</Button>
              <Button loading={approveBusy} disabled={approveBusy} onClick={() => void confirmApprove()}>{f.approve}</Button>
            </div>
          </div>
        ) : null}
      </Modal>
    </div>
  );
}
