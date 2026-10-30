<template>
  <div class="login-shell">
    <div v-if="!narrow" class="login-left">
      <img v-if="bannerUrl" :src="bannerUrl" class="banner" :class="{ ready: bannerReady }" alt="" decoding="async" @load="bannerReady = true" @error="onBannerError" />
    </div>
    <div class="login-right">
      <div class="login-col">
        <div class="brand-lock">
          <img
            v-if="logoOk"
            class="brand-mark"
            src="/brand/weiju-logo.png"
            alt=""
            @error="onLogoError"
          />
          <span class="brand-name">{{ PRODUCT_TITLE }}</span>
          <span class="brand-tag">轻MES</span>
        </div>
        <div class="login-heading">
          <h1>{{ step === 'credentials' ? '欢迎登录' : '选择工厂' }}</h1>
          <p>让生产进度清晰，让车间协作更简单</p>
        </div>
        <!-- docs/151：账密态 / 选厂态同页切换，登录前不出现厂名下拉 -->
        <el-form
          v-if="step === 'credentials'"
          ref="formRef"
          :model="form"
          label-position="top"
          @submit.prevent="onLogin"
        >
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
        <div v-else class="select-factory-panel">
          <p class="select-factory-title">选择要进入的工厂</p>
          <p class="select-factory-hint">仅显示本账号有权进入的工厂</p>
          <el-button
            v-for="f in candidates"
            :key="f.factoryCode"
            class="factory-pick-btn"
            size="large"
            :loading="loading && selectingCode === f.factoryCode"
            :disabled="loading || clearing"
            @click="onSelectFactory(f.factoryCode)"
          >{{ f.factoryName }}</el-button>
          <button
            type="button"
            class="ghost-link back-credentials"
            :disabled="loading || clearing"
            @click="backToCredentials"
          >返回重新登录</button>
        </div>
        <p v-if="cacheLimitedMsg" class="cache-limited">
          {{ cacheLimitedMsg }}
          <button type="button" class="ghost-link" @click="doRefreshAfterClear">刷新页面</button>
        </p>
      </div>
      <footer class="login-footer">
      <p class="footer-links">
        <button type="button" class="footer-link" @click="onLegalInfo">用户协议</button>
        <span class="footer-sep">|</span>
        <button type="button" class="footer-link" @click="onLegalInfo">隐私政策</button>
        <span class="footer-sep">|</span>
        <button type="button" class="footer-link" @click="onCopyright">版权</button>
        <span class="footer-sep">|</span>
        <button type="button" class="footer-link" @click="goHelp">帮助</button>
      </p>
      <p class="footer-meta">
        <span>{{ PRODUCT_TITLE }} {{ appVersionLabel }}</span>
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
import { ref, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { login, selectFactory, loginByQr, getInstanceInfo, getLoginSetting, requestClearBrowserCache } from '@/api/auth'
import { ElMessage, ElMessageBox } from 'element-plus'
import { User, Lock } from '@element-plus/icons-vue'
import {
  CREDIT_RIGHTS,
  PRODUCT_TITLE,
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
import { setWorkerViewMode } from '@/utils/workerView'
import { loginDestination } from '@/utils/loginDestination'
import { resolveLoginFactory, rememberLoginFactory } from '@/utils/loginAppearance'

const route = useRoute()
const router = useRouter()
const loading = ref(false)
const clearing = ref(false)
const cacheLimitedMsg = ref('')
const displayName = ref('')
const logoOk = ref(true)
/** 角落版本号：只认 package.json → vite define 注入的 __APP_VERSION__，不被后端实例信息覆盖 */
const appVersionLabel = (() => {
  const raw = typeof __APP_VERSION__ !== 'undefined' ? String(__APP_VERSION__ || '').trim() : ''
  if (!raw) throw new Error('[登录页] 未注入 __APP_VERSION__，请检查 vite.config.js define 与 package.json version')
  return raw.startsWith('v') || raw.startsWith('V') ? raw : `v${raw}`
})()
const formRef = ref(null)
const form = ref({ account: '', password: '' })
/** credentials | selectFactory（docs/151 同页两态） */
const step = ref('credentials')
const selectToken = ref('')
const candidates = ref([])
const selectingCode = ref('')

function onLogoError() {
  logoOk.value = false
}

function backToCredentials() {
  step.value = 'credentials'
  selectToken.value = ''
  candidates.value = []
  selectingCode.value = ''
}

const DEFAULT_BANNER = '/login-banner-premium-v2.webp'
const narrow = ref(window.matchMedia('(max-width: 768px)').matches)
const bannerReady = ref(false)
const bannerUrl = ref('')
const instanceFactoryCode = ref('')
let bannerFallbackUsed = false
let bannerRequest = 0
let viewportQuery

function normalizeBannerUrl(raw) {
  if (raw == null) return ''
  const s = String(raw).trim()
  if (!s || s === 'null' || s === 'undefined') return ''
  return s
}

function onBannerError() {
  console.error('[登录页] 背景图片加载失败', bannerUrl.value)
  if (bannerFallbackUsed) return
  bannerFallbackUsed = true
  bannerUrl.value = DEFAULT_BANNER
}

async function loadBanner(code) {
  const request = ++bannerRequest
  if (narrow.value) return
  bannerReady.value = false
  bannerFallbackUsed = false
  if (!code) { bannerUrl.value = DEFAULT_BANNER; return }
  try {
    const res = await getLoginSetting(code)
    if (request !== bannerRequest || narrow.value) return
    bannerUrl.value = normalizeBannerUrl(res.data?.bannerUrl) || DEFAULT_BANNER
  } catch (error) {
    console.error('[登录页] 读取工厂背景配置失败', { factoryCode: code, error })
    if (request === bannerRequest && !narrow.value) bannerUrl.value = DEFAULT_BANNER
  }
}

function onViewportChange(event) {
  narrow.value = event.matches
  if (event.matches) {
    bannerRequest++
    bannerUrl.value = ''
  } else {
    void loadBanner(resolveLoginFactory(route.query.factoryCode, instanceFactoryCode.value))
  }
}

onUnmounted(() => {
  bannerRequest++
  viewportQuery?.removeEventListener('change', onViewportChange)
})

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

async function restoreFormAfterCacheRefresh() {
  stripRefreshParam()
  const saved = consumeFormRestore()
  if (!saved) return
  if (saved.account) form.value.account = saved.account
}

onMounted(async () => {
  viewportQuery = window.matchMedia('(max-width: 768px)')
  viewportQuery.addEventListener('change', onViewportChange)
  await restoreFormAfterCacheRefresh()
  // 背景与实例信息不阻塞登录，个人二维码也不等待装饰图片。
  const rememberedCode = resolveLoginFactory(route.query.factoryCode, '')
  if (rememberedCode) void loadBanner(rememberedCode)
  void loadInstanceInfo().then(() => {
    if (!rememberedCode) void loadBanner(instanceFactoryCode.value)
  })
  // docs/150：个人免密二维码 → /#/login?pq=密钥
  const pq = typeof route.query.pq === 'string' ? route.query.pq.trim() : ''
  if (pq) await tryLoginByQr(pq)
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
  saveFormForRestore(form.value.account, '')
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

function applyLoginSession(data) {
  rememberLoginFactory(data.factoryCode)
  localStorage.setItem('token', data.token)
  localStorage.setItem('userId', String(data.userId || ''))
  localStorage.setItem('userName', data.name)
  localStorage.setItem('role', String(data.role))
  localStorage.setItem('instanceName', resolveInstanceLabel(displayName.value))
  if (data.factoryName) localStorage.setItem('factoryName', data.factoryName)
  if (data.factoryCode) localStorage.setItem('factoryCode', data.factoryCode)
  window.dispatchEvent(new Event('mes-auth-changed'))
  localStorage.setItem('licenseTier', data.licenseTier || 'trial')
  if (data.licenseStatus) localStorage.setItem('licenseStatus', data.licenseStatus)
  if (data.licenseMessage) localStorage.setItem('licenseMessage', data.licenseMessage)
  else localStorage.removeItem('licenseMessage')
  setWorkerViewMode(data.workerViewMode)
}

async function goAfterLogin(data) {
  const redirect = route.query.redirect
  const role = Number(data.role || 0)
  const failure = await router.replace(loginDestination(redirect, role))
  if (failure) throw failure
  ElMessage.success('登录成功')
}

/** 个人免密扫码（docs/150）；失败留在登录页，可改用账号密码 */
async function tryLoginByQr(code) {
  if (loading.value || clearing.value) return
  try {
    loading.value = true
    const res = await loginByQr({ code })
    applyLoginSession(res.data)
    await goAfterLogin(res.data)
  } catch (error) {
    // http 拦截器已弹业务提示；留在登录页，可改用账号密码
    console.warn('[登录页] 个人码免密登录失败', error)
  } finally {
    loading.value = false
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
      password: form.value.password
    })
    if (res.data?.needSelectFactory) {
      selectToken.value = res.data.selectToken || ''
      candidates.value = res.data.candidates || []
      if (!selectToken.value || candidates.value.length < 2) {
        ElMessage.error('选厂信息无效，请重新登录')
        return
      }
      step.value = 'selectFactory'
      return
    }
    applyLoginSession(res.data)
    try {
      await goAfterLogin(res.data)
    } catch (error) {
      console.error('[登录页] 身份验证成功后打开目标页面失败', error)
      ElMessage.error('账号验证成功，但页面加载失败，请刷新后重试')
    }
  } finally {
    loading.value = false
  }
}

/** docs/151：第二步只带 selectToken + factoryCode，绝不带密码 */
async function onSelectFactory(factoryCode) {
  if (loading.value || clearing.value) return
  const code = String(factoryCode || '').trim()
  if (!code || !selectToken.value) {
    ElMessage.error('选厂凭证无效，请重新登录')
    backToCredentials()
    return
  }
  try {
    loading.value = true
    selectingCode.value = code
    const res = await selectFactory({
      selectToken: selectToken.value,
      factoryCode: code
    })
    applyLoginSession(res.data)
    try {
      await goAfterLogin(res.data)
    } catch (error) {
      console.error('[登录页] 选厂成功后打开目标页面失败', error)
      ElMessage.error('账号验证成功，但页面加载失败，请刷新后重试')
    }
  } catch {
    // 过期/复用/伪造：回账密态，http 拦截器已提示
    backToCredentials()
  } finally {
    loading.value = false
    selectingCode.value = ''
  }
}
</script>

<style scoped>
.login-shell {
  min-height: 100vh;
  display: flex;
  background: #fff;
  box-sizing: border-box;
}

.login-left {
  flex: 1 1 62%;
  min-width: 0;
  display: flex;
  overflow: hidden;
  background: linear-gradient(135deg, #e0edfc, #f4f8ff);
}

.login-left .banner {
  width: 100%;
  height: 100vh;
  object-fit: cover;
  object-position: left center;
  display: block;
  opacity: 0;
  transition: opacity 0.35s ease;
}
.login-left .banner.ready { opacity: 1; }

.login-right {
  position: relative;
  flex: 0 0 38%;
  min-width: 380px;
  max-width: 680px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 24px 40px 56px;
  box-sizing: border-box;
  background: #fff;
}

.login-col {
  width: 100%;
  max-width: 360px;
  background: transparent;
  box-shadow: none;
  box-sizing: border-box;
}

.brand-lock {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-wrap: nowrap;
  gap: 10px;
  margin: 0 0 32px;
  white-space: nowrap;
}

.brand-mark {
  width: 48px;
  height: 48px;
  object-fit: contain;
  flex-shrink: 0;
}

.brand-name {
  font-size: 28px;
  font-weight: 600;
  line-height: 1;
  color: #1f2329;
}

.brand-tag {
  font-size: 12px;
  line-height: 1;
  color: #8f959e;
  align-self: flex-end;
  padding-bottom: 2px;
}

.login-heading { margin-bottom: 28px; text-align: center; }
.login-heading h1 { margin: 0 0 10px; font-size: 25px; font-weight: 600; color: #20324d; }
.login-heading p { margin: 0; font-size: 13px; color: #8491a5; }

.login-col :deep(.el-form-item) {
  margin-bottom: 18px;
}

.login-col :deep(.el-form-item__label) {
  font-size: 16px;
  font-weight: 500;
  color: var(--app-text-1);
  line-height: 1.4;
  margin-bottom: 6px !important;
  padding: 0;
}

.login-col :deep(.el-input__wrapper) {
  min-height: 48px;
  padding: 4px 14px;
}

.login-col :deep(.el-input__inner) {
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

.select-factory-panel {
  width: 100%;
}

.select-factory-title {
  margin: 0 0 6px;
  font-size: 18px;
  font-weight: 600;
  color: #1f2329;
  text-align: center;
}

.select-factory-hint {
  margin: 0 0 20px;
  font-size: 13px;
  line-height: 1.4;
  color: var(--app-text-2);
  text-align: center;
}

.factory-pick-btn {
  width: 100%;
  height: 48px;
  margin-bottom: 12px;
  font-size: 15px;
}

.back-credentials {
  display: block;
  width: 100%;
  margin-top: 8px;
  text-align: center;
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
  .login-shell { flex-direction: column; min-height: 100svh; background: linear-gradient(155deg, #e7f0ff, #f7faff 50%, #eef5ff); }
  .login-left { display: none; }
  .login-right {
    flex: 1;
    min-width: 0;
    max-width: none;
    width: 100%;
    min-height: 100svh;
    padding: 48px 24px 24px;
    background: transparent;
  }
  .login-col { margin: auto 0; padding: 32px 24px; border-radius: 20px; background: #fff; box-shadow: 0 12px 40px #305a9810; }
  .login-footer { position: static; margin-top: 28px; padding: 0; }
}

@media (max-width: 480px) {
  .login-right { padding: 32px 20px 20px; }
  .login-col { max-width: none; }
}
@media (prefers-reduced-motion: reduce) { .login-left .banner { transition: none; } }
</style>
