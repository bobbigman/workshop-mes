import { createApp } from 'vue'
import App from './App.vue'
import router from './router'

// 【业务背景】PC 后台用 Element Plus；移动报工用 Vant（按需引入，在对应页面再引）。
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import 'element-plus/dist/index.css'
// 【视觉主题】浅色极简主题令牌 + Element Plus 变量覆盖，必须在 element-plus 样式之后引入
import './styles/theme.css'

// 下拉默认提示字：英文 Select →「选择」（与业务文案一致；带 placeholder 的组件仍用自己的）
zhCn.el.select.placeholder = '选择'
if (zhCn.el.cascader) zhCn.el.cascader.placeholder = '选择'

// 【主题切换】默认 light 新主题；legacy 为旧深色侧边栏主题。读取本地偏好应用到 body。
const theme = localStorage.getItem('theme') || 'light'
if (theme === 'legacy') document.body.classList.add('theme-legacy')

const app = createApp(App)
app.use(router)
app.use(ElementPlus, { locale: zhCn })
app.mount('#app')
// 首个路由加载并完成渲染后才撤掉启动提示，路由依赖失败时保留诊断。
router.isReady().then(() => {
  window.dispatchEvent(new Event('mes:ready'))
}).catch(error => {
  console.error('[启动] 首个页面加载失败', error)
  throw error
})

// PWA：生产构建下注册 Service Worker（开发模式不注册，避免干扰 HMR）
if ('serviceWorker' in navigator && import.meta.env.PROD) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch((err) => {
      console.warn('[pwa] Service Worker 注册失败', err)
    })
  })
}
