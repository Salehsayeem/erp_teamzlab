"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchMe, type Me } from "@/lib/services/api";
import { useAuth } from "@/hooks/useAuth";
import { Button, EmptyState, ErrorState, LoadingSkeleton } from "@/components/uikit";

export default function SelectEntityPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [me, setMe] = useState<Me | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchMe(token);
    if (res.ok) {
      setMe(res.data);
      setPhase("success");
    } else {
      setMe(null);
      setPhase("error");
    }
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  if (!ready || phase === "loading") return <LoadingSkeleton rows={3} />;
  if (phase === "error") return <ErrorState message={t.common.networkError} retryLabel={t.common.retry} onRetry={() => void load()} />;
  if (!me) return <EmptyState title={t.common.loading} />;
  const entity = me.legalEntity;
  return (
    <div className="stack">
      <h1 className="t-display">{t.selectEntity.title}</h1>
      <p className="t-caption">{t.selectEntity.subtitle}</p>
      <div className="card">
        <h2 className="t-title">{entity.name}</h2>
        <p className="t-caption mono">{entity.id}</p>
        <div className="stack">
          <p><span className="t-body-strong">{t.selectEntity.tenant}: </span>{me.tenant.name} <span className="t-caption mono">({me.tenant.id})</span></p>
          <p><span className="t-body-strong">{t.selectEntity.jurisdiction}: </span>{entity.jurisdiction}</p>
          <p><span className="t-body-strong">{t.selectEntity.currency}: </span><span className="mono">{entity.currency}</span></p>
        </div>
        <Link href={`/${locale}/dashboard`}>
          <Button>{t.selectEntity.continue}</Button>
        </Link>
      </div>
    </div>
  );
}
