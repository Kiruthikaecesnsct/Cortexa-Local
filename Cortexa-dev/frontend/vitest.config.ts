import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  define: {
    'import.meta.env.VITE_API_BASE_URL': JSON.stringify('http://localhost:5000/api/v1'),
    'import.meta.env.VITE_ENTRA_TENANT_ID': JSON.stringify(''),
    'import.meta.env.VITE_ENTRA_CLIENT_ID': JSON.stringify(''),
    'import.meta.env.VITE_ENTRA_REDIRECT_URI': JSON.stringify(''),
    'import.meta.env.VITE_ENTRA_API_SCOPE': JSON.stringify(''),
  },
  test: {
    environment: 'jsdom',
    globals: true,
  },
});
