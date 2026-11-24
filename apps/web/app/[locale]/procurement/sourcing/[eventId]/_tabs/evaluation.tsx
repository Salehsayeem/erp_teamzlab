"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  assignEvaluators,
  fetchScoreSummary,
  procError,
  submitScore,
  type ScoreSummary,
  type ScoreSummaryBid,
  type ScoreRow,
} from "@/lib/services/procurement";
import { fmtNumber } from "@/lib/format";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Select, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { BID_STATUS_TONES, hasPerm, statusLabel, type WorkspaceCtx } from "../../../_lib/ui";

type CellDraft = { raw: string; pass: "" | "true" | "false"; comment: string };
type Drafts = Record<string, Record<string, CellDraft>>;

const EMPTY_CELL: CellDraft = { raw: "", pass: "", comment: "" };

export function EvaluationTab({ ctx, active }: { ctx: WorkspaceCtx; active: boolean }) {
  const { token, t, locale, event, detail, applyDetail, meId, perms } = ctx;
  const ev = t.procurement.sourcing.workspace.evaluation;
  const canWrite = hasPerm(perms, "sourcing:write");
  const canEvaluate = hasPerm(perms, "sourcing:evaluate");

  const [summary, setSummary] = useState<ScoreSummary | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "forbidden" | "success">("loading");
  const [drafts, setDrafts] = useState<Drafts>({});
  const [savingBid, setSavingBid] = useState<string | null>(null);
  const [cellError, setCellError] = useState<string | null>(null);

  const [userIds, setUserIds] = useState("");
  const [assignError, setAssignError] = useState<string | null>(null);
  const [assigning, setAssigning] = useState(false);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchScoreSummary(token, event.id);
    if (!res.ok) {
      setSummary(null);
      setPhase(res.code === "FORBIDDEN" || res.code === "HTTP_403" ? "forbidden" : "error");
      return;
    }
    setSummary(res.data);
    setDrafts(seedDrafts(res.data, meId));
    setPhase("success");
  }, [token, event.id, meId]);
  useEffect(() => { if (active) void load(); }, [active, load]);

  async function submitAssign(e: FormEvent) {
    e.preventDefault();
    const ids = userIds.split(",").map((x) => x.trim()).filter((x) => x.length > 0);
    if (ids.length === 0) {
      setAssignError(ev.errors.userIdsRequired);
      return;
    }
    setAssigning(true);
    setAssignError(null);
    const res = await assignEvaluators(token, event.id, ids);
    setAssigning(false);
    if (!res.ok) {
      const mapped: Record<string, string> = {
        EVALUATOR_CREATOR_CONFLICT: ev.userIdsHint,
        USER_NOT_FOUND: ev.errors.userIdsRequired,
        FORBIDDEN: t.procurement.common.forbidden,
      };
      setAssignError(procError(res.code, res.message, mapped, ev.assignFailed));
      return;
    }
    applyDetail({ assignments: [...detail.assignments, ...res.data] });
    setUserIds("");
  }

  function setCell(bidId: string, criterionId: string, patch: Partial<CellDraft>) {
    setDrafts((prev) => ({
      ...prev,
      [bidId]: { ...(prev[bidId] ?? {}), [criterionId]: { ...(prev[bidId]?.[criterionId] ?? EMPTY_CELL), ...patch } },
    }));
  }

  async function saveBid(bid: ScoreSummaryBid) {
    const cells = drafts[bid.bidId] ?? {};
    // Inputs cover EVERY event criterion — an evaluator's first save creates their rows.
    const posts = (summary?.criteria ?? []).map((crit) => {
      const draft = cells[crit.id] ?? EMPTY_CELL;
      if (crit.kind === "Scored") {
        if (draft.raw.trim() === "") return null;
        const raw = Number(draft.raw);
        if (!Number.isFinite(raw) || raw < 0 || raw > crit.scaleMax) return "range" as const;
        return { bidId: bid.bidId, criterionId: crit.id, rawScore: raw, passFail: null, comment: draft.comment.trim() || null };
      }
      if (draft.pass === "") return null;
      return { bidId: bid.bidId, criterionId: crit.id, rawScore: null, passFail: draft.pass === "true", comment: draft.comment.trim() || null };
    });
    if (posts.some((p) => p === "range")) {
      setCellError(ev.errors.rawRange);
      return;
    }
    const payload = posts.filter((p): p is Exclude<typeof p, null | "range"> => p !== null && p !== "range");
    if (payload.length === 0) return;
    setSavingBid(bid.bidId);
    setCellError(null);
    let failed = false;
    for (const body of payload) {
      const res = await submitScore(token, event.id, body);
      if (!res.ok) {
        const mapped: Record<string, string> = {
          VALIDATION_ERROR: ev.errors.rawRange,
          FORBIDDEN: ev.forbidden,
        };
        setCellError(procError(res.code, res.message, mapped, ev.scoreFailed));
        failed = true;
        break;
      }
    }
    setSavingBid(null);
    if (!failed) void load();
  }

  const traceColumns: Column<ScoreRow>[] = [
    { key: "criterion", label: ev.traceColumns.criterion, mono: true, render: (r) => r.criterionCode },
    { key: "weight", label: ev.traceColumns.weight, mono: true, render: (r) => fmtNumber(r.weightPct, locale) },
    { key: "raw", label: ev.traceColumns.raw, mono: true, render: (r) => (r.kind === "Scored" ? (r.rawScore === null ? "—" : fmtNumber(r.rawScore, locale)) : "—") },
    { key: "passFail", label: ev.traceColumns.passFail, render: (r) => (r.kind === "PassFail" ? (r.passFail === null ? "—" : r.passFail ? <Badge tone="success">{ev.pass}</Badge> : <Badge tone="danger">{ev.fail}</Badge>) : "—") },
    { key: "normalized", label: ev.traceColumns.normalized, mono: true, render: (r) => (r.normalized === null ? "—" : fmtNumber(r.normalized, locale)) },
    { key: "contribution", label: ev.traceColumns.contribution, mono: true, render: (r) => fmtNumber(r.contribution, locale) },
    { key: "missing", label: ev.traceColumns.missing, render: (r) => (r.missing ? <Badge tone="warning">{ev.missing}</Badge> : "—") },
  ];

  if (phase === "loading") return <LoadingSkeleton rows={4} />;
  if (phase === "forbidden") {
    return (
      <div className="stack">
        <div className="state state--error" role="alert">
          <p className="state__title">{ev.forbidden}</p>
          <Button variant="secondary" onClick={() => void load()}>{t.common.retry}</Button>
        </div>
      </div>
    );
  }
  if (phase === "error") return <ErrorState message={ev.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const data = summary;
  if (data === null) return <LoadingSkeleton rows={4} />;
  const ownOnly = data.bids.some((x) => x.ownRowsOnly);

  return (
    <div className="stack">
      {cellError ? <p className="banner" role="alert">{cellError}</p> : null}
      {assignError ? <p className="banner" role="alert">{assignError}</p> : null}

      {canWrite ? (
        <section className="card">
          <h3 className="t-title">{ev.assignTitle}</h3>
          <form onSubmit={(e) => void submitAssign(e)}>
            <Input id="eval-userids" label={ev.userIds} hint={ev.userIdsHint} value={userIds} onChange={(e) => setUserIds(e.target.value)} error={assignError ?? undefined} />
            <p><Button type="submit" loading={assigning}>{ev.assign}</Button></p>
          </form>
          {detail.assignments.length === 0 ? (
            <EmptyState title={ev.assignedEmpty} />
          ) : (
            <ul>
              {detail.assignments.map((a) => <li key={a.id} className="mono">{a.userId}</li>)}
            </ul>
          )}
        </section>
      ) : null}

      <section className="card">
        <h3 className="t-title">{ev.gridTitle}</h3>
        <p className="t-caption">{ev.formulaVersion}: <span className="mono">{data.formulaVersion}</span></p>
        {ownOnly ? <p className="banner" role="status">{ev.ownScoresOnly}</p> : null}
        {!canEvaluate ? <p className="t-caption">{ev.readOnlyNote}</p> : null}
        {data.bids.length === 0 ? (
          <EmptyState title={ev.noBids} />
        ) : (
          <div className="stack">
            {data.bids.map((bid) => (
              <section className="card" key={bid.bidId}>
                <h4 className="t-body-strong">
                  <span className="mono">{bid.bidId}</span> · {bid.supplierId}{" "}
                  <Badge tone={BID_STATUS_TONES[bid.bidStatus] ?? "muted"}>{statusLabel(t, bid.bidStatus)}</Badge>
                  {bid.disqualified === true ? <Badge tone="danger">{ev.disqualified}</Badge> : null}
                </h4>
                {data.criteria.length === 0 ? (
                  <EmptyState title={ev.noBids} />
                ) : (
                  <>
                    <div className="stack">
                      {data.criteria.map((crit) => {
                        const row = bid.rows.find((r) => r.criterionId === crit.id);
                        const draft = drafts[bid.bidId]?.[crit.id] ?? EMPTY_CELL;
                        return (
                          <div className="form-grid" key={crit.id}>
                            {crit.kind === "Scored" ? (
                              <Input
                                id={`sc-${bid.bidId}-${crit.id}`}
                                label={`${crit.code} (${ev.traceColumns.weight} ${fmtNumber(crit.weightPct, locale)})`}
                                type="number"
                                min="0"
                                max={crit.scaleMax}
                                step="any"
                                disabled={!canEvaluate}
                                value={draft.raw}
                                onChange={(e) => setCell(bid.bidId, crit.id, { raw: e.target.value })}
                              />
                            ) : (
                              <Select
                                id={`sc-${bid.bidId}-${crit.id}`}
                                label={crit.code}
                                placeholder={ev.passFail}
                                options={[{ value: "true", label: ev.pass }, { value: "false", label: ev.fail }]}
                                disabled={!canEvaluate}
                                value={draft.pass}
                                onChange={(e) => setCell(bid.bidId, crit.id, { pass: e.target.value as CellDraft["pass"] })}
                              />
                            )}
                            <Input
                              id={`cm-${bid.bidId}-${crit.id}`}
                              label={ev.comment}
                              disabled={!canEvaluate}
                              value={draft.comment}
                              onChange={(e) => setCell(bid.bidId, crit.id, { comment: e.target.value })}
                            />
                          </div>
                        );
                      })}
                    </div>
                    {canEvaluate ? (
                      <p>
                        <Button small loading={savingBid === bid.bidId} onClick={() => void saveBid(bid)}>{ev.saveScores}</Button>
                      </p>
                    ) : null}
                    {bid.rows.length > 0 ? (
                      <>
                        <h4 className="t-body-strong">{ev.traceTitle}</h4>
                        <Table<ScoreRow> columns={traceColumns} rows={bid.rows} caption={ev.traceTitle} />
                      </>
                    ) : null}
                    {!bid.ownRowsOnly ? (
                      <p className="t-caption">
                        {ev.bidColumns.total}: <span className="mono">{bid.total === null ? "—" : fmtNumber(bid.total, locale)}</span> · {ev.bidColumns.rank}:{" "}
                        <span className="mono">{bid.rank ?? "—"}</span>
                      </p>
                    ) : null}
                  </>
                )}
              </section>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

function seedDrafts(summary: ScoreSummary, meId: string | null): Drafts {
  const drafts: Drafts = {};
  for (const bid of summary.bids) {
    const cells: Record<string, CellDraft> = {};
    for (const row of bid.rows) {
      const own = row.evaluatorUserId !== null && (row.evaluatorUserId === meId || bid.ownRowsOnly);
      cells[row.criterionId] = {
        raw: own && row.rawScore !== null ? String(row.rawScore) : "",
        pass: own && row.passFail !== null ? (row.passFail ? "true" : "false") : "",
        comment: own && row.comment ? row.comment : "",
      };
    }
    drafts[bid.bidId] = cells;
  }
  return drafts;
}
