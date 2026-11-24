"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  answerQa,
  fetchSupplierRows,
  inviteSuppliers,
  procError,
  type EventInvitation,
  type EventQa,
  type QaScope,
  type SupplierRow,
} from "@/lib/services/procurement";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Select, Table, Textarea } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { INVITATION_STATUS_TONES, statusLabel, type WorkspaceCtx } from "../../../_lib/ui";

export function InvitationsTab({ ctx, active }: { ctx: WorkspaceCtx; active: boolean }) {
  const { token, t, locale, event, detail, applyDetail } = ctx;
  const inv = t.procurement.sourcing.workspace.invitations;
  const [suppliers, setSuppliers] = useState<SupplierRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [selected, setSelected] = useState<string[]>([]);
  const [inviteError, setInviteError] = useState<string | null>(null);
  const [inviting, setInviting] = useState(false);

  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState("");
  const [scope, setScope] = useState<QaScope>("EventWide");
  const [supplierId, setSupplierId] = useState("");
  const [publish, setPublish] = useState(true);
  const [qaErrors, setQaErrors] = useState<{ question?: string; answer?: string; supplierId?: string }>({});
  const [qaError, setQaError] = useState<string | null>(null);
  const [posting, setPosting] = useState(false);

  const eligible = (suppliers ?? []).filter((s) => s.status !== "Suspended" && s.status !== "Blocked");

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchSupplierRows(token);
    if (res.ok) {
      setSuppliers(res.data);
      setPhase("success");
    } else setPhase("error");
  }, [token]);
  useEffect(() => { if (active) void load(); }, [active, load]);

  const errMap: Record<string, string> = {
    SUPPLIER_INELIGIBLE: inv.errors.inviteRequired,
    SUPPLIER_NOT_FOUND: t.procurement.common.forbidden,
    INVITATION_EXISTS: inv.empty,
    EVENT_STATE_ERROR: t.procurement.common.forbidden,
    FORBIDDEN: t.procurement.common.forbidden,
  };

  function toggle(id: string) {
    setSelected((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  async function submitInvite() {
    if (selected.length === 0) {
      setInviteError(inv.errors.inviteRequired);
      return;
    }
    setInviting(true);
    setInviteError(null);
    const res = await inviteSuppliers(token, event.id, selected);
    setInviting(false);
    if (!res.ok) {
      setInviteError(procError(res.code, res.message, errMap, inv.inviteFailed));
      return;
    }
    applyDetail({ invitations: [...detail.invitations, ...res.data] });
    setSelected([]);
  }

  async function submitQa(e: FormEvent) {
    e.preventDefault();
    const errs: typeof qaErrors = {};
    if (!question.trim()) errs.question = inv.errors.questionRequired;
    if (!answer.trim()) errs.answer = inv.errors.answerRequired;
    if (scope === "SupplierSpecific" && !supplierId) errs.supplierId = inv.errors.supplierRequired;
    setQaErrors(errs);
    setQaError(null);
    if (Object.keys(errs).length > 0) return;
    setPosting(true);
    const res = await answerQa(token, event.id, {
      question: question.trim(),
      answer: answer.trim(),
      scope,
      supplierId: scope === "SupplierSpecific" ? supplierId : null,
      publish,
    });
    setPosting(false);
    if (!res.ok) {
      setQaError(procError(res.code, res.message, { FORBIDDEN: t.procurement.common.forbidden }, inv.qaFailed));
      return;
    }
    applyDetail({ qa: [res.data, ...detail.qa] });
    setQuestion("");
    setAnswer("");
  }

  const invitationColumns: Column<EventInvitation>[] = [
    { key: "supplier", label: inv.invitationColumns.supplier, mono: true, render: (i) => i.supplierId },
    { key: "status", label: inv.invitationColumns.status, render: (i) => <Badge tone={INVITATION_STATUS_TONES[i.status] ?? "muted"}>{statusLabel(t, i.status)}</Badge> },
    {
      key: "warningGates",
      label: inv.invitationColumns.warningGates,
      render: (i) =>
        i.warningGates
          ? i.warningGates.split(",").map((g) => <Badge key={g} tone="warning">{g.trim()}</Badge>)
          : "—",
    },
  ];
  const qaColumns: Column<EventQa>[] = [
    { key: "question", label: inv.qaColumns.question, render: (q) => q.question },
    { key: "answer", label: inv.qaColumns.answer, render: (q) => q.answer },
    { key: "scope", label: inv.qaColumns.scope, render: (q) => (q.scope === "EventWide" ? inv.scopeEventWide : inv.scopeSupplierSpecific) },
    { key: "supplier", label: inv.qaColumns.supplier, mono: true, render: (q) => q.supplierId ?? "—" },
    { key: "published", label: inv.qaColumns.published, render: (q) => (q.published ? <Badge tone="success">{statusLabel(t, "Published")}</Badge> : <Badge tone="muted">{statusLabel(t, "Draft")}</Badge>) },
  ];

  if (phase === "loading") return <LoadingSkeleton rows={4} />;
  if (phase === "error") return <ErrorState message={inv.inviteFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      {inviteError ? <p className="banner" role="alert">{inviteError}</p> : null}
      {qaError ? <p className="banner" role="alert">{qaError}</p> : null}

      <section className="card">
        <h3 className="t-title">{inv.title}</h3>
        {detail.invitations.length === 0 ? (
          <EmptyState title={inv.empty} />
        ) : (
          <Table<EventInvitation> columns={invitationColumns} rows={detail.invitations} caption={inv.title} />
        )}
        <h4 className="t-body-strong">{inv.inviteTitle}</h4>
        <p className="t-caption">{inv.eligibleOnly}</p>
        {eligible.length === 0 ? (
          <EmptyState title={inv.noSuppliers} />
        ) : (
          <>
            <div className="stack" style={{ marginBlockStart: 0 }}>
              {eligible.map((s) => (
                <label key={s.id} className="field" style={{ flexDirection: "row", alignItems: "center", gap: 10 }}>
                  <input
                    type="checkbox"
                    checked={selected.includes(s.id)}
                    onChange={() => toggle(s.id)}
                    aria-label={`${s.name} (${s.id})`}
                  />
                  <span>{s.name} <span className="mono t-caption">{s.taxId}</span></span>
                  <Badge tone={s.status === "Active" ? "success" : s.status === "Suspended" || s.status === "Blocked" ? "danger" : "warning"}>
                    {statusLabel(t, s.status)}
                  </Badge>
                </label>
              ))}
            </div>
            <p>
              <Button onClick={() => void submitInvite()} loading={inviting}>
                {inv.invite} · {inv.selected} {selected.length}
              </Button>
            </p>
          </>
        )}
      </section>

      <section className="card">
        <h3 className="t-title">{inv.qaTitle}</h3>
        {detail.qa.length === 0 ? <EmptyState title={inv.qaEmpty} /> : <Table<EventQa> columns={qaColumns} rows={detail.qa} caption={inv.qaTitle} />}
        <form onSubmit={(e) => void submitQa(e)}>
          <h4 className="t-body-strong">{inv.qaTitle}</h4>
          <div className="form-grid">
            <Input id="qa-question" label={inv.question} value={question} onChange={(e) => setQuestion(e.target.value)} error={qaErrors.question} />
            <Textarea id="qa-answer" label={inv.answer} value={answer} onChange={(e) => setAnswer(e.target.value)} error={qaErrors.answer} />
            <Select
              id="qa-scope"
              label={inv.scope}
              options={(["EventWide", "SupplierSpecific"] as QaScope[]).map((v) => ({ value: v, label: v === "EventWide" ? inv.scopeEventWide : inv.scopeSupplierSpecific }))}
              value={scope}
              onChange={(e) => setScope(e.target.value as QaScope)}
            />
            {scope === "SupplierSpecific" ? (
              <Select
                id="qa-supplier"
                label={inv.supplier}
                placeholder={inv.supplier}
                options={eligible.map((s) => ({ value: s.id, label: `${s.name} (${s.taxId})` }))}
                value={supplierId}
                onChange={(e) => setSupplierId(e.target.value)}
                error={qaErrors.supplierId}
              />
            ) : null}
          </div>
          <label className="field" style={{ flexDirection: "row", alignItems: "center", gap: 10, marginBlockStart: 12 }}>
            <input type="checkbox" checked={publish} onChange={(e) => setPublish(e.target.checked)} aria-label={inv.publishAnswer} />
            <span>{inv.publishAnswer}</span>
          </label>
          {publish && scope === "EventWide" ? <p className="t-caption">{inv.versionBump}</p> : null}
          <p><Button type="submit" loading={posting}>{inv.qaTitle}</Button></p>
        </form>
      </section>
    </div>
  );
}
