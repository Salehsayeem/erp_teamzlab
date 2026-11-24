import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Dev-only: allow the LAN host the app is browsed from (Next 16 blocks cross-origin
  // access to dev resources like /_next/hmr by default).
  allowedDevOrigins: ["172.30.243.205"],
};

export default nextConfig;
