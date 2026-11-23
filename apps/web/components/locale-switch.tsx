"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { Locale } from "@/lib/i18n";

/** Swaps the leading /{locale} segment, keeping the rest of the path. */
export function LocaleSwitch({ locale, otherLabel }: { locale: Locale; otherLabel: string }) {
  const pathname = usePathname() ?? `/${locale}`;
  const segments = pathname.split("/");
  const target = segments.length > 1 ? [...segments.slice(0, 1), locale === "en" ? "bn" : "en", ...segments.slice(2)] : [`/${locale === "en" ? "bn" : "en"}`];
  const href = target.join("/") || "/";
  return (
    <Link className="locale-switch" href={href} hrefLang={locale === "en" ? "bn" : "en"}>
      {otherLabel}
    </Link>
  );
}
