"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  checkCompPolicy, createGradeRange, createJobProfile, fetchGradeRanges, fetchJobProfiles,
  type GradeRangeRow, type JobProfileRow, type PolicyCheckResult,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Table, ToastRegion, type ToastItem } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPermission, peopleActionError, usePeopleMe } from "../_lib/helpers";

export default function JobCatalogPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.jobcatalog;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canWrite = hasPermission(permissions, "compmaster:write");

  const [profiles, setProfiles] = useState<JobProfileRow[] | null>(null);
  const [ranges, setRanges] = useState<GradeRangeRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [profile, setProfile] = useState({ code: "", name: "", department: "", title: "", effectiveFrom: "" });
  const [range, setRange] = useState({ gradeCode: "", currency: "BDT", frequency: "Monthly", minAmount: "", midAmount: "", maxAmount: "", effectiveFrom: "" });
  const [policy, setPolicy] = useState({ workerId: "", amount: "" });
  const [result, setResult] = useState<PolicyCheckResult | null>(null);

  const push = (tone: ToastItem["tone"], message: string) => setToasts((p) => [...p, { id: crypto.randomUUID(), tone, message }]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [p, r] = await Promise.all([fetchJobProfiles(token), fetchGradeRanges(token)]);
    if (p.ok && r.ok) {
      setProfiles(p.data);
      setRanges(r.data);
      setPhase("success");
    } else {
      setProfiles(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function submitProfile(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    setBusy(true); setFormError(null);
    const res = await createJobProfile(token, {
      code: profile.code.trim(), name: profile.name.trim(), title: profile.title.trim(),
      effectiveFrom: profile.effectiveFrom,
    });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", res.data.code);
    void load();
  }

  async function submitRange(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    setBusy(true); setFormError(null);
    const res = await createGradeRange(token, {
      gradeCode: range.gradeCode.trim(), currency: range.currency.trim(), frequency: range.frequency.trim(),
      minAmount: Number(range.minAmount) || 0, midAmount: Number(range.midAmount) || 0,
      maxAmount: Number(range.maxAmount) || 0, effectiveFrom: range.effectiveFrom,
    });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", res.data.gradeCode);
    void load();
  }

  async function submitPolicy(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    setBusy(true); setFormError(null); setResult(null);
    const res = await checkCompPolicy(token, policy.workerId.trim(), {
      amount: Number(policy.amount) || 0, effectiveFrom: new Date().toISOString().slice(0, 10),
    });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    setResult(res.data);
  }

  const profileColumns: Column<JobProfileRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.code, mono: true, render: (p) => p.code },
    { key: "name", label: tc.columns.name, render: (p) => p.name },
    { key: "dept", label: tc.columns.dept, render: (p) => p.department ?? "—" },
    { key: "grade", label: tc.columns.grade, render: (p) => p.versions.map((v) => `v${v.version} ${v.title}`).join(", ") },
  ], [tc]);

  const rangeColumns: Column<GradeRangeRow>[] = useMemo(() => [
    { key: "grade", label: tc.columns.grade, mono: true, render: (r) => r.gradeCode },
    { key: "range", label: tc.columns.range, mono: true, render: (r) => `${fmtNumber(r.minAmount, locale)} – ${fmtNumber(r.maxAmount, locale)}` },
    { key: "from", label: tc.columns.from, mono: true, render: (r) => r.effectiveFrom },
  ], [tc, locale]);

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
        <h2 className="t-title">{tc.policyTitle}</h2>
        <form onSubmit={(e) => void submitPolicy(e)} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
          <Input id="jc-worker" label={tc.workerId} value={policy.workerId} onChange={(e) => setPolicy({ ...policy, workerId: e.target.value })} />
          <Input id="jc-amount" label={tc.amount} type="number" value={policy.amount} onChange={(e) => setPolicy({ ...policy, amount: e.target.value })} />
          <Button type="submit" loading={busy}>{tc.checkPolicy}</Button>
        </form>
        {result ? (
          <p className="banner" aria-live="polite">
            <Badge tone={result.inPolicy ? "success" : "warning"}>
              {result.inPolicy ? tc.inPolicy : tc.outOfPolicy}
            </Badge>{" "}
            {result.approvalRequired ? tc.approvalRequired : result.message}
          </p>
        ) : null}
      </section>

      {canWrite === true ? (
        <div className="grid-2">
          <section className="card">
            <h2 className="t-title">{tc.profileFormTitle}</h2>
            <form onSubmit={(e) => void submitProfile(e)}>
              <div className="form-grid">
                <Input id="jc-code" label={tc.code} value={profile.code} onChange={(e) => setProfile({ ...profile, code: e.target.value })} />
                <Input id="jc-name" label={tc.name} value={profile.name} onChange={(e) => setProfile({ ...profile, name: e.target.value })} />
                <Input id="jc-dept" label={tc.department} value={profile.department} onChange={(e) => setProfile({ ...profile, department: e.target.value })} />
                <Input id="jc-title" label={tc.profileTitle} value={profile.title} onChange={(e) => setProfile({ ...profile, title: e.target.value })} />
                <Input id="jc-from" label={tc.effectiveFrom} type="date" value={profile.effectiveFrom} onChange={(e) => setProfile({ ...profile, effectiveFrom: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createProfile}</Button></p>
            </form>
          </section>
          <section className="card">
            <h2 className="t-title">{tc.rangeFormTitle}</h2>
            <form onSubmit={(e) => void submitRange(e)}>
              <div className="form-grid">
                <Input id="gr-code" label={tc.gradeCode} value={range.gradeCode} onChange={(e) => setRange({ ...range, gradeCode: e.target.value })} />
                <Input id="gr-cur" label={tc.currency} value={range.currency} onChange={(e) => setRange({ ...range, currency: e.target.value })} />
                <Input id="gr-freq" label={tc.frequency} value={range.frequency} onChange={(e) => setRange({ ...range, frequency: e.target.value })} />
                <Input id="gr-min" label={tc.min} type="number" value={range.minAmount} onChange={(e) => setRange({ ...range, minAmount: e.target.value })} />
                <Input id="gr-mid" label={tc.mid} type="number" value={range.midAmount} onChange={(e) => setRange({ ...range, midAmount: e.target.value })} />
                <Input id="gr-max" label={tc.max} type="number" value={range.maxAmount} onChange={(e) => setRange({ ...range, maxAmount: e.target.value })} />
                <Input id="gr-from" label={tc.effectiveFrom} type="date" value={range.effectiveFrom} onChange={(e) => setRange({ ...range, effectiveFrom: e.target.value })} />
              </div>
              <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createRange}</Button></p>
            </form>
          </section>
        </div>
      ) : null}

      <section className="card">
        <h2 className="t-title">{tc.profilesTitle}</h2>
        {(profiles ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={profileColumns} rows={profiles ?? []} caption={tc.title} />}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.rangesTitle}</h2>
        {(ranges ?? []).length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={rangeColumns} rows={ranges ?? []} caption={tc.rangesTitle} />}
      </section>
    </div>
  );
}

