<template>
  <el-container class="layout" :class="{ 'is-mobile': isMobile, 'monitor-shell': $route.path === '/execution-monitor' }">
    <el-aside v-show="!isMobile" width="var(--app-aside-width)" class="aside">
      <div class="logo">
        <div class="logo-title">{{ factoryName || instanceName }}</div>
        <div v-if="factoryName" class="logo-sub">{{ PRODUCT_TITLE }}</div>
      </div>
      <SideMenu :role="role" :tier="tier" :active="$route.path" />
    </el-aside>

    <el-drawer v-model="drawerOpen" direction="ltr" size="78%" :with-header="false" class="nav-drawer">
      <div class="logo">
        <div class="logo-title">{{ factoryName || instanceName }}</div>
        <div v-if="factoryName" class="logo-sub">{{ PRODUCT_TITLE }}</div>
      </div>
      <SideMenu :role="role" :tier="tier" :active="$route.path" @navigate="drawerOpen = false" />
    </el-drawer>

    <el-container class="main-wrap">
      <el-header class="header">
        <div class="header-left">
          <button
            v-if="isMobile"
            type="button"
            class="menu-btn"
            aria-label="打开菜单"
            @click="drawerOpen = true"
          >
            <span /><span /><span />
          </button>
          <span class="page-title">{{ $route.meta.title || '首页' }}</span>
        </div>
        <div class="header-right">
          <el-button
            v-if="isMobile"
            type="primary"
            :icon="Iphone"
            @click="goH5"
          >报工端</el-button>
          <el-button v-if="!isMobile" text @click="toggleTheme">{{ isLegacy ? '切简约版' : '切经典版' }}</el-button>
          <el-button
            v-if="!isMobile"
            text
            title="本页帮助（F1）"
            aria-label="本页帮助"
            @click="pageHelpRef?.toggle()"
          >？</el-button>
          <span v-if="!isMobile" class="user">{{ userName }}</span>
          <el-button :size="isMobile ? 'small' : 'default'" @click="onLogout">退出</el-button>
        </div>
      </el-header>
      <el-alert
        v-if="licenseBanner"
        class="license-banner"
        :title="licenseBanner"
        :type="licenseBannerType"
        show-icon
        :closable="false"
      />
      <el-main class="main"><router-view /></el-main>
    </el-container>
    <PageHelp ref="pageHelpRef" :enabled="!isMobile" />
  </el-container>
</template>

<script setup>
import { ref, onMounted, onUnmounted, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Iphone } from '@element-plus/icons-vue'
import SideMenu from './SideMenu.vue'
import PageHelp from '@/components/PageHelp.vue'
import { getInstanceInfo, getLicenseStatus } from '@/api/auth'
import { resolveInstanceLabel, PRODUCT_TITLE } from '@/utils/instanceDisplay'

const router = useRouter()
const route = useRoute()
const userName = ref(localStorage.getItem('userName') || '')
const instanceName = ref(resolveInstanceLabel(localStorage.getItem('instanceName')))
const factoryName = ref(localStorage.getItem('factoryName') || '')
const role = Number(localStorage.getItem('role') || 0)
const tier = ref(localStorage.getItem('licenseTier') || 'trial')
const licenseBanner = ref('')
const licenseBannerType = ref('warning')

const isLegacy = ref(localStorage.getItem('theme') === 'legacy')
const isMobile = ref(false)
const drawerOpen = ref(false)
const pageHelpRef = ref(null)
let mql

function applyLicenseStatus(data) {
  if (!data) return
  if (data.effectiveTier) {
    tier.value = data.effectiveTier
    localStorage.setItem('licenseTier', data.effectiveTier)
  }
  if (role !== 1) {
    licenseBanner.value = ''
    return
  }
  if (data.status === 'expired') {
    licenseBanner.value = data.message || '授权已过期，高级功能暂不可用，请联系部署方续费'
    licenseBannerType.value = 'error'
  } else if (data.status === 'grace') {
    licenseBanner.value = data.message || '授权校验已超过一段时间，请尽快联网'
    licenseBannerType.value = 'warning'
  } else {
    licenseBanner.value = ''
  }
}

/** docs/141：工人拿手机误开 PC 后台 → 送进报工端 */
function maybeRedirectWorkerToH5() {
  if (!isMobile.value) return
  if (role !== 2) return
  if (route.path.startsWith('/h5')) return
  router.replace('/h5/home')
}

function checkMobile() {
  isMobile.value = window.matchMedia('(max-width: 768px)').matches
  if (!isMobile.value) drawerOpen.value = false
  maybeRedirectWorkerToH5()
}

function toggleTheme() {
  isLegacy.value = !isLegacy.value
  localStorage.setItem('theme', isLegacy.value ? 'legacy' : 'light')
  document.body.classList.toggle('theme-legacy', isLegacy.value)
}

function onLogout() {
  localStorage.clear()
  router.push('/login')
}

function goH5() {
  router.push('/h5/home')
}

watch(() => route.path, () => {
  drawerOpen.value = false
  maybeRedirectWorkerToH5()
})

onMounted(() => {
  checkMobile()
  mql = window.matchMedia('(max-width: 768px)')
  mql.addEventListener('change', checkMobile)
  getInstanceInfo().then(res => {
    if (res.data?.displayName) {
      const label = resolveInstanceLabel(res.data.displayName)
      instanceName.value = label
      localStorage.setItem('instanceName', label)
    }
  }).catch(() => {
    ElMessage.warning('实例信息加载失败，侧栏名称可能不是最新')
  })
  getLicenseStatus().then(res => {
    if (res.code === 0) applyLicenseStatus(res.data)
  }).catch(() => {
    // 保留本地 licenseTier 缓存，不因断网清空
    ElMessage.warning('授权状态刷新失败，暂用本地缓存档位')
  })
})

onUnmounted(() => {
  mql?.removeEventListener('change', checkMobile)
})
</script>

<style scoped>
.layout { height: 100vh; width: 100%; min-width: 0; }
.aside {
  background: var(--app-aside-bg);
  overflow-y: auto;
  border-right: 1px solid var(--app-aside-border);
  flex-shrink: 0;
}
.logo {
  display: flex;
  flex-direction: column;
  justify-content: center;
  box-sizing: border-box;
  min-height: var(--app-logo-min-height);
  padding: var(--app-logo-padding);
  background: var(--app-logo-bg);
  border-bottom: 1px solid var(--app-logo-border);
  color: var(--app-logo-color);
}
.logo-title {
  font-weight: 700;
  font-size: var(--app-logo-title-size);
  line-height: 1.3;
}
.main-wrap { min-width: 0; flex: 1; }
.license-banner { margin: 0; border-radius: 0; }
.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: var(--app-header-bg);
  border-bottom: 1px solid var(--app-border);
  box-shadow: var(--app-header-shadow);
  height: var(--app-header-height) !important;
  padding: 0 var(--app-page-padding-x);
}
.header-left { display: flex; align-items: center; gap: 10px; min-width: 0; }
.page-title {
  font-size: 18px;
  font-weight: 600;
  color: var(--app-text-1);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.header-right { display: flex; align-items: center; flex-shrink: 0; gap: 8px; }
.user { margin: 0 10px; color: var(--app-text-2); font-size: 14px; }
.main { background: var(--app-bg); min-width: 0; }

/* 监控工作台：深蓝导航只作用于本页，其他业务页保留现有主题。 */
.monitor-shell {
  --app-aside-width: 196px;
  --app-aside-bg: #092748;
  --app-aside-border: #123558;
  --app-aside-text: #c1d0e2;
  --app-aside-text-active: #fff;
  --app-aside-active-bg: #246fe5;
  --app-aside-hover-bg: #153a60;
  --app-aside-group-text: #6e90b3;
  --app-logo-bg: #092748;
  --app-logo-color: #fff;
  --app-logo-sub-color: #7b9abb;
  --app-logo-border: #163655;
  --app-logo-min-height: 66px;
  --app-menu-item-height: 40px;
  --app-header-bg: #092748;
  --app-header-height: 56px;
}
.monitor-shell .main { padding: 18px 20px; background: #f2f5fa; }
.monitor-shell .header { border-bottom-color: #163655; }
.monitor-shell .page-title { color: #f2f6fc; font-size: 16px; }
.monitor-shell .user { color: #c1d0e2; }
.monitor-shell .header-right :deep(.el-button) { color: #d5e2f3; background: transparent; border-color: #385474; }
.monitor-shell .menu-btn span { background: #fff; }

.menu-btn {
  width: 36px; height: 36px; padding: 8px;
  border: none; background: transparent; border-radius: 8px;
  display: flex; flex-direction: column; justify-content: center; gap: 5px;
  cursor: pointer;
}
.menu-btn:active { background: var(--app-border-light); }
.menu-btn span {
  display: block; height: 2px; width: 100%;
  background: var(--app-text-1); border-radius: 1px;
}

.is-mobile .header { padding: 0 16px; }
.is-mobile .page-title { font-size: 16px; }
.is-mobile .main { padding: 12px !important; }
</style>

<style>
.nav-drawer .el-drawer__body {
  padding: 0;
  background: var(--app-aside-bg);
  overflow-y: auto;
}
.nav-drawer .logo {
  display: flex;
  flex-direction: column;
  justify-content: center;
  box-sizing: border-box;
  min-height: var(--app-logo-min-height);
  padding: var(--app-logo-padding);
  background: var(--app-logo-bg);
  border-bottom: 1px solid var(--app-logo-border);
  color: var(--app-logo-color);
}
.nav-drawer .logo-title {
  font-weight: 700;
  font-size: var(--app-logo-title-size);
  line-height: 1.3;
}
.logo-sub {
  font-size: 12px;
  font-weight: 400;
  line-height: 1.3;
  opacity: 0.7;
}
</style>
