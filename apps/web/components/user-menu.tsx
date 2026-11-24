"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { getDictionary, isLocale } from "@/lib/i18n";
import { fetchMe } from "@/lib/services/api";
import { TOKEN_KEY } from "@/hooks/useAuth";

// Topbar user chip + dropdown: signed-in identity (from GET /me), entity line,
// and Log out (clears the token, back to login). Fails visible: if /me cannot
// be fetched the chip still offers Log out (token-based).
export default function UserMenu({ locale }: { locale: string }) {
  const router = useRouter();
  const dict = getDictionary(isLocale(locale) ? locale : "en");
  const [open, setOpen] = useState(false);
  const [me, setMe] = useState<{ name: string; email: string; role: string } | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const token = localStorage.getItem(TOKEN_KEY);
    if (!token) return;
    let cancelled = false;
    void fetchMe(token).then((res) => {
      if (cancelled) return;
      if (res.ok) {
        const d = res.data as { user?: { name?: string; email?: string }; role?: string };
        setMe({
          name: d.user?.name ?? "",
          email: d.user?.email ?? "",
          role: d.role ?? "",
        });
      }
    });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    function onDocClick(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    }
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("click", onDocClick);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("click", onDocClick);
      document.removeEventListener("keydown", onKey);
    };
  }, []);

  function logout() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem("erp_entity");
    setOpen(false);
    void router.replace(`/${locale}/login`);
  }

  const initials = (me?.name ?? "").trim().slice(0, 2).toUpperCase() || "?";

  return (
    <div ref={rootRef} className="usermenu">
      <button
        type="button"
        className="usermenu__chip"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <span className="usermenu__avatar" aria-hidden="true">
          {initials}
        </span>
      </button>
      {open ? (
        <div className="usermenu__panel" role="menu">
          <div className="usermenu__head">
            <p className="usermenu__name">{me?.name ?? "…"}</p>
            <p className="usermenu__email">{me?.email ?? ""}</p>
          </div>
          <button type="button" role="menuitem" className="usermenu__item" onClick={logout}>
            {dict.userMenu.logout}
          </button>
        </div>
      ) : null}
    </div>
  );
}
