"use client";

import { useEffect, useState } from "react";

// Dark is the default theme (html.dark class, reference-theme convention); "light" is an
// explicit toggle persisted to localStorage("theme"). The initial application happens in
// the inline script in the locale layout (no FOUC).
export default function ThemeToggle({ lightLabel, darkLabel }: { lightLabel: string; darkLabel: string }) {
  const [light, setLight] = useState(false);

  useEffect(() => {
    setLight(!document.documentElement.classList.contains("dark"));
  }, []);

  function toggle() {
    const next = !light;
    setLight(next);
    document.documentElement.classList.toggle("dark", !next);
    try { localStorage.setItem("theme", next ? "light" : "dark"); } catch { /* private mode */ }
  }

  return (
    <button type="button" className="btn btn--secondary btn--sm" onClick={toggle} aria-label={light ? darkLabel : lightLabel}>
      {light ? "◐" : "◑"}
    </button>
  );
}
