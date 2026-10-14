import react from '@vitejs/plugin-react';
import type { ProxyOptions } from 'vite';
import { defineConfig } from 'vitest/config';

// During development you can run the phone UI with hot reload against a running Fluxio
// desktop app:  FLUXIO_TARGET=http://192.168.1.20:47800 npm run dev
// The proxy rewrites Host/Origin so the app's same-origin checks still pass.
const target = process.env.FLUXIO_TARGET;

function proxyTo(url: string): ProxyOptions {
  return {
    target: url,
    changeOrigin: true,
    configure: (proxy) => {
      proxy.on('proxyReq', (req) => {
        req.setHeader('origin', url);
        req.removeHeader('sec-fetch-site');
      });
    },
  };
}

export default defineConfig({
  plugins: [react()],
  base: '/',
  build: {
    outDir: 'dist',
    assetsDir: 'assets',
    sourcemap: false,
    // Phones: Safari 15+ (iOS 15) and recent Chromium browsers.
    target: ['es2022', 'safari15', 'chrome100', 'firefox100'],
    modulePreload: { polyfill: false },
  },
  server: target ? { host: true, proxy: { '/api': proxyTo(target), '/dl': proxyTo(target) } } : undefined,
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
