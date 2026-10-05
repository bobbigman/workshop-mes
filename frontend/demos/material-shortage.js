import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import 'element-plus/dist/index.css'
import Demo from './MaterialShortage.vue'

createApp(Demo).use(ElementPlus, { locale: zhCn }).mount('#app')
