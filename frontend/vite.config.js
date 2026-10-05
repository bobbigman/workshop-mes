import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import path from 'path'
import { readFileSync } from 'fs'

const pkg = JSON.parse(readFileSync(path.resolve(__dirname, 'package.json'), 'utf-8'))
const appVersion = String(pkg.version || '').trim()
if (!appVersion) {
  throw new Error('[vite] package.json 的 version 不能为空（登录页角落要显示真实版本号）')
}

// 【业务背景】局域网部署：开发时代理到后端 API，生产构建出静态 dist/ 由 IIS/Nginx 托管。
export default defineConfig(({ mode }) => ({
  plugins: [vue()],
  define: {
    // 登录页角落版本：读 package.json version，发版改 version 即自动变（禁止写死 / 禁止空串）
    __APP_VERSION__: JSON.stringify(appVersion)
  },
  resolve: {
    alias: { '@': path.resolve(__dirname, 'src') }
  },
  server: {
    host: '0.0.0.0',
    port: mode === 'b' ? 5174 : 5173,
    // 放开所有外部域名访问，适配cpolar动态域名，不用每次更新域名白名单
    allowedHosts: true,
    // 内网 HTTPS（mkcert 签发）：手机摄像头扫码强制要求 HTTPS，证书在 frontend/certs/
    https: {
      key: readFileSync(path.resolve(__dirname, 'certs/key.pem')),
      cert: readFileSync(path.resolve(__dirname, 'certs/cert.pem'))
    },
    proxy: {
      // 默认连 WorkShop（WorkshopMes:8080）；npm run dev:b 连 WorkShopB（WorkshopMesB:8081）
      '/api': {
        target: mode === 'b' ? 'http://127.0.0.1:8081' : 'http://127.0.0.1:8080',
        changeOrigin: true
      },
      // 登录页 banner 等上传静态文件（docs/69）；开发态不代理会 404 破图
      '/uploads': {
        target: mode === 'b' ? 'http://127.0.0.1:8081' : 'http://127.0.0.1:8080',
        changeOrigin: true
      }
    }
  }
}))
