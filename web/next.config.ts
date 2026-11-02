import type { NextConfig } from "next";

// The browser only ever talks to this server; the API base URL is server-side configuration (API_BASE_URL).
const config: NextConfig = {
  poweredByHeader: false,
  reactStrictMode: true,
};

export default config;
