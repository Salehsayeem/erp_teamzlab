"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { ApiResult } from "@/lib/services/api";

type Phase = "loading" | "error" | "success";

/**
 * Reusable load/refresh cycle for a list endpoint: phase + rows + retry.
 * The fetcher may change identity between renders (kept in a ref);
 * the effect only re-runs when the token changes.
 */
export function useListResource<T>(token: string | null, fetcher: (token: string) => Promise<ApiResult<T[]>>) {
  const [data, setData] = useState<T[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    setErrorCode(null);
    const res = await fetcherRef.current(token);
    if (res.ok) {
      setData(res.data);
      setPhase("success");
    } else {
      setData(null);
      setErrorCode(res.code);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  return { data, phase, errorCode, load, setData };
}
