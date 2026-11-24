import { NextRequest, NextResponse } from "next/server";

// Locale routing lives on /[locale] (lib/i18n: en | bn). Bare / and any non-locale path
// redirect to the best locale: NEXT_LOCALE cookie first, then a bn Accept-Language match
// (bn is first-class per the spec), otherwise en. Unknown deep paths redirect too and
// 404 under /[locale] where the not-found page is locale-correct.
const LOCALES = ["en", "bn"];

function pickLocale(req: NextRequest): string {
  const cookie = req.cookies.get("NEXT_LOCALE")?.value;
  if (cookie && LOCALES.includes(cookie)) return cookie;
  if (/\bbn\b/i.test(req.headers.get("accept-language") ?? "")) return "bn";
  return "en";
}

export function middleware(req: NextRequest) {
  const { pathname } = req.nextUrl;
  const isLocalePath = LOCALES.some(
    (l) => pathname === `/${l}` || pathname.startsWith(`/${l}/`),
  );
  if (isLocalePath) return;

  const url = req.nextUrl.clone();
  url.pathname = `/${pickLocale(req)}${pathname === "/" ? "" : pathname}`;
  return NextResponse.redirect(url);
}

export const config = {
  // skip Next internals, API routes, and any file with an extension (static assets)
  matcher: ["/((?!_next|api/|.*\\..*).*)"],
};
