"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  completeOnboardingTask, createOnboardingTemplate, fetchOnboardingTasks, generateOnboardingTasks, waiveOnboardingTask,
  type OnboardingTaskWithOverdue,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtDay } from "../_lib/helpers";
import {
  Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Table,
  ToastRegion, type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../_lib/helpers";

export default function OnboardingPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.onboarding;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canWrite = hasPermission(permissions, "onboarding:write");

  const [workerId, setWorkerId] = useState("");
  const [tasks, setTasks] = useState<OnboardingTaskWithOverdue[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("success");
  const [loadedFor, setLoadedFor] = useState("");
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [waiving, setWaiving] = useState<OnboardingTaskWithOverdue | null>(null);
  const [waiveReason, setWaiveReason] = useState("");
  const [template, setTemplate] = useState({ categoryCode: "STAFF", taskKey: "", name: "", dueOffsetDays: "3", ownerRole: "R-HR", required: true });

  const push = (tone: ToastItem["tone"], message: string) => setToasts((p) => [...p, { id: crypto.randomUUID(), tone, message }]);

  const load = useCallback(async (id: string) => {
    if (!token || !id.trim()) return;
    setPhase("loading");
    const res = await fetchOnboardingTasks(token, id.trim());
    if (res.ok) {
      setTasks(res.data);
      setLoadedFor(id.trim());
      setPhase("success");
    } else {
      setTasks(null);
      setPhase("error");
    }
  }, [token]);

  async function generate(e: FormEvent) {
    e.preventDefault();
    if (!token || !workerId.trim()) return;
    setBusy(true); setFormError(null);
    const res = await generateOnboardingTasks(token, workerId.trim());
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", String(res.data.created));
    void load(workerId);
  }

  async function submitTemplate(e: FormEvent) {
    e.preventDefault();
    if (!token || !template.taskKey.trim() || !template.name.trim()) return;
    setBusy(true); setFormError(null);
    const res = await createOnboardingTemplate(token, {
      categoryCode: template.categoryCode.trim(), taskKey: template.taskKey.trim(), name: template.name.trim(),
      dueOffsetDays: Number(template.dueOffsetDays) || 0, ownerRole: template.ownerRole.trim() || "R-HR",
      required: template.required,
    });
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", template.taskKey);
    setTemplate({ ...template, taskKey: "", name: "" });
  }

  async function submitWaive() {
    if (!token || !waiving || !waiveReason.trim()) return;
    setBusy(true);
    const res = await waiveOnboardingTask(token, waiving.task.id, waiveReason.trim());
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", waiving.task.taskKey);
    setWaiving(null); setWaiveReason("");
    void load(loadedFor);
  }

  async function complete(task: OnboardingTaskWithOverdue) {
    if (!token) return;
    setBusy(true);
    const res = await completeOnboardingTask(token, task.task.id);
    setBusy(false);
    if (!res.ok) { setFormError(peopleActionError(t, res.code, res.message)); return; }
    push("success", task.task.taskKey);
    void load(loadedFor);
  }

  const columns: Column<OnboardingTaskWithOverdue>[] = useMemo(() => [
    { key: "task", label: tc.columns.task, render: (x) => x.task.name + " (" + x.task.taskKey + ")" },
    { key: "due", label: tc.columns.due, mono: true, render: (x) => fmtDay(x.task.dueDate, locale) },
    { key: "owner", label: tc.columns.owner, mono: true, render: (x) => x.task.ownerRole },
    { key: "required", label: tc.columns.required, render: (x) => (x.task.required ? "✓" : "—") },
    { key: "status", label: tc.columns.status, render: (x) => (
        <span className="inline-flex" style={{ gap: "0.25rem" }}>
          <Badge tone={peopleTone(x.task.status)}>{peopleStatusLabel(t, x.task.status)}</Badge>
          {x.overdue ? <Badge tone="danger">{tc.overdueBadge}</Badge> : null}
        </span>
      ) },
    { key: "actions", label: "", render: (x) => canWrite === true && x.task.status === "Pending" ? (
        <span className="inline-flex" style={{ gap: "0.25rem" }}>
          <Button variant="secondary" onClick={() => void complete(x)}>{tc.complete}</Button>
          <Button variant="secondary" onClick={() => setWaiving(x)}>{tc.waive}</Button>
        </span>
      ) : null },
  ], [tc, t, locale, canWrite]);

  if (!ready || phase === "loading") return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load(loadedFor)} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={(id) => setToasts((p) => p.filter((x) => x.id !== id))} closeLabel={t.common.cancel} />
      {formError ? <p className="banner" role="alert">{formError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.generateTitle}</h2>
        <form onSubmit={(e) => void generate(e)} className="inline-flex" style={{ gap: "0.5rem", alignItems: "end" }}>
          <Input id="ob-worker" label={tc.workerId} value={workerId} onChange={(e) => setWorkerId(e.target.value)} />
          <Button type="submit" loading={busy} disabled={!meReady}>{tc.generate}</Button>
        </form>
        {canWrite === true ? (
          <form onSubmit={(e) => void submitTemplate(e)} style={{ marginTop: "1rem" }}>
            <h3 className="t-title">{tc.templateFormTitle}</h3>
            <div className="form-grid">
              <Input id="ob-cat" label={tc.categoryCode} value={template.categoryCode} onChange={(e) => setTemplate({ ...template, categoryCode: e.target.value })} />
              <Input id="ob-key" label={tc.taskKey} value={template.taskKey} onChange={(e) => setTemplate({ ...template, taskKey: e.target.value })} />
              <Input id="ob-name" label={tc.taskName} value={template.name} onChange={(e) => setTemplate({ ...template, name: e.target.value })} />
              <Input id="ob-offset" label={tc.dueOffsetDays} type="number" value={template.dueOffsetDays} onChange={(e) => setTemplate({ ...template, dueOffsetDays: e.target.value })} />
              <Input id="ob-role" label={tc.ownerRole} value={template.ownerRole} onChange={(e) => setTemplate({ ...template, ownerRole: e.target.value })} />
            </div>
            <p><Button type="submit" loading={busy} disabled={!meReady}>{tc.createTemplate}</Button></p>
          </form>
        ) : null}
      </section>

      <section className="card">
        {tasks === null || tasks.length === 0 ? <EmptyState title={tc.empty} /> : (
          <Table columns={columns} rows={tasks} caption={tc.title} />
        )}
      </section>

      <Modal open={waiving !== null} onClose={() => setWaiving(null)} title={tc.waive + " — " + (waiving?.task.taskKey ?? "")} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-caption">{tc.waiveHint}</p>
        <Input id="ob-waive-reason" label={tc.waiveReason} value={waiveReason} onChange={(e) => setWaiveReason(e.target.value)} />
        <p><Button onClick={() => void submitWaive()} loading={busy}>{tc.waive}</Button></p>
      </Modal>
    </div>
  );
}
