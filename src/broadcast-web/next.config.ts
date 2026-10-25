import type { NextConfig } from "next";
const config: NextConfig = {
  output: "standalone",
  agentRules: false,
  async rewrites() {
    const backend = process.env.API_INTERNAL_URL ?? "http://localhost:5080";
    return ["/api/:path*", "/hubs/:path*", "/iptv/:path*", "/hls/:path*"].map(
      (source) => ({ source, destination: `${backend}${source}` }),
    );
  },
};
export default config;
