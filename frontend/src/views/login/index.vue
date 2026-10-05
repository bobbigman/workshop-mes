<template>
  <div class="login-shell">
    <div class="login-left">
      <img :src="bannerUrl || DEFAULT_BANNER" class="banner" alt="" @error="onBannerError" />
    </div>
    <div class="login-right">
      <div class="login-box">
        <div class="brand-row">
          <img class="brand-logo" src="/brand/xiaomifeng-logo.svg" alt="小蜜蜂报工 · 轻MES" />
        </div>
        <el-form ref="formRef" :model="form" :rules="formRules" label-position="top" @submit.prevent="onLogin">
          <el-form-item v-if="showFactorySelect" label="账套 / 工厂" prop="factoryCode">
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
            <div class="factory-inline-err">
              <span>账套列表加载失败</span>
              <el-button
                class="factory-refresh"
                link
                type="primary"
                :loading="factoriesLoading"
                :icon="Refresh"
                title="重新加载"
                aria-label="重新加载账套列表"
                @click="loadFactories"
              />
            </div>
          </el-form-item>
          <el-form-item label="账号">
            <el-input v-model="form.account" size="large" placeholder="请输入账号" autocomplete="username">
              <template #prefix>
                <el-icon><User /></el-icon>
              </template>
            </el-input>
          </el-form-item>
          <el-form-item label="密码">
            <el-input
              v-model="form.password"
              size="large"
              type="password"
              show-password
              placeholder="请输入密码"
              autocomplete="current-password"
            >
              <template #prefix>
                <el-icon><Lock /></el-icon>
              </template>
            </el-input>
          </el-form-item>
          <el-button
            class="login-btn"
            type="primary"
            size="large"
            :loading="loading"
            :disabled="clearing"
            @click="onLogin"
          >登录</el-button>
          <div class="forgot-row">
            <button type="button" class="ghost-link" :disabled="loading || clearing" @click="onForgotPassword">忘记密码</button>
          </div>
        </el-form>
        <p v-if="cacheLimitedMsg" class="cache-limited">
          {{ cacheLimitedMsg }}
          <button type="button" class="ghost-link" @click="doRefreshAfterClear">刷新页面</button>
        </p>
      </div>
      <footer class="login-footer">
        <p class="footer-links">
          <button type="button" class="footer-link" @click="onCopyright">版权</button>
          <span class="footer-sep">|</span>
          <button type="button" class="footer-link" @click="onLegalInfo">用户协议</button>
          <span class="footer-sep">|</span>
          <button type="button" class="footer-link" @click="onLegalInfo">隐私政策</button>
          <span class="footer-sep">|</span>
          <button type="button" class="footer-link" @click="goHelp">帮助</button>
        </p>
        <p class="footer-meta">
          <span>小蜜蜂报工 {{ appVersionLabel }}</span>
          <span class="footer-sep">|</span>
          <span>建议浏览器 Chrome</span>
          <span class="footer-sep">|</span>
          <button
            type="button"
            class="footer-link"
            :disabled="loading || clearing"
            @click="onClearCache"
          >{{ clearing ? '正在清理…' : '清除本地缓存' }}</button>
        </p>
      </footer>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { login, getInstanceInfo, getFactories, getLoginSetting, requestClearBrowserCache } from '@/api/auth'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh, User, Lock } from '@element-plus/icons-vue'
import {
  CREDIT_RIGHTS,
  creditLine,
  creditRoles,
  isCreditHydrated,
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
import { setWorkerViewMode, workerHomePath } from '@/utils/workerView'

const route = useRoute()
const router = useRouter()
const loading = ref(false)
const clearing = ref(false)
const cacheLimitedMsg = ref('')
const displayName = ref('')
/** 角落版本号：只认 package.json → vite define 注入的 __APP_VERSION__，不被后端实例信息覆盖 */
const appVersionLabel = (() => {
  const raw = typeof __APP_VERSION__ !== 'undefined' ? String(__APP_VERSION__ || '').trim() : ''
  if (!raw) throw new Error('[登录页] 未注入 __APP_VERSION__，请检查 vite.config.js define 与 package.json version')
  return raw.startsWith('v') || raw.startsWith('V') ? raw : `v${raw}`
})()
const formRef = ref(null)
const form = ref({ account: '', password: '', factoryCode: '' })
const factories = ref([])
const factoriesLoading = ref(false)
const factoriesLoadError = ref(false)
const showFactorySelect = computed(() => factories.value.length >= 2)
const formRules = computed(() => ({
  factoryCode: showFactorySelect.value
    ? [{ required: true, message: '请先选择账套/工厂', trigger: 'change' }]
    : []
}))

// 未配置登录图时用 public 默认图（部署后进 wwwroot，由 .NET UseStaticFiles 提供）
const DEFAULT_BANNER = '/login-banner-default.jpg'
const bannerUrl = ref(DEFAULT_BANNER)
const instanceFactoryCode = ref('')
/** 防止默认图也失败时 @error 死循环 */
let bannerFallbackUsed = false

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
  const maxAttempts = 4
  let lastErr = null
  for (let i = 0; i < maxAttempts; i++) {
    try {
      const res = await getInstanceInfo()
      displayName.value = res.data?.displayName || ''
      instanceFactoryCode.value = res.data?.factoryCode || ''
      document.title = resolveDocumentTitle(displayName.value)
      return
    } catch (e) {
      lastErr = e
      if (i < maxAttempts - 1) await sleep(600 * (i + 1))
    }
  }
  // 实例信息失败：主区不展示，工人靠账套区刷新即可
  console.warn('[登录页] 实例信息加载失败', lastErr)
}

/** 重启后后端常晚于前端就绪：失败自动重试，仍失败则账套区露出内联刷新。 */
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

function goHelp() {
  router.push('/help')
}

function onForgotPassword() {
  ElMessageBox.alert(
    '局域网系统暂不支持自助找回密码，请联系系统管理员重置。',
    '重置密码',
    { confirmButtonText: '知道了', type: 'info' }
  )
}

function onLegalInfo() {
  ElMessageBox.alert(
    '本系统为内网使用，业务数据由本厂管理员管理，具体请联系管理员。',
    '说明',
    { confirmButtonText: '知道了', type: 'info' }
  )
}

async function onCopyright() {
  if (!isCreditHydrated()) {
    try {
      await getInstanceInfo()
    } catch {
      // 回退胡工单方版，不白屏
    }
  }
  const parts = [creditLine.value, CREDIT_RIGHTS]
  if (creditRoles.value) parts.push(creditRoles.value)
  const body = parts.map((line) => `<div>${line}</div>`).join('')
  ElMessageBox.alert(
    `<div style="text-align:center;line-height:1.7">${body}</div>`,
    '版权',
    { confirmButtonText: '知道了', dangerouslyUseHTMLString: true }
  )
}

function doRefreshAfterClear() {
  saveFormForRestore(form.value.account, form.value.factoryCode)
  location.replace(buildRefreshUrl(location.href))
}

async function onClearCache() {
  if (loading.value || clearing.value) return
  try {
    await ElMessageBox.confirm(
      '将清除本站缓存并刷新页面，未提交的账号密码需重新输入，已登录状态保留。是否继续？',
      '清除本地缓存',
      { confirmButtonText: '继续', cancelButtonText: '取消', type: 'warning' }
    )
  } catch {
    return
  }
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
  if (showFactorySelect.value) {
    try {
      await formRef.value?.validateField('factoryCode')
    } catch {
      return
    }
    if (!String(form.value.factoryCode || '').trim()) return
  }
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
    setWorkerViewMode(res.data.workerViewMode)
    const redirect = route.query.redirect
    try {
      const role = Number(res.data.role || 0)
      const failure = typeof redirect === 'string' && redirect.startsWith('/h5/') && !redirect.startsWith('//')
        ? await router.replace(redirect)
        : await router.push(role === 2 ? workerHomePath() : '/order')
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
  position: relative;
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 24px 16px 48px;
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

.brand-row {
  position: relative;
  margin: 0 0 28px;
}

.brand-logo {
  display: block;
  width: 260px;
  max-width: 100%;
  height: auto;
  margin: 0 auto;
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

.forgot-row {
  display: flex;
  justify-content: flex-end;
  margin-top: 8px;
}

.ghost-link {
  padding: 0;
  border: none;
  background: transparent;
  color: var(--app-text-2);
  font-size: 12px;
  line-height: 1.4;
  cursor: pointer;
  opacity: 0.85;
}
.ghost-link:hover:not(:disabled) { color: var(--app-primary); opacity: 1; }
.ghost-link:disabled { cursor: not-allowed; opacity: 0.5; }

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

.factory-inline-err {
  display: flex;
  align-items: center;
  gap: 4px;
  min-height: 40px;
  font-size: 13px;
  line-height: 1.4;
  color: #cf1322;
}

.factory-refresh {
  padding: 0 4px !important;
  height: 24px;
}

.cache-limited {
  margin: 12px 0 0;
  font-size: 12px;
  line-height: 1.5;
  color: #b88230;
  text-align: center;
}

.login-footer {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 12px;
  padding: 0 16px;
  text-align: center;
  box-sizing: border-box;
}

.footer-links,
.footer-meta {
  margin: 0;
  font-size: 11px;
  line-height: 1.6;
  color: var(--app-text-2);
  opacity: 0.7;
}

.footer-meta { margin-top: 2px; }

.footer-sep {
  margin: 0 6px;
  opacity: 0.6;
}

.footer-link {
  padding: 0;
  border: none;
  background: transparent;
  color: inherit;
  font-size: inherit;
  line-height: inherit;
  cursor: pointer;
}
.footer-link:hover:not(:disabled) { color: var(--app-primary); opacity: 1; }
.footer-link:disabled { cursor: not-allowed; opacity: 0.45; }

@media (max-width: 768px) {
  .login-shell { flex-direction: column; }
  .login-left { flex: none; width: 100%; }
  .login-left .banner { height: 200px; }
  .login-right { flex: 1; }
}

@media (max-width: 480px) {
  .login-right {
    padding: 16px 12px 56px;
  }
  .login-box {
    width: 100%;
    padding: 32px 20px 24px;
  }
}
</style>
