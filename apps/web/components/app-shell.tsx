"use client";

import { useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { setApiLocale } from "@/lib/services/api";
import { LocaleSwitch } from "@/components/locale-switch";
import ThemeToggle from "@/components/theme-toggle";
import UserMenu from "@/components/user-menu";
import AppSidebar from "@/components/app-sidebar";

// The app chrome: sidebar (desktop static / mobile slide-in drawer via the
// topbar hamburger) + topbar (brand, hamburger, locale, theme, user menu) +
// content. Client component: owns the mobile drawer state.
export default function AppShell({ locale, children }: { locale: string; children: ReactNode }) {
  const loc: Locale = isLocale(locale) ? locale : "en";
  // During this render body — children render (and fetch) after it, so API error
  // text is localized before any request can resolve. Idempotent, no re-render.
  setApiLocale(loc);
  const dict = getDictionary(loc);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const pathname = usePathname();
  const isAuthPage =
    pathname === `/${locale}/login` || pathname === `/${locale}/select-entity`;

  return (
    <div className="app-shell">
      <AppSidebar locale={locale} mobileOpen={drawerOpen} onClose={() => setDrawerOpen(false)} />
      {drawerOpen ? (
        <div className="drawer-scrim" onClick={() => setDrawerOpen(false)} aria-hidden="true" />
      ) : null}
      <div className="shell__main">
        <header className="topbar">
          <div className="topbar__start">
            {isAuthPage ? null : (
              <button
                type="button"
                className="topbar__menu"
                aria-label={dict.userMenu.openMenu}
                aria-expanded={drawerOpen}
                onClick={() => setDrawerOpen((v) => !v)}
              >
                ☰
              </button>
            )}
            <Link href={`/${locale}`} className="topbar__title">
              {dict.meta.appTitle}
            </Link>
          </div>
          <div className="topbar__actions">
            <LocaleSwitch locale={loc} otherLabel={dict.meta.otherLocale} />
            <ThemeToggle lightLabel="Switch to light theme" darkLabel="Switch to dark theme" />
            {isAuthPage ? null : <UserMenu locale={locale} />}
          </div>
        </header>
        <main className="content">{children}</main>
      </div>
    </div>
  );
}
