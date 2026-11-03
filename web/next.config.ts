import type { NextConfig } from "next";

// The browser only ever talks to this server; the API base URL is server-side configuration (API_BASE_URL).
// Covers come from the public blob container (T-085). COVER_IMAGE_BASE_URL is that container's address, read at build
// time; locally it is Azurite. next/image only optimises images from this prefix.
const coverBase = new URL(process.env.COVER_IMAGE_BASE_URL ?? "http://127.0.0.1:10000/devstoreaccount1/product-covers/");
const local = ["127.0.0.1", "localhost", "::1"].includes(coverBase.hostname);

const config: NextConfig = {
  // A self-contained server (.next/standalone) for the container image (deploy/docker/Web.Dockerfile, T-062).
  output: "standalone",
  poweredByHeader: false,
  reactStrictMode: true,
  images: {
    remotePatterns: [
      {
        protocol: coverBase.protocol.replace(":", "") as "http" | "https",
        hostname: coverBase.hostname,
        port: coverBase.port,
        pathname: `${coverBase.pathname.replace(/\/$/, "")}/**`,
      },
    ],
    // Next refuses to fetch from local addresses by default; only Azurite during development needs it.
    dangerouslyAllowLocalIP: local,
  },
};

export default config;
