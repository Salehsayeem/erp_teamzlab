"use client";

import { useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { exchangeToken } from "@/lib/services/api";
import { Button, ErrorState } from "@/components/uikit";
import { TOKEN_KEY } from "@/hooks/useAuth";

const SEEDED_USERS = [
  { email: "admin@teamzlab.dev", key: "admin" },
  { email: "procurve@teamzlab.dev", key: "procurement" },
  { email: "finance@teamzlab.dev", key: "finance" },
  { email: "sales@teamzlab.dev", key: "sales" },
  { email: "assets@teamzlab.dev", key: "assets" },
  { email: "hr@teamzlab.dev", key: "hr" },
  { email: "mfg@teamzlab.dev", key: "mfg" },
] as const;

export default function LoginPage() {
  const params = useParams<{ locale: string }>();
  const router = useRouter();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const [busyEmail, setBusyEmail] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function signIn(email: string) {
    setBusyEmail(email);
    setError(null);
    const res = await exchangeToken(email);
    if (!res.ok) {
      setError(res.code === "NETWORK" ? t.common.networkError : `${t.login.failed} ${res.message}`);
      setBusyEmail(null);
      return;
    }
    localStorage.setItem(TOKEN_KEY, res.data.token);
    router.replace(`/${locale}/select-entity`);
  }

  return (
    <div className="auth">
      <h1 className="t-display">{t.login.title}</h1>
      <p className="t-caption">{t.login.subtitle}</p>
      {error ? <ErrorState message={error} retryLabel={t.common.retry} onRetry={() => setError(null)} /> : null}
      <div className="auth__card">
        <div className="auth__rows">
          {SEEDED_USERS.map((u) => (
            <div key={u.email} className="auth__row">
              <div>
                <p className="auth__role">{t.login.roles[u.key]}</p>
                <p className="auth__email">{u.email}</p>
              </div>
              <Button loading={busyEmail === u.email} disabled={busyEmail !== null && busyEmail !== u.email} onClick={() => void signIn(u.email)}>
                {t.login.signIn}
              </Button>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
