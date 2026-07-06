import type { NextConfig } from "next";

/**
 * The app is a pure CSR client against an independently-deployed .NET API: no Next server
 * ever talks to the backend on the user's behalf. Next is used strictly as an app shell,
 * router and build tool — all data fetching happens in the browser (React Query + fetch).
 */
const nextConfig: NextConfig = {
  reactStrictMode: true,
  // Presigned MinIO URLs are short-lived and host-relative to the deployment; the classic
  // <img> pipeline (no next/image optimization server) keeps the client fully static.
  images: { unoptimized: true },
};

export default nextConfig;
