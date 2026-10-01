import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import path from 'path'

// 【业务背景】局域网部署：开发时代理到后端 API，生产构建出静态 dist/ 由 IIS/Nginx 托管。
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': path.resolve(__dirname, 'src') }
  },
  server: {
    port: 5173,
    // 【TODO·填空】把 target 改成你的后端内网地址（如 http://192.168.1.100:8080）
    proxy: {
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true
      }
    }
  }
})
