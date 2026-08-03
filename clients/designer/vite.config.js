import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Build output goes straight into the API's wwwroot, so `dotnet run` serves the app
// and no Node is needed at runtime. `npm run dev` proxies /api to the API for live reload.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../../src/WorkflowDemo.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: { '/api': 'http://localhost:5000' },
  },
});
