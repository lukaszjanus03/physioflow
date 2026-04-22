/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  images: {
    remotePatterns: [
      // Cloudflare R2 public URLs (jeśli zrobimy publiczny bucket dla zdjęć ćwiczeń)
      { protocol: 'https', hostname: '*.r2.cloudflarestorage.com' },
      { protocol: 'https', hostname: 'img.youtube.com' }, // thumbnaile YT
    ],
  },
  async rewrites() {
    return [
      // Proxy do backendu w dev - omija CORS
      ...(process.env.NODE_ENV === 'development'
        ? [{ source: '/api/:path*', destination: `${process.env.NEXT_PUBLIC_API_URL}/api/:path*` }]
        : []),
    ];
  },
};

export default nextConfig;
