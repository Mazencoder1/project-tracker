import path from "node:path";
import type { NextConfig } from "next";

// Migration note: the UI is compiled to a static bundle that ASP.NET Core serves from wwwroot.
// No visual code is changed; `unoptimized` only stops next/image from routing through the Node
// image optimizer (the emblem is an SVG, so the rendered output is identical).
const nextConfig: NextConfig = {
  output: "export",
  images: { unoptimized: true },
  turbopack: { root: path.resolve(__dirname) },
};

export default nextConfig;
