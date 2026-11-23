import type { ReactNode } from "react";

// Minimal pass-through root: [locale]/layout owns <html>/<body> so lang/dir are
// SSR-correct per locale (official App Router i18n root-layout pattern).
export default function RootLayout({ children }: { children: ReactNode }) {
  return children;
}
