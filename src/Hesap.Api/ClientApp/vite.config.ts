import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { fileURLToPath, URL } from "node:url";

// Derleme çıktısı ASP.NET Core'un wwwroot/app klasörüne gider; sayfalar kök adresten (/, /invoices …) sunulur,
// dosyalar /app/ altından yüklenir. Geliştirmede (npm run dev) API istekleri yerel .NET sunucusuna yönlendirilir.
const backend = process.env.HESAP_BACKEND ?? "http://localhost:5300";

export default defineConfig({
  base: "/app/",
  plugins: [react()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  build: {
    outDir: "../wwwroot/app",
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 900,
    rollupOptions: {
      output: {
        // Kütüphaneler ayrı dosyalara: uygulama güncellendiğinde tarayıcı önbelleği korunur.
        manualChunks(id: string) {
          if (!id.includes("node_modules")) return undefined;
          if (/[\/](react|react-dom|react-router|scheduler)[\/]/.test(id)) return "react";
          if (id.includes("@mantine/charts") || id.includes("recharts") || id.includes("d3-")) return "charts";
          if (id.includes("@mantine") || id.includes("@floating-ui")) return "mantine";
          return "vendor";
        },
      },
    },
  },
  server: {
    port: 5174,
    proxy: { "/api": backend },
  },
});
