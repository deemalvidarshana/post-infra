import type { NextConfig } from "next";

const identityBaseUrl = process.env.IDENTITY_BASE_URL ?? "http://identity-api:8080";

const nextConfig: NextConfig = {
  output: 'standalone',
  async rewrites() {
    return [
      {
        source: '/api/identity/:path*',
        destination: `${identityBaseUrl.replace(/\/$/, "")}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
