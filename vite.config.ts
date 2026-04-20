import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  base: './',
  plugins: [vue()],
  server: {
    port: 5173,
    host: '0.0.0.0',
    proxy: {
      '/mes-api': {
        target: 'http://172.25.57.144:8076',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/mes-api/, ''),
        configure: (proxy) => {
          proxy.on('error', (err) => {
            console.log('[MES代理错误]', err.message)
          })
          proxy.on('proxyReq', (_, req) => {
            console.log('[MES代理请求]', req.method, req.url)
          })
        }
      },
      '/mes-push': {
        target: 'http://172.25.57.144:8072',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/mes-push/, '')
      }
    }
  },
  build: {
    target: 'es2015',
    minify: 'esbuild'
  }
})
