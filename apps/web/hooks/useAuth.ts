"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import type { Locale } from "@/lib/i18n";

export const TOKEN_KEY = "erp_token";

/** Client auth guard: reads the token from localStorage, redirects to login when missing. */
export function useAuth(locale: Locale): { token: string | null; ready: boolean } {
  const router = useRouter();
  const [token, setToken] = useState<string | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    const stored = localStorage.getItem(TOKEN_KEY);
    if (stored) setToken(stored);
    else void router.replace(`/${locale}/login`);
    setReady(stored !== null);
  }, [locale, router]);

  return { token, ready };
}
