<template>
  <div class="h5-page">
    <div class="title-row">
      <FactoryBadge />
      <h2>扫码报工</h2>
      <H5LogoutBtn />
    </div>
    <ValueTip :text="VALUE_TIPS.report" tone="h5" compact />
    <section v-if="diagnosticsEnabled" class="camera-diagnostics" aria-label="扫码检查">
      <strong>扫码检查 0922-10</strong>
      <p class="diag-meta">{{ diagnosticMeta }}</p>
      <pre ref="diagPre">{{ diagnosticLines.slice().reverse().join('\n') }}</pre>
      <div class="diag-actions">
        <button type="button" class="diag-btn" @click="copyDiagnostics">复制日志</button>
        <button type="button" class="diag-btn" @click="clearDiagnostics">清空</button>
      </div>
      <p>点一次蓝色按钮，再点「复制日志」发给我即可。</p>
    </section>

    <!-- 手动输入 -->
    <template v-if="manual">
      <p class="hint">请输入工单号</p>
      <div class="manual">
        <input
          ref="manualInput"
          v-model="manualText"
          class="manual-input"
          placeholder="请输入工单号"
          @keyup.enter="onManual"
        />
        <div class="manual-actions">
          <button type="button" class="ghost" @click="closeManual">返回</button>
          <button type="button" class="go" @click="onManual">确定</button>
        </div>
      </div>
    </template>

    <!-- 摄像头模式 -->
    <template v-else-if="mode === 'camera'">
      <button
        v-if="cameraState === 'idle' || cameraState === 'error'"
        type="button"
        class="go camera-start-btn"
        @pointerdown="onCameraStartPointer"
        @click="startCamera"
      >打开摄像头扫码</button>
      <button
        v-else-if="cameraState === 'starting'"
        type="button"
        class="ghost"
        @click="cancelStart"
      >取消</button>
      <button
        v-else
        type="button"
        class="ghost"
        @click="stopCamera"
      >停止摄像头</button>
      <div class="stage">
        <div class="frame" :class="{ live: cameraState === 'scanning' }">
          <video
            ref="videoEl"
            class="preview"
            :class="{ show: cameraState === 'scanning' }"
            muted
            playsinline
            autoplay
          />
          <template v-if="cameraState !== 'scanning'">
            <span class="corner tl" />
            <span class="corner tr" />
            <span class="corner bl" />
            <span class="corner br" />
          </template>
        </div>
        <p class="wait" role="status" aria-live="polite">{{ cameraStatusText }}</p>
      </div>
      <p v-if="cameraHint" class="hint warn">{{ cameraHint }}</p>
      <p v-else class="hint">对准工单流转卡二维码，或改用下方其它方式</p>

      <button type="button" class="manual-btn" @click="openManual">手动输入工单号</button>
      <button type="button" class="linkish" @click="switchToPda">扫码枪模式</button>
    </template>

    <!-- PDA / 扫码枪 -->
    <template v-else>
      <div class="stage">
        <div class="frame" aria-hidden="true">
          <span class="corner tl" />
          <span class="corner tr" />
          <span class="corner bl" />
          <span class="corner br" />
        </div>
        <p class="wait">等待扫描工单</p>
        <input
          ref="scanInput"
          v-model="scanText"
          class="pda-input"
          autocomplete="off"
          aria-label="扫描工单号"
          @keyup.enter="onPdaScan"
          @blur="holdPdaFocus"
        />
      </div>
      <p class="hint">请用扫码枪扫描工单流转卡二维码</p>
      <button type="button" class="manual-btn" @click="openManual">手动输入工单号</button>
      <button type="button" class="linkish" @click="switchToCamera">摄像头扫码</button>
    </template>

    <TabBar active="scan" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import { showToast } from 'vant'
import 'vant/es/toast/style'
import TabBar from './TabBar.vue'
import FactoryBadge from '@/components/FactoryBadge.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'
import jsQR from 'jsqr'
import { parseOrderNo, parseScanPayload } from '@/utils/scanPayload'

const MODE_KEY = 'h5.scan.mode'
const router = useRouter()
// 仅故障检查链接显示；不记录账号、二维码内容或摄像头画面。
const diagnosticsEnabled = typeof window !== 'undefined'
  && new URLSearchParams(window.location?.search || '').get('scan-check')
const diagnosticLines = ref([])
const diagPre = ref(null)
const DIAGNOSTICS_KEY = 'h5.scan.diagnostics.local'
let diagnosticPending = false
if (diagnosticsEnabled) {
  try {
    const saved = JSON.parse(window.localStorage.getItem(DIAGNOSTICS_KEY) || 'null')
    if (Array.isArray(saved?.lines)) {
      diagnosticLines.value = saved.lines.filter(line => typeof line === 'string').slice(-40)
      diagnosticPending = saved.pending === true
    }
  } catch (err) {
    console.error('[h5/scan] 读取扫码诊断记录失败', err)
    diagnosticLines.value = ['浏览器无法读取历史检查记录']
  }
}

function recordCameraStep(message, pending = diagnosticPending) {
  if (!diagnosticsEnabled) return
  diagnosticPending = pending
  diagnosticLines.value = [...diagnosticLines.value.slice(-39), `${new Date().toLocaleTimeString()} ${message}`]
  try {
    // 用 localStorage：崩溃/重载后仍保留（sessionStorage 会随会话/崩溃丢失）。
    window.localStorage.setItem(DIAGNOSTICS_KEY, JSON.stringify({ lines: diagnosticLines.value, pending }))
  } catch (err) {
    console.error('[h5/scan] 保存扫码诊断记录失败', err)
  }
  nextTick(() => {
    if (diagPre.value) diagPre.value.scrollTop = 0
  })
}

function copyDiagnostics() {
  const text = [
    `版本 0922-10；网址 ${typeof window !== 'undefined' && window.location ? window.location.href : '未知'}`,
    `UA ${typeof navigator !== 'undefined' ? (navigator.userAgent || '未知').slice(0, 160) : '未知'}`,
    `可见性 ${typeof document !== 'undefined' ? (document.visibilityState || '未知') : '未知'}；${diagnosticMeta.value}`,
    '--- 记录（新→旧）---',
    ...diagnosticLines.value.slice().reverse()
  ].join('\n')
  const done = () => showToast('日志已复制，请粘贴给我')
  const fail = () => showToast('复制失败，请长按记录区手动复制')
  if (navigator?.clipboard?.writeText) {
    navigator.clipboard.writeText(text).then(done, fail)
  } else {
    const ta = document.createElement('textarea')
    ta.value = text
    document.body.appendChild(ta)
    ta.select()
    try { document.execCommand('copy'); done() } catch (err) { fail() }
    document.body.removeChild(ta)
  }
}

function clearDiagnostics() {
  diagnosticLines.value = []
  diagnosticPending = false
  try { window.localStorage.removeItem(DIAGNOSTICS_KEY) } catch (err) {}
  recordCameraStep('已清空记录')
}

const mode = ref('camera') // camera | pda
const manual = ref(false)
const manualText = ref('')
const manualInput = ref(null)

const scanInput = ref(null)
const scanText = ref('')

const videoEl = ref(null)
const cameraState = ref('idle') // idle | starting | scanning | error
const cameraHint = ref('')
const starting = ref(false)

const diagnosticMeta = computed(() => {
  const ui = manual.value ? '手动输入' : (mode.value === 'pda' ? '扫码枪' : '摄像头')
  return `当前界面：${ui}；摄像头状态：${cameraState.value}${starting.value ? '；启动中' : ''}`
})

function onCameraStartPointer() {
  recordCameraStep(`触摸/按下打开按钮；界面=${manual.value ? '手动' : mode.value}；状态=${cameraState.value}`)
}

let scanTimer = null
let scanCanvas = null
let scanCtx = null
let startSession = 0
let handlingResult = false
let alive = true

const cameraStatusText = computed(() => {
  if (cameraState.value === 'starting') return '正在打开摄像头，请允许使用摄像头…'
  if (cameraState.value === 'scanning') return '请对准工单二维码'
  if (cameraState.value === 'error') return '摄像头未就绪'
  return '点击上方蓝色按钮打开摄像头'
})

onMounted(() => {
  starting.value = false
  if (diagnosticPending) {
    cameraState.value = 'error'
    cameraHint.value = '上次打开摄像头时页面重新加载，请用手机自带浏览器打开同一网址再试'
    recordCameraStep('上次摄像头请求未完成，扫码页面已重新加载', false)
  }
  recordCameraStep(`页面进入方式：${window.performance?.getEntriesByType('navigation')?.[0]?.type || '未知'}`)
  recordCameraStep(`页面已加载；安全环境：${window.isSecureContext ? '是' : '否'}；摄像头接口：${navigator.mediaDevices?.getUserMedia ? '有' : '无'}`)
  recordCameraStep(`可见性：${document?.visibilityState || '未知'}；网址：${window.location?.href || '未知'}`)
  const saved = localStorage.getItem(MODE_KEY)
  if (saved === 'pda') {
    mode.value = 'pda'
    nextTick(() => scanInput.value?.focus())
  } else {
    mode.value = 'camera'
  }
  document.addEventListener('visibilitychange', onVisibility)
  window.addEventListener('pagehide', onPageHide)
  window.addEventListener('pageshow', onPageShow)
  window.addEventListener('beforeunload', onBeforeUnload)
})

onBeforeUnmount(() => {
  recordCameraStep('离开扫码页面，停止请求')
  alive = false
  document.removeEventListener('visibilitychange', onVisibility)
  window.removeEventListener('pagehide', onPageHide)
  window.removeEventListener('pageshow', onPageShow)
  window.removeEventListener('beforeunload', onBeforeUnload)
  stopCamera()
})

function onPageShow(event) {
  recordCameraStep(`页面显示；persisted=${event.persisted}`)
}

function onBeforeUnload() {
  recordCameraStep('即将离开/卸载页面（beforeunload）')
}

function onPageHide(event) {
  recordCameraStep(`浏览器离开页面；${event.persisted ? '页面进入缓存' : '页面未进入缓存'}`)
  // 系统授权界面可能触发 pagehide，但尚未拿到视频流；此时不应取消进行中的权限请求。
  const waitingPermission = cameraState.value === 'starting' && !scanTimer && !videoEl.value?.srcObject
  if (waitingPermission) return
  // 保留未完成标记，供重载后判断；同时使旧请求失效并释放视频。
  startSession += 1
  starting.value = false
  destroyScanner()
  if (cameraState.value === 'starting' || cameraState.value === 'scanning') {
    cameraState.value = 'idle'
    cameraHint.value = '页面已暂停，请重新打开摄像头'
  }
}

function persistMode(next) {
  mode.value = next
  localStorage.setItem(MODE_KEY, next === 'pda' ? 'pda' : 'camera')
}

function onVisibility() {
  recordCameraStep(`页面${document.hidden ? '进入后台' : '返回前台'}`)
  if (document.hidden) {
    // 系统授权界面可能使网页暂时不可见；此时还没有视频流，不取消待授权请求。
    if (cameraState.value === 'starting' && !scanTimer && !videoEl.value?.srcObject) return
    // 授权返回时再次检查前台状态，防止真正离开页面后在后台启动摄像头。
    if (cameraState.value === 'starting' && !scanTimer) return
    if (cameraState.value === 'idle' || cameraState.value === 'error') return
    stopCamera({ keepHint: true })
    if (mode.value === 'camera' && !manual.value) {
      cameraHint.value = cameraHint.value || '已暂停摄像头，返回后请重新打开'
      cameraState.value = 'idle'
    }
  }
}

function goReport(orderNo, operationId) {
  const query = { order: orderNo }
  if (operationId != null) query.op = String(operationId)
  router.push({ path: '/h5/report', query })
}

function applyScanText(raw, { fromManual = false } = {}) {
  const parsed = fromManual ? parseOrderNo(raw) : parseScanPayload(raw)
  if (!parsed.ok) {
    showToast(parsed.error)
    return false
  }
  goReport(parsed.orderNo, parsed.operationId)
  return true
}

async function requestCameraStream() {
  try {
    return await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: { facingMode: { ideal: 'environment' } }
    })
  } catch (err) {
    const name = err?.name || ''
    if (name === 'OverconstrainedError' || name === 'ConstraintNotSatisfiedError') {
      recordCameraStep('后置摄像头不可用，改用默认摄像头')
      return await navigator.mediaDevices.getUserMedia({ audio: false, video: true })
    }
    throw err
  }
}

async function startCamera() {
  recordCameraStep(`点击打开；当前状态：${cameraState.value}`)
  if (starting.value || cameraState.value === 'scanning') return
  cameraHint.value = ''

  if (typeof window !== 'undefined' && !window.isSecureContext) {
    recordCameraStep('无法请求：浏览器未将当前网址视为安全环境')
    cameraState.value = 'error'
    cameraHint.value = '摄像头需要 HTTPS 访问，请改用手动输入或扫码枪'
    return
  }
  if (!navigator.mediaDevices?.getUserMedia) {
    recordCameraStep('无法请求：浏览器未提供摄像头接口')
    cameraState.value = 'error'
    cameraHint.value = '当前浏览器不支持摄像头，请改用手动输入或扫码枪'
    return
  }

  const session = ++startSession
  starting.value = true
  cameraState.value = 'starting'
  handlingResult = false
  let stream = null
  const timeout = setTimeout(() => {
    if (!alive || session !== startSession) return
    recordCameraStep('等待 15 秒超时')
    stopCamera()
    cameraState.value = 'error'
    cameraHint.value = '摄像头启动超时，请检查摄像头权限；若没有权限提示，请用手机系统浏览器打开本页重试'
  }, 15000)

  try {
    destroyScanner()
    // 与已通过真机验证的 camera-check.html 相同：先拿流，再绑 video.play()，最后挂识别器。
    recordCameraStep('开始向浏览器请求摄像头权限（默认摄像头）', true)
    stream = await requestCameraStream()
    recordCameraStep(`浏览器已返回视频流；请求${session === startSession ? '有效' : '已取消'}`)
    if (!alive || session !== startSession) return
    if (document.hidden) {
      recordCameraStep('授权返回时仍在后台，关闭视频流', false)
      cameraState.value = 'error'
      cameraHint.value = '页面不在前台，已关闭摄像头。请返回本页后重新打开摄像头'
      return
    }
    await nextTick()
    const video = videoEl.value
    if (!video) throw new Error('[h5/scan] 视频节点未就绪')
    video.srcObject = stream
    try {
      await video.play()
    } catch (playErr) {
      console.error('[h5/scan] 视频播放失败', playErr)
      throw playErr
    }
    recordCameraStep('视频已开始播放')
    cameraState.value = 'scanning'
    startScanLoop(video, session)
    recordCameraStep('识别器已就绪，等待二维码', false)
  } catch (err) {
    recordCameraStep(`启动失败：${err?.name || '未知错误'}；请求${session === startSession ? '有效' : '已取消'}`, session === startSession ? false : diagnosticPending)
    if (!alive || session !== startSession) return
    console.error('[h5/scan] 打开摄像头失败', err)
    destroyScanner()
    cameraState.value = 'error'
    const name = err?.name || ''
    if (name === 'NotAllowedError' || name === 'PermissionDeniedError' || name === 'SecurityError') {
      cameraHint.value = '请在手机应用权限和浏览器网站设置中允许摄像头，然后重试；也可用手机系统浏览器打开本页'
    } else if (name === 'NotFoundError' || name === 'DevicesNotFoundError') {
      cameraHint.value = '未检测到摄像头，请改用手动输入或扫码枪'
    } else if (name === 'NotReadableError' || name === 'TrackStartError') {
      cameraHint.value = '摄像头被占用，请关闭其它应用后重试'
    } else {
      cameraHint.value = '无法打开摄像头，请重试或改用手动输入'
    }
  } finally {
    clearTimeout(timeout)
    // 取消、超时或离开后，迟到的授权只关闭自己的流，不能关闭新一轮摄像头。
    if (stream && (!alive || session !== startSession || cameraState.value !== 'scanning')) {
      stream.getTracks().forEach(track => track.stop())
    }
    if (session === startSession) starting.value = false
  }
}

function cancelStart() {
  recordCameraStep('点击取消打开摄像头', false)
  startSession += 1
  starting.value = false
  destroyScanner()
  cameraState.value = 'idle'
  cameraHint.value = '已取消打开摄像头，可点击按钮重试'
}

function stopCamera({ keepHint = false } = {}) {
  recordCameraStep(`停止摄像头；原状态：${cameraState.value}`, false)
  startSession += 1
  starting.value = false
  destroyScanner()
  if (cameraState.value === 'scanning' || cameraState.value === 'starting') {
    cameraState.value = 'idle'
  }
  if (!keepHint) cameraHint.value = ''
}

function destroyScanner() {
  if (scanTimer) {
    clearInterval(scanTimer)
    scanTimer = null
  }
  try {
    const video = videoEl.value
    if (video?.srcObject) {
      video.srcObject.getTracks().forEach(track => track.stop())
      video.srcObject = null
    }
  } catch (err) {
    console.error('[h5/scan] destroyScanner failed', err)
  }
}

// 主线程纯 JS 解码（jsQR），不创建 Worker/Blob/WASM —— 百度浏览器内核会在扫码库建 Worker 时崩溃。
function startScanLoop(video, session) {
  if (!scanCanvas) {
    scanCanvas = document.createElement('canvas')
    scanCtx = scanCanvas.getContext('2d', { willReadFrequently: true })
  }
  const MAX_DIM = 720 // 降采样到 720 内，兼顾速度与识别率
  const sample = () => {
    if (!alive || session !== startSession || cameraState.value !== 'scanning' || handlingResult) return
    const vw = video.videoWidth
    const vh = video.videoHeight
    if (!vw || !vh) return
    const scale = Math.min(1, MAX_DIM / Math.max(vw, vh))
    const w = Math.max(1, Math.floor(vw * scale))
    const h = Math.max(1, Math.floor(vh * scale))
    if (scanCanvas.width !== w) scanCanvas.width = w
    if (scanCanvas.height !== h) scanCanvas.height = h
    try {
      scanCtx.drawImage(video, 0, 0, w, h)
      const imageData = scanCtx.getImageData(0, 0, w, h)
      const code = jsQR(imageData.data, w, h, { inversionAttempts: 'attemptBoth' })
      if (code && code.data) void onDecode(code.data)
    } catch (err) {
      // 每帧无码是正常情况；仅记录非预期的画布/解码异常。
      recordCameraStep(`识别循环异常：${err?.name || '未知'}`)
      console.error('[h5/scan] 识别循环失败', err)
    }
  }
  sample()
  scanTimer = setInterval(sample, 120)
}

async function onDecode(data) {
  if (!alive || handlingResult || cameraState.value !== 'scanning') return
  handlingResult = true
  const parsed = parseScanPayload(data)
  if (!parsed.ok) {
    showToast(parsed.error)
    handlingResult = false
    return
  }
  stopCamera()
  goReport(parsed.orderNo)
}

function switchToPda() {
  manual.value = false
  stopCamera()
  persistMode('pda')
  nextTick(() => scanInput.value?.focus())
}

function switchToCamera() {
  manual.value = false
  persistMode('camera')
  cameraState.value = 'idle'
  cameraHint.value = ''
}

function holdPdaFocus() {
  nextTick(() => {
    if (!alive || mode.value !== 'pda' || manual.value) return
    scanInput.value?.focus()
  })
}

function onPdaScan() {
  const raw = scanText.value
  scanText.value = ''
  if (!applyScanText(raw)) {
    nextTick(() => scanInput.value?.focus())
  }
}

function openManual() {
  stopCamera()
  manual.value = true
  nextTick(() => manualInput.value?.focus())
}

function closeManual() {
  manual.value = false
  manualText.value = ''
  if (mode.value === 'pda') {
    nextTick(() => scanInput.value?.focus())
  } else {
    cameraState.value = cameraState.value === 'error' ? 'error' : 'idle'
  }
}

function onManual() {
  if (!applyScanText(manualText.value, { fromManual: true })) {
    nextTick(() => manualInput.value?.focus())
  }
}
</script>

<style scoped>
.h5-page {
  padding: 24px var(--h5-page-padding);
  padding-bottom: calc(var(--h5-tabbar-height) + 16px + env(safe-area-inset-bottom));
  min-height: 100vh;
  background: var(--app-bg);
  box-sizing: border-box;
}
h2 { text-align: center; margin: 8px 0 20px; color: var(--app-text-1); font-size: 20px; }
.title-row { display: flex; align-items: center; gap: 8px; }
.camera-diagnostics { margin-bottom: 16px; padding: 12px; border: 1px solid var(--app-primary); border-radius: 8px; background: var(--app-card); color: var(--app-text-1); }
.camera-diagnostics p { margin: 8px 0; font-size: 13px; }
.camera-diagnostics .diag-meta { font-size: 13px; font-weight: 600; color: var(--app-primary); }
.diag-actions { display: flex; gap: 8px; margin-top: 6px; }
.diag-btn {
  flex: 1; height: 34px; border: 1px solid var(--app-primary-border);
  background: var(--app-card); color: var(--app-primary); border-radius: 6px; font-size: 13px;
}
.camera-diagnostics pre { margin: 0; height: 100px; overflow-y: auto; white-space: pre-wrap; overflow-wrap: anywhere; font-size: 12px; line-height: 1.5; }
.stage { position: relative; text-align: center; margin-top: 16px; }
.frame {
  width: 240px; height: 240px; margin: 0 auto; position: relative;
  border-radius: 12px; overflow: hidden; background: var(--app-border-light);
}
.frame.live { background: #000; }
.preview {
  position: absolute; inset: 0; width: 100%; height: 100%;
  object-fit: cover; opacity: 0; pointer-events: none;
}
.preview.show { opacity: 1; }
.corner {
  position: absolute; width: 28px; height: 28px;
  border-color: var(--app-primary); border-style: solid;
}
.tl { top: 12px; left: 12px; border-width: 3px 0 0 3px; }
.tr { top: 12px; right: 12px; border-width: 3px 3px 0 0; }
.bl { bottom: 12px; left: 12px; border-width: 0 0 3px 3px; }
.br { bottom: 12px; right: 12px; border-width: 0 3px 3px 0; }
.wait { margin: 16px 0 0; font-size: 18px; font-weight: 600; color: var(--app-text-1); }
.hint { text-align: center; color: var(--app-text-3); font-size: 14px; margin: 8px 0 20px; }
.hint.warn { color: var(--app-danger, #c0392b); }
.pda-input {
  position: absolute; left: 0; top: 0; width: 100%; height: 100%;
  opacity: 0; border: 0; padding: 0; margin: 0; background: transparent;
}
.manual-btn, .go, .ghost, .linkish {
  display: block; width: 100%; height: var(--h5-control-height);
  border-radius: var(--app-radius-sm); font-size: 16px; margin-top: 10px;
  touch-action: manipulation;
}
.camera-start-btn { position: relative; z-index: 2; }
.manual-btn, .ghost {
  background: var(--app-card); color: var(--app-primary);
  border: 1px solid var(--app-primary-border);
}
.go {
  border: none; background: var(--app-primary); color: var(--app-card);
}
.go:disabled { opacity: 0.6; }
.linkish {
  background: transparent; border: none; color: var(--app-text-3); height: auto;
  margin-top: 8px; padding: 8px 0;
}
.manual { margin-top: 8px; }
.manual-input {
  width: 100%; height: var(--h5-control-height); box-sizing: border-box;
  border: 1px solid var(--app-border); border-radius: var(--app-radius-sm);
  padding: 0 12px; font-size: 16px; background: var(--app-card); color: var(--app-text-1);
}
.manual-actions { display: flex; gap: 10px; margin-top: 10px; }
.manual-actions .ghost, .manual-actions .go { margin-top: 0; }
</style>
