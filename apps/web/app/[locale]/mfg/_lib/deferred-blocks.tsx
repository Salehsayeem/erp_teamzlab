"use client";

import { useCallback, useEffect, useState } from "react";
import {
  fetchScheduleRuns, fetchSchedulePlacements,
  fetchLaborTime, fetchDowntimeEvents, fetchWipProjection,
  fetchCustodies, fetchCloseReconciliation,
  type PlacementRow, type LaborRow, type DowntimeRow,
  type WipProjectionRow, type CustodyRow, type CloseReconciliationRow,
} from "@/lib/services/mfg-deferred";
import type { Dict } from "@/lib/i18n";
import { fmtNumber } from "@/lib/format";
import { Badge, EmptyState, Table, type Column } from "@/components/uikit";
import type { Locale } from "@/lib/i18n";

// Production order detail — MF deferred blocks (routing/schedule, WIP, labor, downtime,
// subcontract custody, close reconciliation). Reads only; the write flows live on their
// own screens (shop-floor, subcontract, genealogy).

export function DeferredOrderBlocks({ token, orderId, t, locale }: {
  token: string;
  orderId: string;
  t: Dict;
  locale: Locale;
}) {
  const gd = t.mfg.genealogy;
  const sf = t.mfg.shopfloor;
  const sub = t.mfg.subcontract;
  const sc = t.mfg.sched;
  const [placements, setPlacements] = useState<PlacementRow[] | null>(null);
  const [labor, setLabor] = useState<LaborRow[] | null>(null);
  const [downtime, setDowntime] = useState<DowntimeRow[] | null>(null);
  const [wip, setWip] = useState<WipProjectionRow | null>(null);
  const [custody, setCustody] = useState<{ rows: CustodyRow[]; reconciliation: { sent: number; returned: number; outstanding: number } } | null>(null);
  const [recon, setRecon] = useState<CloseReconciliationRow | null>(null);

  const load = useCallback(async () => {
    const [runs, l, d, w, c, r] = await Promise.all([
      fetchScheduleRuns(token, orderId), fetchLaborTime(token, orderId), fetchDowntimeEvents(token, orderId),
      fetchWipProjection(token, orderId), fetchCustodies(token, orderId), fetchCloseReconciliation(token, orderId),
    ]);
    const firstRun = runs.ok ? runs.data[0] : undefined;
    const runId = firstRun ? firstRun.id : null;
    if (runId) {
      const p = await fetchSchedulePlacements(token, runId);
      setPlacements(p.ok ? p.data : null);
    } else {
      setPlacements([]);
    }
    setLabor(l.ok ? l.data : []);
    setDowntime(d.ok ? d.data : []);
    setWip(w.ok ? w.data : null);
    setCustody(c.ok ? c.data : null);
    setRecon(r.ok ? r.data : null);
  }, [token, orderId]);
  useEffect(() => { void load(); }, [load]);

  const placementCols: Column<PlacementRow>[] = [
    { key: "seq", label: sc.colSeq, mono: true, render: (p) => p.seq },
    { key: "op", label: sc.colOp, mono: true, render: (p) => p.operationCode },
    { key: "wc", label: sc.colWc, mono: true, render: (p) => p.workCenterId },
    { key: "start", label: sc.colStart, mono: true, render: (p) => p.startAt ?? "—" },
    { key: "state", label: sc.colStatus, render: (p) => (p.scheduled ? <Badge tone="success">{sc.scheduled}</Badge> : <Badge tone="warning">{sc.unscheduled}</Badge>) },
  ];

  const laborCols: Column<LaborRow>[] = [
    { key: "worker", label: sf.colWorker, mono: true, render: (l) => l.workerEmploymentId },
    { key: "from", label: sf.colFrom, mono: true, render: (l) => l.startAt },
    { key: "to", label: sf.colTo, mono: true, render: (l) => l.endAt },
    { key: "min", label: sf.colMinutes, mono: true, render: (l) => fmtNumber(l.minutes, locale) },
    { key: "status", label: sf.colStatus, render: (l) => <Badge tone={l.status === "Approved" ? "success" : l.status === "Rejected" ? "danger" : "warning"}>{l.status}</Badge> },
  ];

  const downCols: Column<DowntimeRow>[] = [
    { key: "wc", label: sf.downtimeWc, mono: true, render: (d) => d.workCenterId },
    { key: "from", label: sf.colFrom, mono: true, render: (d) => d.startAt },
    { key: "to", label: sf.colTo, mono: true, render: (d) => d.endAt },
    { key: "reason", label: sf.colReason, mono: true, render: (d) => d.reasonCode },
  ];

  const custodyCols: Column<CustodyRow>[] = [
    { key: "step", label: sub.colStep, mono: true, render: (c) => c.routingOperationSeq },
    { key: "kind", label: sub.colKind, render: (c) => (c.kind === "Send" ? sub.kindSend : sub.kindReturn) },
    { key: "qty", label: sub.colQty, mono: true, render: (c) => fmtNumber(c.qty, locale) },
    { key: "movement", label: sub.colMovement, mono: true, render: (c) => c.inventoryMovementId },
  ];

  const varianceCols: Column<CloseReconciliationRow["variances"][number]>[] = [
    { key: "bucket", label: gd.colBucket, mono: true, render: (v) => v.bucketCode },
    { key: "qty", label: gd.colQty, mono: true, render: (v) => fmtNumber(v.qtyVariance, locale) },
    { key: "owner", label: gd.colOwner, mono: true, render: (v) => v.owner || "—" },
    { key: "age", label: gd.colAge, mono: true, render: (v) => v.ageDays },
    { key: "disp", label: gd.colDisposition, render: (v) => v.disposition },
  ];

  const bucketTiles = recon ? [
    { label: gd.planned, value: recon.buckets.planned },
    { label: gd.issued, value: recon.buckets.issued },
    { label: gd.returned, value: recon.buckets.returned },
    { label: gd.completed, value: recon.buckets.completed },
    { label: gd.scrapped, value: recon.buckets.scrapped },
    { label: gd.wipRemainder, value: recon.buckets.wipRemainder },
  ] : [];

  return (
    <div className="stack">
      <section className="card">
        <h2 className="t-title">{sc.placementsTitle}</h2>
        {placements === null ? <p className="t-caption">{sc.empty}</p> : placements.length === 0 ? <EmptyState title={sc.empty} /> : (
          <Table columns={placementCols} rows={placements} caption={sc.placementsTitle} />
        )}
      </section>
      <section className="card">
        <h2 className="t-title">{sf.wipTitle}</h2>
        {wip === null ? <p className="t-caption">{sf.empty}</p> : (
          <div className="tiles">
            {[
              { label: sf.wipDebits, value: wip.wipDebits },
              { label: sf.wipCredits, value: wip.wipCredits },
              { label: sf.wipRemainder, value: wip.wipRemainder },
            ].map((tile) => (
              <div className="kpi" key={tile.label}>
                <p className="kpi__label">{tile.label}</p>
                <p className="kpi__value">{fmtNumber(tile.value, locale)}</p>
              </div>
            ))}
          </div>
        )}
      </section>
      <section className="card">
        <h2 className="t-title">{sf.laborTitle}</h2>
        {labor === null || labor.length === 0 ? <p className="t-caption">{sf.empty}</p> : <Table columns={laborCols} rows={labor} caption={sf.laborTitle} />}
        <h3 className="t-body-strong">{sf.downtimeTitle}</h3>
        {downtime === null || downtime.length === 0 ? <p className="t-caption">{sf.empty}</p> : <Table columns={downCols} rows={downtime} caption={sf.downtimeTitle} />}
      </section>
      <section className="card">
        <h2 className="t-title">{sub.custodyTitle}</h2>
        {custody === null ? <p className="t-caption">{sub.empty}</p> : (
          <div className="stack">
            <p className="t-caption">
              {sub.sent}: <span className="mono">{fmtNumber(custody.reconciliation.sent, locale)}</span> · {sub.returned}: <span className="mono">{fmtNumber(custody.reconciliation.returned, locale)}</span> · {sub.outstanding}: <span className="mono">{fmtNumber(custody.reconciliation.outstanding, locale)}</span>
            </p>
            {custody.rows.length === 0 ? <EmptyState title={sub.empty} /> : <Table columns={custodyCols} rows={custody.rows} caption={sub.custodyTitle} />}
          </div>
        )}
      </section>
      <section className="card">
        <h2 className="t-title">{gd.reconTitle}</h2>
        {recon === null ? <p className="t-caption">{gd.noVariances}</p> : (
          <div className="stack">
            <section className="tiles">
              {bucketTiles.map((tile) => (
                <div className="kpi" key={tile.label}>
                  <p className="kpi__label">{tile.label}</p>
                  <p className="kpi__value">{fmtNumber(tile.value, locale)}</p>
                </div>
              ))}
            </section>
            {recon.variances.length === 0 ? <p className="t-caption">{gd.noVariances}</p> : (
              <Table columns={varianceCols} rows={recon.variances} caption={gd.variancesTitle} />
            )}
          </div>
        )}
      </section>
    </div>
  );
}
