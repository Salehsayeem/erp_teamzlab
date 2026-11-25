"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchReceipts, fetchAsns, intakeAsn, type ReceiptRow, type Asn } from "@/lib/services/procurement";
import { fmtDate } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";

export default function ReceiptsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [rows, setRows] = useState<ReceiptRow[] | null>(null);
  const [asns, setAsns] = useState<Asn[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [tab, setTab] = useState("receipts");
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [r, a] = await Promise.all([fetchReceipts(token), fetchAsns(token)]);
    if (r.ok && a.ok) { setRows(r.data); setAsns(a.data); setPhase("success"); }
    else { setRows(null); setPhase("error"); }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  const list = rows ?? [];
  const asnList = asns ?? [];
  const statusLabel = (s: string) => (t.common.status as Record<string, string>)[s] ?? s;

  if (!ready || (phase === "loading" && rows === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={t.procurement.receipts.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.procurement.receipts.title}</h1>
        <p className="t-caption">{t.procurement.receipts.subtitle}</p>
      </div>
      <section className="card">
        <h2 className="t-title">{t.procurement.receipts.listTab}</h2>
        {list.length === 0 ? <EmptyState title={t.procurement.receipts.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">ID</th>
                  <th scope="col">{t.procurement.receipts.po}</th>
                  <th scope="col">{t.procurement.receipts.itemSku}</th>
                  <th scope="col">{t.procurement.receipts.qty}</th>
                  <th scope="col">{t.procurement.receipts.receivedAt}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((r) => (
                  <tr key={r.id}>
                    <td className="mono">
                      <Link href={`/${locale}/procurement/receipts/${encodeURIComponent(r.id)}`}>{r.id}</Link>
                    </td>
                    <td className="mono">{r.purchaseOrderId}</td>
                    <td className="mono">{r.itemSku}</td>
                    <td className="mono">{r.qty}</td>
                    <td><span className="t-caption">{fmtDate(r.receivedAt, locale)}</span></td>
                    <td>
                      <Link className="mono" href={`/${locale}/procurement/receipts/${encodeURIComponent(r.id)}`}>{t.procurement.receipts.openDetail}</Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.receipts.asnTab}</h2>
        {asnList.length === 0 ? <EmptyState title={t.procurement.receipts.asnEmpty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">ID</th>
                  <th scope="col">{t.procurement.receipts.po}</th>
                  <th scope="col">{t.procurement.receipts.shipmentRef}</th>
                  <th scope="col">{t.procurement.receipts.matched}</th>
                  <th scope="col">{t.procurement.receipts.quarantined}</th>
                </tr>
              </thead>
              <tbody>
                {asnList.map((a) => (
                  <tr key={a.id}>
                    <td className="mono">{a.id}</td>
                    <td className="mono">{a.poId}</td>
                    <td>{a.shipmentRef}</td>
                    <td><Badge tone={a.status === "Matched" ? "success" : "warning"}>{a.status}</Badge></td>
                    <td className="mono">{a.lines.filter((l) => l.quarantined).length}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <p className="t-caption">{t.procurement.receipts.intakeNote}</p>
      </section>
    </div>
  );
}
