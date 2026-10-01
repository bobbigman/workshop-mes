<template>
  <div class="login-shell">
    <div class="login-left">
      <img :src="bannerUrl || DEFAULT_BANNER" class="banner" alt="" @error="onBannerError" />
    </div>
    <div class="login-right">
      <div class="login-box">
        <img class="brand-logo" src="/brand/xiaomifeng-logo.svg" alt="小蜜蜂 · 轻MES" />
        <p v-if="factoryLabel" class="factory">{{ factoryLabel }}</p>
        <p v-else-if="instanceInfoError" class="factory factory-err">
          实例信息加载失败
          <el-button link type="primary" :loading="instanceInfoLoading" @click="loadInstanceInfo">重试</el-button>
        </p>
        <h1>{{ productTitle }}</h1>
        <p class="sub">{{ subtitle }}</p>
        <el-form :model="form" label-position="top" @submit.prevent="onLogin">
          <el-form-item v-if="showFactorySelect" label="账套 / 工厂">
            <el-select v-model="form.factoryCode" size="large" placeholder="请选择账套" style="width: 100%">
              <el-option
                v-for="f in factories"
                :key="f.factoryCode"
                :label="f.factoryName"
                :value="f.factoryCode"
              />
            </el-select>
          </el-form-item>
          <el-form-item v-else-if="factoriesLoading" label="账套 / 工厂">
            <div class="factory-hint">正在加载账套列表…</div>
          </el-form-item>
          <el-form-item v-else-if="factoriesLoadError" label="账套 / 工厂">
            <div class="factory-hint err">
              账套列表加载失败（后端可能还在启动）
              <el-button link type="primary" :loading="factoriesLoading" @click="loadFactories">重试</el-button>
            </div>
          </el-form-item>
          <el-form-item label="账号">
            <el-input v-model="form.account" size="large" placeholder="请输入账号" autocomplete="username" />
          </el-form-item>
          <el-form-item label="密码">
            <el-input
              v-model="form.password"
              size="large"
              type="password"
              show-password
              placeholder="请输入密码"
              autocomplete="current-password"
            />
          </el-form-item>
          <el-button
            class="login-btn"
            type="primary"
            size="large"
            :loading="loading"
            :disabled="clearing"
            @click="onLogin"
          >登录</el-button>
        </el-form>
        <p class="hint">账号或密码有问题，请联系管理员</p>
        <div class="cache-tools">
          <el-button
            class="cache-btn"
            size="default"
            :loading="clearing"
            :disabled="loading || clearing"
            @click="onClearCache"
          >{{ clearing ? '正在清理…' : '清缓存并刷新' }}</el-button>
          <p class="cache-hint">页面显示异常或更新未生效时可使用，保留登录信息。</p>
          <p v-if="form.password" class="cache-hint cache-hint-warn">刷新后需重新输入未提交的密码。</p>
          <p v-if="cacheLimitedMsg" class="cache-hint cache-hint-warn">{{ cacheLimitedMsg }}</p>
          <el-button
            v-if="cacheLimitedMsg"
            link
            type="primary"
            @click="doRefreshAfterClear"
          >刷新页面</el-button>
        </div>
        <p class="manual-entry"><router-link to="/help">操作手册</router-link></p>
      </div>
      <div class="credit">
        <p class="credit-line">{{ CREDIT_LINE }}</p>
        <p class="credit-roles">{{ CREDIT_ROLES }}</p>
        <p class="version">版本 {{ version || '—' }}</p>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { login, getInstanceInfo, getFactories, getLoginSetting, requestClearBrowserCache } from '@/api/auth'
import { ElMessage } from 'element-plus'
import {
  PRODUCT_TITLE,
  CREDIT_LINE,
  CREDIT_ROLES,
  resolveFactoryLabel,
  resolveInstanceLabel,
  resolveDocumentTitle
} from '@/utils/instanceDisplay'
import {
  runClearBrowserCache,
  saveFormForRestore,
  consumeFormRestore,
  buildRefreshUrl,
  stripRefreshParam
} from '@/utils/clearBrowserCache'

const route = useRoute()
const router = useRouter()
const loading = ref(false)
const clearing = ref(false)
const cacheLimitedMsg = ref('')
const displayName = ref('')
const version = ref(typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '')
const form = ref({ account: '', password: '', factoryCode: '' })
const factories = ref([])
const factoriesLoading = ref(false)
const factoriesLoadError = ref(false)
const instanceInfoLoading = ref(false)
const instanceInfoError = ref(false)
const showFactorySelect = computed(() => factories.value.length >= 2)
const narrow = ref(false)
let mql

// 未配置登录图时用 public 默认图（部署后进 wwwroot，由 .NET UseStaticFiles 提供）
const DEFAULT_BANNER = '/login-banner-default.jpg'
const bannerUrl = ref(DEFAULT_BANNER)
const instanceFactoryCode = ref('')
/** 防止默认图也失败时 @error 死循环 */
let bannerFallbackUsed = false

const productTitle = PRODUCT_TITLE
const factoryLabel = computed(() => resolveFactoryLabel(displayName.value))
const subtitle = computed(() =>
  narrow.value ? '扫码报工，查看生产任务' : '生产进度 · 工单管理 · 报工统计'
)

function checkNarrow() {
  narrow.value = window.matchMedia('(max-width: 480px)').matches
}

function normalizeBannerUrl(raw) {
  if (raw == null) return ''
  const s = String(raw).trim()
  if (!s || s === 'null' || s === 'undefined') return ''
  return s
}

function onBannerError() {
  if (bannerFallbackUsed) return
  bannerFallbackUsed = true
  bannerUrl.value = DEFAULT_BANNER
}

async function loadBanner(code) {
  bannerFallbackUsed = false
  if (!code) { bannerUrl.value = DEFAULT_BANNER; return }
  try {
    const res = await getLoginSetting(code)
    bannerUrl.value = normalizeBannerUrl(res.data?.bannerUrl) || DEFAULT_BANNER
  } catch {
    bannerUrl.value = DEFAULT_BANNER
  }
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

async function loadInstanceInfo() {
  instanceInfoLoading.value = true
  instanceInfoError.value = false
  const maxAttempts = 4
  let lastErr = null
  try {
    for (let i = 0; i < maxAttempts; i++) {
      try {
        const res = await getInstanceInfo()
        displayName.value = res.data?.displayName || ''
        if (res.data?.version) version.value = res.data.version
        instanceFactoryCode.value = res.data?.factoryCode || ''
        document.title = resolveDocumentTitle(displayName.value)
        instanceInfoError.value = false
        return
      } catch (e) {
        lastErr = e
        if (i < maxAttempts - 1) await sleep(600 * (i + 1))
      }
    }
    instanceInfoError.value = true
    console.warn('[登录页] 实例信息加载失败', lastErr)
  } finally {
    instanceInfoLoading.value = false
  }
}

/** 重启后后端常晚于前端就绪：失败自动重试，仍失败则露出「重试」按钮。 */
async function loadFactories() {
  factoriesLoading.value = true
  factoriesLoadError.value = false
  const maxAttempts = 6
  let lastErr = null
  try {
    for (let i = 0; i < maxAttempts; i++) {
      try {
        const res = await getFactories()
        const list = res.data || []
        factories.value = list
        factoriesLoadError.value = false
        if (list.length === 1) {
          form.value.factoryCode = list[0].factoryCode
          await loadBanner(list[0].factoryCode)
        } else {
          await loadBanner(instanceFactoryCode.value)
        }
        return
      } catch (e) {
        lastErr = e
        if (i < maxAttempts - 1) await sleep(800 * (i + 1))
      }
    }
    factories.value = []
    factoriesLoadError.value = true
    console.warn('[登录页] 账套列表加载失败', lastErr)
  } finally {
    factoriesLoading.value = false
  }
}

async function restoreFormAfterCacheRefresh() {
  stripRefreshParam()
  const saved = consumeFormRestore()
  if (!saved) return
  if (saved.account) form.value.account = saved.account
  // 账套列表加载后再套用仍有效的 factoryCode
  return saved.factoryCode || ''
}

onMounted(async () => {
  checkNarrow()
  mql = window.matchMedia('(max-width: 480px)')
  mql.addEventListener('change', checkNarrow)
  const pendingFactory = await restoreFormAfterCacheRefresh()
  await loadInstanceInfo()
  await loadFactories()
  if (pendingFactory) {
    const stillValid = factories.value.some((f) => f.factoryCode === pendingFactory)
    if (stillValid) form.value.factoryCode = pendingFactory
    // 账套已不存在：不造数据，沿用 loadFactories 原选择规则
  }
})

// 多账套切换时重新拉对应账套的登录图
watch(() => form.value.factoryCode, (code) => {
  if (factories.value.length >= 2) loadBanner(code || instanceFactoryCode.value)
})

onUnmounted(() => {
  mql?.removeEventListener('change', checkNarrow)
})

function doRefreshAfterClear() {
  saveFormForRestore(form.value.account, form.value.factoryCode)
  location.replace(buildRefreshUrl(location.href))
}

async function onClearCache() {
  if (loading.value || clearing.value) return
  cacheLimitedMsg.value = ''
  clearing.value = true
  try {
    const result = await runClearBrowserCache({ requestClear: requestClearBrowserCache })
    if (!result.ok) {
      ElMessage.error(result.message || '清缓存失败')
      return
    }
    if (result.limited) {
      cacheLimitedMsg.value = result.message
      ElMessage.warning(result.message)
      return
    }
    ElMessage.success(result.message)
    doRefreshAfterClear()
  } catch (err) {
    ElMessage.error(err?.message || '清缓存失败')
  } finally {
    clearing.value = false
  }
}

async function onLogin() {
  if (loading.value || clearing.value) return
  if (!form.value.account || !form.value.password) {
    ElMessage.error('请填写账号和密码')
    return
  }
  try {
    loading.value = true
    const res = await login({
      account: form.value.account,
      password: form.value.password,
      factoryCode: form.value.factoryCode || ''
    })
    localStorage.setItem('token', res.data.token)
    localStorage.setItem('userId', String(res.data.userId || ''))
    localStorage.setItem('userName', res.data.name)
    localStorage.setItem('role', String(res.data.role))
    localStorage.setItem('instanceName', resolveInstanceLabel(displayName.value))
    // 当前登录工厂名（PC 侧栏 / H5 顶栏 / 看板展示用）
    if (res.data.factoryName) localStorage.setItem('factoryName', res.data.factoryName)
    if (res.data.factoryCode) localStorage.setItem('factoryCode', res.data.factoryCode)
    localStorage.setItem('licenseTier', res.data.licenseTier || 'trial')
    if (res.data.licenseStatus) localStorage.setItem('licenseStatus', res.data.licenseStatus)
    if (res.data.licenseMessage) localStorage.setItem('licenseMessage', res.data.licenseMessage)
    else localStorage.removeItem('licenseMessage')
    const redirect = route.query.redirect
    try {
      const role = Number(res.data.role || 0)
      const failure = typeof redirect === 'string' && redirect.startsWith('/h5/') && !redirect.startsWith('//')
        ? await router.replace(redirect)
        : await router.push(role === 2 ? '/h5/home' : '/order')
      if (failure) throw failure
      ElMessage.success('登录成功')
    } catch (error) {
      console.error('[登录页] 身份验证成功后打开目标页面失败', error)
      ElMessage.error('账号验证成功，但页面加载失败，请刷新后重试')
    }
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-shell {
  min-height: 100vh;
  display: flex;
  background: var(--app-login-bg);
  box-sizing: border-box;
}

.login-left {
  flex: 0 0 58%;
  display: flex;
  min-width: 0;
}

.login-left .banner {
  width: 100%;
  height: 100vh;
  object-fit: cover;
  display: block;
}

.login-right {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 24px 16px;
  box-sizing: border-box;
}

.login-box {
  background: var(--app-card);
  width: 400px;
  max-width: 100%;
  padding: 40px 36px 28px;
  border-radius: var(--app-radius);
  box-shadow: var(--app-shadow-lg);
  box-sizing: border-box;
}

.brand-logo {
  display: block;
  width: 260px;
  max-width: 100%;
  height: auto;
  margin: 0 auto 20px;
}

.factory {
  text-align: center;
  margin: 0 0 8px;
  font-size: 15px;
  line-height: 1.4;
  color: var(--app-text-2);
  font-weight: 500;
}
.factory-err {
  color: #cf1322;
  font-weight: 400;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  flex-wrap: wrap;
}

.login-box h1 {
  text-align: center;
  font-size: 26px;
  line-height: 1.3;
  margin: 0;
  color: var(--app-text-1);
  font-weight: 700;
}

.sub {
  text-align: center;
  color: var(--app-text-2);
  font-size: 15px;
  line-height: 1.5;
  margin: 10px 0 28px;
}

.login-box :deep(.el-form-item) {
  margin-bottom: 18px;
}

.login-box :deep(.el-form-item__label) {
  font-size: 16px;
  font-weight: 500;
  color: var(--app-text-1);
  line-height: 1.4;
  margin-bottom: 6px !important;
  padding: 0;
}

.login-box :deep(.el-input__wrapper) {
  min-height: 48px;
  padding: 4px 14px;
}

.login-box :deep(.el-input__inner) {
  font-size: 16px;
  height: 40px;
}

.login-btn {
  width: 100%;
  height: 50px;
  margin-top: 6px;
  font-size: 16px;
}

.hint {
  text-align: center;
  margin: 18px 0 0;
  font-size: 14px;
  line-height: 1.5;
  color: var(--app-text-2);
}

.factory-hint {
  font-size: 14px;
  line-height: 1.5;
  color: var(--app-text-2);
  min-height: 48px;
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.factory-hint.err { color: #cf1322; }

.cache-tools {
  margin: 14px 0 0;
  text-align: center;
}

.cache-btn {
  width: 100%;
}

.cache-hint {
  margin: 8px 0 0;
  font-size: 13px;
  line-height: 1.5;
  color: var(--app-text-2);
}

.cache-hint-warn {
  color: #b88230;
}

.manual-entry {
  text-align: center;
  margin: 10px 0 0;
  font-size: 14px;
}

.manual-entry a {
  color: var(--app-primary);
}

.credit {
  text-align: center;
  margin: 16px 0 0;
  max-width: 400px;
  padding: 0 8px;
  box-sizing: border-box;
}

.credit-line {
  margin: 0;
  font-size: 13px;
  line-height: 1.5;
  color: var(--app-text-2);
  letter-spacing: 0.02em;
}

.credit-roles {
  margin: 4px 0 0;
  font-size: 12px;
  line-height: 1.5;
  color: var(--app-text-2);
  opacity: 0.9;
}

.version {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--app-text-2);
  letter-spacing: 0.02em;
  opacity: 0.85;
}

@media (max-width: 768px) {
  .login-shell { flex-direction: column; }
  .login-left { flex: none; width: 100%; }
  .login-left .banner { height: 200px; }
  .login-right { flex: 1; }
}

@media (max-width: 480px) {
  .login-right {
    padding: 16px 12px;
  }
  .login-box {
    width: 100%;
    padding: 32px 20px 24px;
  }
}
</style>
