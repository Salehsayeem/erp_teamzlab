"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createScheduleVersion, createWorkSchedule, expandSchedule, fetchWorkSchedules,
  type ScheduleDay, type ScheduleRow,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Table, ToastRegion, type ToastItem } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPermission, peopleActionError, usePeopleMe } from "../_lib/helpers";

export default function SchedulesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.schedules;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canWrite = hasPermission(permissions, "schedules:write");

  const [schedules, setSchedules] = useState<ScheduleRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [schedule, setSchedule] = useState({ code: "", name: "", timezone: "Asia/Dhaka", startTimeLocal: "09:00" });
  const [version, setVersion] = useState({ scheduleId: "", weeklyPlannedHours: "40", effectiveFrom: "", holidayCalendarId: "" });
  const [expansion, setExpansion] = useState({ scheduleId: "", from: "", to: "" });
  const [days, setDays] = useState<ScheduleDay[] | null>(null);

  const push = (tone: ToastItem["tone"], message: string) => setToasts((p) => [...p, { id: crypto.randomUUID(), tone, message }]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchWorkSchedules(token);
    if (res.ok) {
      setSchedules(res.data);
      setPhase("success");
    } else {
      setSchedules(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function submitSchedule(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    setBusy(true); setFormError(null);
    const res = await createWorkSchedule(token, {
      code: schedule.code.trim(), name: schedule.name.trim(),
      timezone: schedule.timezone.trim(), startTimeLocal: schedule.startTimeLocal.trim(),
    });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", res.data.code);
    void load();
  }

  async function submitVersion(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    setBusy(true); setFormError(null);
    const res = await createScheduleVersion(token, version.scheduleId.trim(), {
      weeklyPlannedHours: Number(version.weeklyPlannedHours) || 0, effectiveFrom: version.effectiveFrom,
      holidayCalendarId: version.holidayCalendarId.trim() || null,
    });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", version.scheduleId);
    void load();
  }

  async function submitExpansion(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    setBusy(true); setFormError(null); setDays(null);
    const res = await expandSchedule(token, expansion.scheduleId.trim(), expansion.from, expansion.to);
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    setDays(res.data.days);
  }

  const columns: Column<ScheduleRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.date, mono: true, render: (s) => s.code },
    { key: "name", label: tc.columns.date, render: (s) => s.name },
    { key: "tz", label: tc.columns.date, mono: true, render: (s) => s.timezone + " · " + s.startTimeLocal },
    { key: "versions", label: tc.columns.date, render: (s) => s.versions.map((v) => `v${v.version}: ${v.weeklyPlannedHours}h @ ${v.effectiveFrom}`).join(" · ") || "—" },
    { key: "status", label: tc.columns.date, render: (s) => <Badge tone="success">{tc.columns.workday}</Badge> },
  ], [tc]);

  if (!ready || phase === "loading") return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={(id) => setToasts((p) => p.filter((x) => x.id !== id))} closeLabel={t.common.cancel} />
      {formError ? <p className="banner" role="alert">{formError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.expansionTitle}</h2>
        <form onSubmit={(e) => void submitExpansion(e)} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
          <Input id="sc-id" label={tc.code} value={expansion.scheduleId} onChange={(e) => setExpansion({ ...expansion, scheduleId: e.target.value })} />
          <Input id="sc-from" label={tc.from} type="date" value={expansion.from} onChange={(e) => setExpansion({ ...expansion, from: e.target.value })} />
          <Input id="sc-to" label={tc.to} type="date" value={expansion.to} onChange={(e) => setExpansion({ ...expansion, to: e.target.value })} />
          <Button type="submit" loading={busy}>{tc.expand}</Button>
        </form>
        {days && days.length > 0 ? (
          <div style={{ marginTop: "1rem" }}>
            <Table
              caption={tc.expansionTitle}
              columns={[
                { key: "date", label: tc.columns.date, mono: true, render: (d: ScheduleDay) => d.date },
                { key: "work", label: tc.columns.workday, render: (d: ScheduleDay) => (d.isWorkday ? "✓" : "—") },
                { key: "hours", label: tc.columns.hours, mono: true, render: (d: ScheduleDay) => String(d.plannedHours) },
                { key: "holiday", label: tc.columns.holiday, render: (d: ScheduleDay) => d.holiday ?? "—" },
                { key: "interval", label: tc.columns.interval, mono: true, render: (d: ScheduleDay) => d.intervals.map((i) => `${i.startUtc} → ${i.endUtc}`).join(", ") || "—" },
              ]}
              rows={days}
            />
          </div>
        ) : null}
      </section>

      {canWrite === true ? (
        <div className="grid-2">
          <section className="card">
            <h2 className="t-title">{tc.scheduleFormTitle}</h2>
            <form onSubmit={(e) => void submitSchedule(e)}>
              <div className="form-grid">
                <Input id="sc-code" label={tc.code} value={schedule.code} onChange={(e) => setSchedule({ ...schedule, code: e.target.value })} />
                <Input id="sc-name" label={tc.name} value={schedule.name} onChange={(e) => setSchedule({ ...schedule, name: e.target.value })} />
                <Input id="sc-tz" label={tc.timezone} value={schedule.timezone} onChange={(e) => setSchedule({ ...schedule, timezone: e.target.value })} />
                <Input id="sc-start" label={tc.startTime} value={schedule.startTimeLocal} onChange={(e) => setSchedule({ ...schedule, startTimeLocal: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createSchedule}</Button></p>
            </form>
          </section>
          <section className="card">
            <h2 className="t-title">{tc.versionFormTitle}</h2>
            <form onSubmit={(e) => void submitVersion(e)}>
              <div className="form-grid">
                <Input id="sv-id" label={tc.code} value={version.scheduleId} onChange={(e) => setVersion({ ...version, scheduleId: e.target.value })} />
                <Input id="sv-hours" label={tc.weeklyHours} type="number" value={version.weeklyPlannedHours} onChange={(e) => setVersion({ ...version, weeklyPlannedHours: e.target.value })} />
                <Input id="sv-from" label={tc.effectiveFrom} type="date" value={version.effectiveFrom} onChange={(e) => setVersion({ ...version, effectiveFrom: e.target.value })} />
                <Input id="sv-cal" label={tc.holidayCalendar} value={version.holidayCalendarId} onChange={(e) => setVersion({ ...version, holidayCalendarId: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.addVersion}</Button></p>
            </form>
          </section>
        </div>
      ) : null}

      <section className="card">
        {(schedules ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={columns} rows={schedules ?? []} caption={tc.title} />}
      </section>
    </div>
  );
}
