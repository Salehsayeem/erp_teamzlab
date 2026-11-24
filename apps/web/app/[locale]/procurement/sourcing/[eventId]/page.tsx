"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchMe } from "@/lib/services/api";
import { fetchEventDetail, fetchSourcingEvents, type EventDetail, type SourcingEvent } from "@/lib/services/procurement";
import { fmtDate } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, LoadingSkeleton, Tabs } from "@/components/uikit";
import { EVENT_STATUS_TONES, hasPerm, statusLabel, type WorkspaceCtx } from "../../_lib/ui";
import { SetupTab } from "./_tabs/setup";
import { InvitationsTab } from "./_tabs/invitations";
import { BidsTab } from "./_tabs/bids";
import { EvaluationTab } from "./_tabs/evaluation";
import { AwardsTab } from "./_tabs/awards";

const EMPTY_DETAIL: EventDetail = { event: null as unknown as SourcingEvent, lots: [], criteria: [], invitations: [], qa: [], assignments: [] };

export default function EventWorkspacePage() {
  const params = useParams<{ locale: string; eventId: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const eventId = params.eventId;
  const t = getDictionary(locale);
  const w = t.procurement.sourcing.workspace;
  const { token, ready } = useAuth(locale);

  const [event, setEvent] = useState<SourcingEvent | null>(null);
  const [detail, setDetail] = useState<EventDetail>(EMPTY_DETAIL);
  const [me, setMe] = useState<{ id: string; perms: string[] } | null>(null);
  const [phase, setPhase] = useState<"loading" | "notFound" | "error" | "success">("loading");
  const [activeTab, setActiveTab] = useState("");

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchMe(token).then((res) => {
      if (!cancelled && res.ok) setMe({ id: res.data.user.id, perms: res.data.permissions });
    });
    return () => { cancelled = true; };
  }, [token]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    // Depth comes from GET /sourcing-events/{id} once it lands; until then the list
    // endpoint still yields the event row, and child sections degrade to in-session.
    const detailRes = await fetchEventDetail(token, eventId);
    if (detailRes.ok) {
      setEvent(detailRes.data.event);
      setDetail(detailRes.data);
      setPhase("success");
      return;
    }
    const listRes = await fetchSourcingEvents(token);
    const row = listRes.ok ? listRes.data.find((e) => e.id === eventId) : undefined;
    if (row) {
      setEvent(row);
      setDetail({ ...EMPTY_DETAIL, event: row });
      setPhase("success");
    } else {
      setEvent(null);
      setPhase(listRes.ok ? "notFound" : "error");
    }
  }, [token, eventId]);
  useEffect(() => { void load(); }, [load]);

  const applyDetail = useCallback((patch: Partial<EventDetail>) => {
    setDetail((prev) => ({ ...prev, ...patch }));
  }, []);
  const onEvent = useCallback((next: SourcingEvent) => setEvent(next), []);

  if (!ready || token === null || (phase === "loading" && event === null)) return <LoadingSkeleton rows={6} />;

  if (phase === "error" || event === null) {
    return (
      <div className="stack">
        <div className="page-header">
          <h1 className="t-display">{w.loadFailed}</h1>
          <p className="t-caption"><Link href={`/${locale}/procurement/sourcing`}>{w.backToList}</Link></p>
        </div>
        <div className="state state--error" role="alert">
          <p className="state__title">{phase === "notFound" ? w.notFound : w.loadFailed}</p>
          <Button variant="secondary" onClick={() => void load()}>{t.common.retry}</Button>
        </div>
      </div>
    );
  }

  const canWrite = me ? hasPerm(me.perms, "sourcing:write") : false;
  const canApprove = me ? hasPerm(me.perms, "sourcing:approve") : false;
  const canEvaluate = me ? hasPerm(me.perms, "sourcing:evaluate") : false;
  const isRfi = event.eventType === "Rfi";
  const meta = w.meta;
  const statusLabelFor = (v: string) => statusLabel(t, v);

  // Permission-aware tabs; the buyer view (R-PROC/R-FIN) and the evaluator view (R-EVAL)
  // share the route — evaluators get Evaluation only.
  const tabs = [];
  if (canWrite && event.status === "Draft") tabs.push({ id: "setup", label: w.tabs.setup });
  if (canWrite && (event.status === "Draft" || event.status === "Published")) tabs.push({ id: "invitations", label: w.tabs.invitations });
  if (canWrite || canApprove) tabs.push({ id: "bids", label: w.tabs.bids });
  if (canWrite || canApprove || canEvaluate) tabs.push({ id: "evaluation", label: w.tabs.evaluation });
  if ((canWrite || canApprove) && !isRfi) tabs.push({ id: "awards", label: w.tabs.awards });
  const visible = tabs.some((tab) => tab.id === activeTab) ? activeTab : tabs[0]?.id ?? "";

  const ctx: WorkspaceCtx = { token, locale, t, event, meId: me?.id ?? null, perms: me?.perms ?? [], detail, applyDetail, onEvent };

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{event.title}</h1>
        <p className="t-caption">
          <Link href={`/${locale}/procurement/sourcing`}>{w.backToList}</Link>
        </p>
      </div>

      <section className="card">
        <h2 className="t-title">
          {event.id} <Badge tone={EVENT_STATUS_TONES[event.status] ?? "muted"}>{statusLabelFor(event.status)}</Badge>{" "}
          <Badge tone="muted">{statusLabelFor(event.eventType)}</Badge>
        </h2>
        <div className="form-grid">
          <p className="t-caption">{meta.opens}: {fmtDate(event.opensAt, locale)} · {event.timeZoneName}</p>
          <p className="t-caption">{meta.closes}: {fmtDate(event.closesAt, locale)}</p>
          <p className="t-caption">{meta.currency}: <span className="mono">{event.currency}</span></p>
          <p className="t-caption">{meta.version}: <span className="mono">v{event.version}</span></p>
          <p className="t-caption">{meta.createdBy}: <span className="mono">{event.createdBy}</span></p>
        </div>
        {isRfi ? <p className="banner" role="status">{w.noAwardIntent}</p> : null}
      </section>

      {tabs.length === 0 ? (
        <EmptyState title={t.procurement.common.forbidden} />
      ) : (
        <Tabs
          tabs={tabs.map((tab) => ({
            id: tab.id,
            label: tab.label,
            // Tabs render every panel (hidden) on mount — `active` lets each tab
            // refetch when it becomes the visible one instead of only at page load.
            content:
              tab.id === "setup" ? <SetupTab ctx={ctx} /> :
              tab.id === "invitations" ? <InvitationsTab ctx={ctx} active={visible === "invitations"} /> :
              tab.id === "bids" ? <BidsTab ctx={ctx} active={visible === "bids"} /> :
              tab.id === "evaluation" ? <EvaluationTab ctx={ctx} active={visible === "evaluation"} /> :
              <AwardsTab ctx={ctx} active={visible === "awards"} />,
          }))}
          activeId={visible}
          onChange={setActiveTab}
          label={event.title}
        />
      )}
    </div>
  );
}
