"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Dict, type Locale } from "@/lib/i18n";
import {
  activateFiscalCalendar,
  closeFiscalPeriod,
  createFiscalCalendar,
  fetchFiscalCalendars,
  fetchFiscalPeriods,
  reopenFiscalPeriod,
  type FiscalCalendar,
  type FiscalPeriodRow,
} from "@/lib/services/finance";
import { useAuth } from "@/hooks/useAuth";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  Select,
  Table,
} from "@/components/uikit";

type Phase = "loading" | "error" | "success";

// API error codes → errors.<CODE> catalog lines; the API's English detail rides along.
function catalogError(t: Dict, code: string, message: string, fallback: string): string {
  if (code === "NETWORK") return t.common.networkError;
  const line = (t.errors as unknown as Record<string, string>)[code];
  return line ? `${line}: ${message}` : `${fallback}: ${message}`;
}

const PERIOD_TONES: Record<string, "muted" | "warning" | "success" | "danger"> = {
  Open: "success",
  SoftClosed: "warning",
  Closed: "danger",
};

// Finance periods (004/005): calendar layouts (4-4-5 + adjustment period) with activation,
// and the period status board whose close/reopen confirm modals carry the reason + the
// 063 waiver variant. Errors surface in-modal — never a hidden failed state.
export default function FiscalPeriodsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const f = t.fin.periods;
  const { token, ready } = useAuth(locale);
  const [periods, setPeriods] = useState<FiscalPeriodRow[] | null>(null);
  const [calendars, setCalendars] = useState<FiscalCalendar[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [banner, setBanner] = useState<string | null>(null);

  const [form, setForm] = useState({ name: "", year: String(new Date().getFullYear() + 1), pattern: "FourFourFive", startDate: "" });
  const [formError, setFormError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const [activating, setActivating] = useState<FiscalCalendar | null>(null);
  const [activateBusy, setActivateBusy] = useState(false);

  const [closing, setClosing] = useState<FiscalPeriodRow | null>(null);
  const [closeForm, setCloseForm] = useState({ reason: "", waive: false });
  const [closeError, setCloseError] = useState<string | null>(null);
  const [closeBusy, setCloseBusy] = useState(false);

  const [reopening, setReopening] = useState<FiscalPeriodRow | null>(null);
  const [reopenReason, setReopenReason] = useState("");
  const [reopenError, setReopenError] = useState<string | null>(null);
  const [reopenBusy, setReopenBusy] = useState(false);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [per, cal] = await Promise.all([fetchFiscalPeriods(token), fetchFiscalCalendars(token)]);
    if (per.ok && cal.ok) {
      setPeriods(per.data);
      setCalendars(cal.data);
      setPhase("success");
    } else {
      setPeriods(null);
      setCalendars(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function submitCalendar() {
    if (!token) return;
    setFormError(null);
    const year = Number(form.year);
    const name = form.name.trim();
    if (!name || !/^\d{4}$/.test(form.year) || year < 2000 || year > 2200
      || !/^\d{4}-\d{2}-\d{2}$/.test(form.startDate) || !form.startDate.startsWith(form.year)) {
      setFormError(f.invalidInput);
      return;
    }
    setCreating(true);
    const res = await createFiscalCalendar(token, { name, year, pattern: form.pattern, startDate: form.startDate });
    setCreating(false);
    if (!res.ok) { setFormError(catalogError(t, res.code, res.message, f.actionFailed)); return; }
    setForm({ name: "", year: String(year + 1), pattern: form.pattern, startDate: "" });
    await load();
  }

  async function confirmActivate() {
    if (!token || !activating) return;
    setActivateBusy(true);
    const res = await activateFiscalCalendar(token, activating.id);
    setActivateBusy(false);
    if (!res.ok) {
      setActivating(null);
      setBanner(catalogError(t, res.code, res.message, f.actionFailed));
      return;
    }
    setActivating(null);
    await load();
  }

  async function confirmClose() {
    if (!token || !closing) return;
    setCloseError(null);
    if (closeForm.waive && !closeForm.reason.trim()) { setCloseError(f.reasonWaiveRequired); return; }
    setCloseBusy(true);
    const res = await closeFiscalPeriod(token, closing.id, {
      reason: closeForm.reason.trim() || undefined,
      waiveBlockers: closeForm.waive || undefined,
    });
    setCloseBusy(false);
    if (!res.ok) { setCloseError(catalogError(t, res.code, res.message, f.actionFailed)); return; }
    setClosing(null);
    setCloseForm({ reason: "", waive: false });
    await load();
  }

  async function confirmReopen() {
    if (!token || !reopening) return;
    setReopenError(null);
    if (!reopenReason.trim()) { setReopenError(f.requiredReason); return; }
    setReopenBusy(true);
    const res = await reopenFiscalPeriod(token, reopening.id, { reason: reopenReason.trim() });
    setReopenBusy(false);
    if (!res.ok) { setReopenError(catalogError(t, res.code, res.message, f.actionFailed)); return; }
    setReopening(null);
    setReopenReason("");
    await load();
  }

  if (!ready || phase === "loading") return <LoadingSkeleton rows={7} label={t.common.loading} />;
  if (phase === "error") return <ErrorState message={f.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{f.title}</h1>
        <p className="t-caption">{f.subtitle}</p>
      </div>
      {banner ? <p className="banner" role="alert">{banner}</p> : null}

      <section className="card">
        <h2 className="t-title">{f.calendarsTitle}</h2>
        {calendars !== null && calendars.length === 0 ? (
          <EmptyState title={f.emptyCalendars} />
        ) : (
          <Table
            caption={f.calendarsTitle}
            rows={calendars ?? []}
            columns={[
              { key: "name", label: f.columns.calendar, render: (c) => c.name },
              { key: "id", label: t.fin.fx.columns.id, mono: true, render: (c) => c.id },
              { key: "year", label: f.yearLabel, mono: true, render: (c) => String(c.year) },
              { key: "pattern", label: f.patternLabel, render: (c) => c.pattern },
              { key: "range", label: f.columns.range, mono: true, render: (c) => `${c.startDate} → ${c.endDate}` },
              { key: "layout", label: f.columns.layout, render: (c) =>
                `${c.periods.length}${c.periods.some((p) => p.isAdjustment) ? ` + ${f.adjustment}` : ""}` },
              { key: "state", label: f.columns.state, render: (c) => (
                <Badge tone={c.activated ? "success" : "muted"}>{c.activated ? f.activated : f.draft}</Badge>
              ) },
              { key: "actions", label: f.columns.actions, render: (c) =>
                !c.activated ? (
                  <Button onClick={() => setActivating(c)}>{f.activate}</Button>
                ) : null },
            ]}
          />
        )}
        <h2 className="t-title">{f.createTitle}</h2>
        <div className="form">
          <Input id="cal-name" label={f.nameLabel} value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <Input id="cal-year" label={f.yearLabel} className="input mono" type="number" min="2000" max="2200" step="1"
            value={form.year} onChange={(e) => setForm({ ...form, year: e.target.value })} />
          <Select
            id="cal-pattern"
            label={f.patternLabel}
            value={form.pattern}
            onChange={(e) => setForm({ ...form, pattern: e.target.value })}
            options={[
              { value: "FourFourFive", label: "4-4-5" },
              { value: "CalendarMonths", label: "12 × month" },
            ]}
          />
          <Input id="cal-start" label={f.startDateLabel} className="input mono" type="date" value={form.startDate}
            onChange={(e) => setForm({ ...form, startDate: e.target.value })} />
          {formError ? <p className="field__error" role="alert">{formError}</p> : null}
          <div>
            <Button loading={creating} disabled={creating} onClick={() => void submitCalendar()}>{f.create}</Button>
          </div>
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{f.boardTitle}</h2>
        {periods !== null && periods.length === 0 ? (
          <EmptyState title={f.emptyPeriods} />
        ) : (
          <Table
            caption={f.boardTitle}
            rows={periods ?? []}
            columns={[
              { key: "name", label: f.columns.name, mono: true, render: (p) => p.name },
              { key: "range", label: f.columns.range, mono: true, render: (p) => `${p.startDate} → ${p.endDate}` },
              { key: "status", label: f.columns.status, render: (p) => (
                <Badge tone={PERIOD_TONES[p.status] ?? "muted"}>{p.status}</Badge>
              ) },
              { key: "adj", label: f.adjustment, render: (p) =>
                p.isAdjustment ? <Badge tone="warning">{f.adjustment}</Badge> : <span className="t-caption">—</span> },
              { key: "closed", label: f.columns.closed, render: (p) =>
                p.closedAt ? (
                  <span className="t-caption">{p.closedBy ?? "system"} · {p.closedAt.slice(0, 16).replace("T", " ")}</span>
                ) : "—" },
              { key: "actions", label: f.columns.actions, render: (p) => (
                <div className="row-actions">
                  {p.status !== "Closed" ? (
                    <Button onClick={() => { setClosing(p); setCloseForm({ reason: "", waive: false }); setCloseError(null); }}>
                      {f.closeAction}
                    </Button>
                  ) : null}
                  {p.status !== "Open" ? (
                    <Button variant="secondary" onClick={() => { setReopening(p); setReopenReason(""); setReopenError(null); }}>
                      {f.reopenAction}
                    </Button>
                  ) : null}
                </div>
              ) },
            ]}
          />
        )}
      </section>

      <Modal open={activating !== null} onClose={() => setActivating(null)} title={f.activateTitle} closeLabel={t.common.cancel} busy={activateBusy}>
        {activating ? (
          <div className="stack">
            <p>{f.activateBody.replace("{name}", activating.name).replace("{count}", String(activating.periods.length))}</p>
            <div className="row-actions">
              <Button variant="secondary" onClick={() => setActivating(null)}>{t.common.cancel}</Button>
              <Button loading={activateBusy} disabled={activateBusy} onClick={() => void confirmActivate()}>{f.activate}</Button>
            </div>
          </div>
        ) : null}
      </Modal>

      <Modal open={closing !== null} onClose={() => setClosing(null)} title={f.closeTitle} closeLabel={t.common.cancel} busy={closeBusy}>
        {closing ? (
          <div className="stack">
            <p>{f.closeBody
              .replace("{id}", `${closing.id} · ${closing.name}`)
              .replace("{range}", `${closing.startDate} → ${closing.endDate}`)}</p>
            <p className="t-caption">{f.closeBlockedHint}</p>
            <Input id="close-reason" label={f.reasonLabel} hint={closeForm.waive ? f.reasonWaiveRequired : undefined}
              value={closeForm.reason} onChange={(e) => setCloseForm({ ...closeForm, reason: e.target.value })} />
            <label className="field" htmlFor="close-waive">
              <span>
                <input id="close-waive" type="checkbox" checked={closeForm.waive}
                  onChange={(e) => setCloseForm({ ...closeForm, waive: e.target.checked })} />
                {" "}{f.waiveLabel}
              </span>
            </label>
            {closeForm.waive ? <p className="field__error" role="alert">{f.waiveWarning}</p> : null}
            {closeError ? <p className="field__error" role="alert">{closeError}</p> : null}
            <div className="row-actions">
              <Button variant="secondary" onClick={() => setClosing(null)}>{t.common.cancel}</Button>
              <Button variant="danger" loading={closeBusy} disabled={closeBusy} onClick={() => void confirmClose()}>
                {f.closeAction}
              </Button>
            </div>
          </div>
        ) : null}
      </Modal>

      <Modal open={reopening !== null} onClose={() => setReopening(null)} title={f.reopenTitle} closeLabel={t.common.cancel} busy={reopenBusy}>
        {reopening ? (
          <div className="stack">
            <p>{f.reopenBody.replace("{id}", `${reopening.id} · ${reopening.name}`)}</p>
            <Input id="reopen-reason" label={f.reasonLabel} value={reopenReason}
              onChange={(e) => setReopenReason(e.target.value)} error={reopenError ?? undefined} />
            {reopenError ? <p className="field__error" role="alert">{reopenError}</p> : null}
            <div className="row-actions">
              <Button variant="secondary" onClick={() => setReopening(null)}>{t.common.cancel}</Button>
              <Button loading={reopenBusy} disabled={reopenBusy} onClick={() => void confirmReopen()}>
                {f.reopenAction}
              </Button>
            </div>
          </div>
        ) : null}
      </Modal>
    </div>
  );
}
