/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  images: {
    remotePatterns: [
      { protocol: 'https', hostname: '*.r2.cloudflarestorage.com' },
      { protocol: 'https', hostname: 'img.youtube.com' },
    ],
  },
  async rewrites() {
    return [
      ...(process.env.NODE_ENV === 'development'
        ? [{ source: '/api/:path*', destination: `${process.env.NEXT_PUBLIC_API_URL}/api/:path*` }]
        : []),
    ];
  },
  webpack: (config, { isServer }) => {
    // Konva nie ma działać po stronie serwera (SSR) - mówimy webpackowi
    // żeby zignorował 'canvas' (Node.js native module wymagany tylko dla SSR Konvy).
    if (isServer) {
      config.externals = [...(config.externals || []), 'canvas'];
    }
    return config;
  },
};

export default nextConfig;