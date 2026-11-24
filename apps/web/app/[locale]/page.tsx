"use client";

import { useEffect } from "react";
import { useParams, useRouter } from "next/navigation";

// Locale root (/en, /bn): the app's entry is login — it hands off to select-entity and then
// the dashboard. Without this page the bare locale path 404s.
export default function LocaleRootPage() {
  const params = useParams<{ locale: string }>();
  const router = useRouter();

  useEffect(() => {
    router.replace(`/${params.locale}/login`);
  }, [params.locale, router]);

  return null;
}
