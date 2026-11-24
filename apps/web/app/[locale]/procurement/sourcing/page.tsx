"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchMe } from "@/lib/services/api";
import {
  createSourcingEvent,
  fetchSourcingEvents,
  procError,
  type SourcingEvent,
  type SourcingEventStatus,
  type SourcingEventType,
} from "@/lib/services/procurement";
import { fmtDate } from "@/lib/format";
import { hasPerm } from "../_lib/ui";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Select, Table, type BadgeTone } from "@/components/uikit";
import type { Column } from "@/components/uikit";

type Phase = "loading" | "error" | "success";
type FormState = { title: string; eventType: SourcingEventType; opensAt: string; closesAt: string; timeZoneName: string; currency: string };

const EMPTY_FORM: FormState = { title: "", eventType: "Rfq", opensAt: "", closesAt: "", timeZoneName: "Asia/Dhaka", currency: "BDT" };

// Status → tone rides the badge dot, the label text always stays --text.
const STATUS_TONES: Record<string, BadgeTone> = {
  Draft: "muted",
  Published: "success",
  Closed: "muted",
  Awarded: "success",
  NoAward: "warning",
  Cancelled: "danger",
};

function toIso(local: string): string | null {
  const d = new Date(local);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

export default function SourcingEventsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const p = t.procurement;
  const s = p.sourcing;

  const [status, setStatus] = useState("");
  const [eventType, setEventType] = useState("");
  const [events, setEvents] = useState<SourcingEvent[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);

  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [errors, setErrors] = useState<Partial<Record<keyof FormState, string>>>({});
  const [createError, setCreateError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const [meId, setMeId] = useState<string | null>(null);
  const [canWrite, setCanWrite] = useState<boolean | null>(null);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchMe(token).then((res) => {
      if (cancelled) return;
      if (res.ok) {
        setMeId(res.data.user.id);
        setCanWrite(hasPerm(res.data.permissions, "sourcing:write"));
      } else setCanWrite(false);
    });
    return () => { cancelled = true; };
  }, [token]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchSourcingEvents(token, { status: status || undefined, eventType: eventType || undefined });
    if (res.ok) {
      setEvents(res.data);
      setPhase("success");
    } else {
      setEvents(null);
      setErrorCode(res.code);
      setPhase("error");
    }
  }, [token, status, eventType]);
  useEffect(() => { void load(); }, [load]);

  const errMap: Record<string, string> = {
    FORBIDDEN: p.common.forbidden,
  };

  function validate(): boolean {
    const next: Partial<Record<keyof FormState, string>> = {};
    if (!form.title.trim()) next.title = s.errors.titleRequired;
    if (!form.timeZoneName.trim()) next.timeZoneName = s.errors.timeZoneRequired;
    if (!form.currency.trim()) next.currency = s.errors.currencyRequired;
    const opens = toIso(form.opensAt);
    const closes = toIso(form.closesAt);
    if (!opens || !closes || opens >= closes) next.closesAt = s.errors.windowInvalid;
    setErrors(next);
    return Object.keys(next).length > 0;
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    setCreateError(null);
    if (validate() || !token) return;
    setCreating(true);
    const res = await createSourcingEvent(token, {
      title: form.title.trim(),
      eventType: form.eventType,
      opensAt: toIso(form.opensAt) ?? form.opensAt,
      closesAt: toIso(form.closesAt) ?? form.closesAt,
      timeZoneName: form.timeZoneName.trim(),
      currency: form.currency.trim().toUpperCase(),
    });
    setCreating(false);
    if (!res.ok) {
      setCreateError(procError(res.code, res.message, errMap, s.createFailed));
      return;
    }
    setEvents((prev) => [res.data, ...(prev ?? [])]);
    setForm(EMPTY_FORM);
  }

  const statusLabel = (v: string) => (p.statuses as Record<string, string>)[v] ?? v;
  const columns: Column<SourcingEvent>[] = [
    { key: "id", label: s.columns.id, mono: true, render: (e) => e.id },
    { key: "title", label: s.columns.title, render: (e) => e.title },
    { key: "type", label: s.columns.type, render: (e) => <Badge tone="muted">{statusLabel(e.eventType)}</Badge> },
    { key: "status", label: s.columns.status, render: (e) => <Badge tone={STATUS_TONES[e.status] ?? "muted"}>{statusLabel(e.status)}</Badge> },
    { key: "opens", label: s.columns.opens, render: (e) => <span className="t-caption">{fmtDate(e.opensAt, locale)} · {e.timeZoneName}</span> },
    { key: "closes", label: s.columns.closes, render: (e) => <span className="t-caption">{fmtDate(e.closesAt, locale)}</span> },
    { key: "currency", label: s.columns.currency, mono: true, render: (e) => e.currency },
    { key: "version", label: s.columns.version, mono: true, render: (e) => `v${e.version}` },
    {
      key: "actions",
      label: s.columns.actions,
      render: (e) => (
        <div className="row-actions">
          <Link className="btn btn--secondary btn--sm" href={`/${locale}/procurement/sourcing/${e.id}`}>
            {s.openWorkspace}
          </Link>
        </div>
      ),
    },
  ];

  if (!ready || (phase === "loading" && events === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    const forbidden = errorCode === "FORBIDDEN" || errorCode === "HTTP_403";
    return (
      <div className="stack">
        <div className="page-header">
          <h1 className="t-display">{s.title}</h1>
          <p className="t-caption">{s.subtitle}</p>
        </div>
        <div className="state state--error" role="alert">
          <p className="state__title">{forbidden ? s.forbidden : s.loadFailed}</p>
          {!forbidden ? <Button variant="secondary" onClick={() => void load()}>{t.common.retry}</Button> : null}
        </div>
      </div>
    );
  }
  const list = events ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{s.title}</h1>
        <p className="t-caption">{s.subtitle}</p>
      </div>
      {createError ? <p className="banner" role="alert">{createError}</p> : null}

      {canWrite === false ? null : (
        <section className="card">
          <h2 className="t-title">{s.newTitle}</h2>
          <form onSubmit={(e) => void submit(e)}>
            <div className="form-grid">
              <Input id="ev-title" label={s.form.title} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} error={errors.title} />
              <Select
                id="ev-type"
                label={s.form.eventType}
                options={(["Rfq", "Itb", "Rfi"] as SourcingEventType[]).map((v) => ({ value: v, label: statusLabel(v) }))}
                value={form.eventType}
                onChange={(e) => setForm({ ...form, eventType: e.target.value as SourcingEventType })}
              />
              <Input id="ev-opens" label={s.form.opensAt} type="datetime-local" value={form.opensAt} onChange={(e) => setForm({ ...form, opensAt: e.target.value })} />
              <Input id="ev-closes" label={s.form.closesAt} type="datetime-local" value={form.closesAt} onChange={(e) => setForm({ ...form, closesAt: e.target.value })} error={errors.closesAt} />
              <Input id="ev-tz" label={s.form.timeZone} hint={s.form.timeZoneHint} value={form.timeZoneName} onChange={(e) => setForm({ ...form, timeZoneName: e.target.value })} error={errors.timeZoneName} />
              <Input id="ev-currency" label={s.form.currency} value={form.currency} onChange={(e) => setForm({ ...form, currency: e.target.value })} error={errors.currency} />
            </div>
            <p>
              <Button type="submit" loading={creating} disabled={meId === null}>{s.newTitle}</Button>
            </p>
          </form>
        </section>
      )}

      <section className="card">
        <div className="form-grid">
          <Select
            id="ev-filter-status"
            label={s.filters.status}
            placeholder={s.filters.all}
            options={(["Draft", "Published", "Closed", "Awarded", "NoAward", "Cancelled"] as SourcingEventStatus[]).map((v) => ({ value: v, label: statusLabel(v) }))}
            value={status}
            onChange={(e) => setStatus(e.target.value)}
          />
          <Select
            id="ev-filter-type"
            label={s.filters.type}
            placeholder={s.filters.all}
            options={(["Rfq", "Itb", "Rfi"] as SourcingEventType[]).map((v) => ({ value: v, label: statusLabel(v) }))}
            value={eventType}
            onChange={(e) => setEventType(e.target.value)}
          />
        </div>
        {list.length === 0 ? <EmptyState title={s.empty} /> : <Table<SourcingEvent> columns={columns} rows={list} caption={s.title} />}
      </section>
    </div>
  );
}
