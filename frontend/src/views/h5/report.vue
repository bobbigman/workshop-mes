<template>
  <div class="h5-page">
    <div class="title-row">
      <FactoryBadge />
      <h2 class="order-no">{{ detail?.orderNo || orderNo }}</h2>
      <H5LogoutBtn :dirty="isDirty" />
    </div>
    <p v-if="detail" class="product">
      <template v-if="detail.productCode">{{ detail.productCode }} · </template>{{ detail.productName }}
    </p>

    <div v-if="loadError" class="load-error">
      <p>无法打开该工单，请返回后重试</p>
      <button type="button" class="btn btn-primary" @click="router.replace(reportBackPath)">返回</button>
    </div>

    <div class="summary" v-else-if="detail">
      <div class="summary-row">
        <div>
          <span class="k">计划</span>
          <span class="v">{{ summary.plan }}</span>
        </div>
        <div>
          <span class="k">已完成</span>
          <span class="v">{{ summary.done }}</span>
        </div>
      </div>
      <div class="remain-block">
        <div class="k">剩余可报</div>
        <div class="big">{{ summary.remain }}</div>
      </div>
    </div>

    <div class="guide-block" v-if="detail && !loadError">
      <button type="button" class="guide-toggle" @click="guideVisible = !guideVisible">
        查看作业指导书 / 图纸{{ guides.length ? `（${guides.length}）` : '' }}
      </button>
      <div v-if="guideVisible" class="guide-list">
        <p v-if="guidesLoadError" class="guide-empty err">
          作业指导书加载失败
          <button type="button" class="link-btn" @click="reloadGuides">重试</button>
        </p>
        <p v-else-if="guides.length === 0" class="guide-empty">暂无作业指导书 / 图纸</p>
        <button
          v-for="g in guides"
          :key="g.id"
          type="button"
          class="guide-item"
          @click="openGuide(g)"
        >
          <span class="guide-name">{{ g.fileName }}</span>
          <span class="guide-meta">{{ g.refType === 'operation' ? (g.refName || '工序') : '产品' }}{{ g.version ? ' · ' + g.version : '' }}</span>
        </button>
      </div>
    </div>

    <div v-if="detail && !loadError && noReportableOps" class="no-ops-tip">
      该工单当前没有你可报的工序
    </div>

    <div class="form" v-if="detail && !loadError && !noReportableOps">
      <label>工序</label>
      <select v-model="form.operationId" @change="onOpChange">
        <option value="">请选择工序</option>
        <option v-for="o in (detail?.tasks || [])" :key="o.operationId" :value="String(o.operationId)">
          {{ o.seq }} {{ o.operationName }}
        </option>
      </select>

      <label>本次良品</label>
      <input
        ref="goodInput"
        v-model.number="form.goodQty"
        class="qty-input"
        type="number"
        min="0"
        :max="remainNum"
      />
      <div class="stepper">
        <button type="button" :disabled="!canAdjust" @click="bump(-10)">-10</button>
        <button type="button" :disabled="!canAdjust" @click="bump(-1)">-1</button>
        <button type="button" :disabled="!canAdjust" @click="bump(1)">+1</button>
        <button type="button" :disabled="!canAdjust" @click="bump(10)">+10</button>
      </div>
      <button type="button" class="fill-btn" :disabled="!canAdjust" @click="fillRemain">
        报全部剩余 {{ remainNum }}
      </button>

      <label>不良品</label>
      <input v-model.number="form.defectQty" type="number" min="0" />

      <template v-if="Number(form.defectQty) > 0">
        <label>不良品原因</label>
        <select v-model="form.defectId">
          <option :value="null">请选择</option>
          <option v-for="d in defects" :key="d.id" :value="d.id">{{ d.name }}</option>
        </select>
      </template>

      <template v-if="reportFieldsLoadError">
        <p class="field-err">
          自定义字段加载失败，仍可报工
          <button type="button" class="link-btn" @click="loadReportFields">重试</button>
        </p>
      </template>
      <template v-else-if="reportFields.length">
        <template v-for="f in reportFields" :key="f.id">
          <label>{{ f.fieldName }}</label>
          <input v-if="f.fieldType === 'text'" v-model="form.ext[f.id]" type="text" />
          <input v-else-if="f.fieldType === 'number'" v-model="form.ext[f.id]" type="number" />
          <select v-else-if="f.fieldType === 'single'" v-model="form.ext[f.id]">
            <option value="">请选择</option>
            <option v-for="o in (f.options || [])" :key="o" :value="o">{{ o }}</option>
          </select>
        </template>
      </template>

      <label>报工时长（选填）</label>
      <div class="duration-row">
        <input v-model.number="form.hours" type="number" min="0" max="24" />
        <span>小时</span>
        <input v-model.number="form.minutes" type="number" min="0" max="59" />
        <span>分钟</span>
      </div>

      <label class="share-toggle">
        <input v-model="shareEnabled" type="checkbox" />
        多人共同报工
      </label>
      <div v-if="shareEnabled" class="share-box">
        <p class="share-hint">勾选参与人（须含自己），良品/不良按人数平分，余数归你</p>
        <label v-for="c in candidates" :key="c.id" class="share-person">
          <input type="checkbox" :value="c.id" v-model="participantIds" />
          {{ c.name }}
        </label>
        <p v-if="candidatesLoadError" class="share-hint err">
          参与人列表加载失败
          <button type="button" class="link-btn" @click="reloadCandidates">重试</button>
        </p>
        <p v-else-if="candidates.length === 0" class="share-hint">该工序暂无可选参与人</p>
        <div v-if="sharePreview.length" class="share-preview">
          <div v-for="p in sharePreview" :key="p.userId" class="share-preview-row">
            {{ p.name }}：良品 {{ p.good }} / 不良 {{ p.defect }}
          </div>
        </div>
      </div>

      <p v-if="locked" class="full-tip">该工序已报满，不可继续报工</p>
    </div>

    <div class="action-bar" v-if="detail && !loadError && !noReportableOps">
      <button class="btn btn-outline" :disabled="submitting || locked" @click="onContinue">继续报工</button>
      <button class="btn btn-primary" :disabled="submitting || locked" @click="onSubmit">提交报工</button>
    </div>
    <TabBar active="home" />

    <!-- 内嵌文档预览（PDF/Word）：全屏遮罩层，不跳新窗口、不触发下载 -->
    <div v-if="previewFile" class="doc-overlay">
      <div class="doc-overlay-head">
        <span class="doc-overlay-title">{{ previewFile.fileName }}</span>
        <button type="button" class="doc-overlay-close" @click="closePreview">关闭</button>
      </div>
      <DocPreview :blob="previewFile.blob" :file-type="previewFile.fileType" :file-name="previewFile.fileName" />
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { showToast, showSuccessToast, showImagePreview } from 'vant'
import 'vant/es/toast/style'
import 'vant/es/image-preview/style'
import { getOrderByNo, submitReport, getDefectsByOp, getCustomFields, getReportCandidates } from '@/api/prod'
import { getKnowledgeFileByOrder, getKnowledgeFileRaw } from '@/api/knowledge'
import TabBar from './TabBar.vue'
import FactoryBadge from '@/components/FactoryBadge.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'
import DocPreview from '@/components/DocPreview.vue'
import { getKnowledgeFileType } from '@/utils/knowledgeFile'

const route = useRoute()
const router = useRouter()
const reportBackPath = '/h5/home'
const orderNo = ref(route.query.order || '')
const detail = ref(null)
const loadError = ref(false)
const defects = ref([])
const submitting = ref(false)
const goodInput = ref(null)
const guides = ref([])
const guidesLoadError = ref(false)
const guideVisible = ref(false)
const previewFile = ref(null) // { blob, fileType, fileName }
const reportFields = ref([])
const reportFieldsLoadError = ref(false)
const form = ref({ operationId: '', goodQty: 0, defectQty: 0, defectId: null, hours: 0, minutes: 0, ext: {} })
const shareEnabled = ref(false)
const candidates = ref([])
const candidatesLoadError = ref(false)
const participantIds = ref([])
const pendingClientRequestId = ref('')
const myUserId = Number(localStorage.getItem('userId') || 0) || (() => {
  try {
    const token = localStorage.getItem('token') || ''
    const part = token.split('.')[1]
    if (!part) return 0
    const b64 = part.replace(/-/g, '+').replace(/_/g, '/')
    const pad = b64 + '='.repeat((4 - (b64.length % 4)) % 4)
    const bin = atob(pad)
    const bytes = Uint8Array.from(bin, c => c.charCodeAt(0))
    const payload = JSON.parse(new TextDecoder().decode(bytes))
    return Number(payload.uid || 0)
  } catch {
    return 0
  }
})()

const noReportableOps = computed(() =>
  !!detail.value && !loadError.value && (!(detail.value.tasks || []).length)
)

const isDirty = computed(() => {
  if (!detail.value || loadError.value) return false
  const f = form.value
  return !!(
    f.operationId ||
    (Number(f.goodQty) || 0) > 0 ||
    (Number(f.defectQty) || 0) > 0 ||
    (Number(f.hours) || 0) > 0 ||
    (Number(f.minutes) || 0) > 0 ||
    Object.values(f.ext || {}).some(v => String(v ?? '').trim() !== '')
  )
})

const summary = computed(() => {
  if (!detail.value || !form.value.operationId) {
    return { plan: '—', done: '—', defect: '—', remain: '—' }
  }
  const t = (detail.value.tasks || []).find(x => String(x.operationId) === String(form.value.operationId))
  if (!t) return { plan: '—', done: '—', defect: '—', remain: '—' }
  return { plan: t.planQty, done: t.doneQty, defect: t.defectQty, remain: Math.max(0, t.planQty - t.doneQty) }
})

const remainNum = computed(() => {
  const r = summary.value.remain
  return typeof r === 'number' ? r : 0
})
const locked = computed(() => !!form.value.operationId && remainNum.value <= 0)
const canAdjust = computed(() => !!form.value.operationId && !locked.value)

const sharePreview = computed(() => {
  if (!shareEnabled.value || !participantIds.value.length) return []
  const ids = [...new Set(participantIds.value.map(Number))]
  const n = ids.length
  if (n < 1) return []
  const good = Number(form.value.goodQty) || 0
  const defect = Number(form.value.defectQty) || 0
  const goodBase = Math.floor(good / n)
  const goodRem = good % n
  const defectBase = Math.floor(defect / n)
  const defectRem = defect % n
  return ids.map(id => {
    const c = candidates.value.find(x => x.id === id)
    const isMe = id === myUserId
    return {
      userId: id,
      name: c?.name || String(id),
      good: goodBase + (isMe ? goodRem : 0),
      defect: defectBase + (isMe ? defectRem : 0)
    }
  })
})

watch(() => form.value.defectQty, (n) => {
  if (!(Number(n) > 0)) form.value.defectId = null
})

function clampQty(n) {
  const max = remainNum.value
  if (!Number.isFinite(n) || n < 0) return 0
  return Math.min(max, n)
}

function bump(delta) {
  form.value.goodQty = clampQty((Number(form.value.goodQty) || 0) + delta)
}

function fillRemain() {
  form.value.goodQty = remainNum.value
}

onMounted(async () => {
  if (!orderNo.value) {
    showToast('缺少工单号')
    router.replace(reportBackPath)
    return
  }
  await reloadDetail()
  await tryPreselectOp()
  await loadReportFields()
})

/** 扫工序码带入的 op：合法且在工单工序里 → 预选；否则退回手动选，不报错 */
async function tryPreselectOp() {
  if (!detail.value || loadError.value) return
  const raw = route.query.op
  if (raw == null || String(raw).trim() === '') return
  const id = Number(raw)
  if (!Number.isInteger(id) || id <= 0) return
  const hit = (detail.value.tasks || []).some(t => Number(t.operationId) === id)
  if (!hit) return
  form.value.operationId = String(id)
  await onOpChange()
}

async function loadReportFields() {
  reportFieldsLoadError.value = false
  try {
    const res = await getCustomFields('report')
    reportFields.value = res.data || []
    const ext = { ...form.value.ext }
    for (const f of reportFields.value) {
      if (ext[f.id] === undefined) ext[f.id] = ''
    }
    form.value.ext = ext
  } catch {
    showToast('自定义字段加载失败，仍可正常报工')
    reportFields.value = []
    reportFieldsLoadError.value = true
  }
}

async function reloadGuides() {
  if (!detail.value?.id) return
  guidesLoadError.value = false
  try {
    const g = await getKnowledgeFileByOrder(detail.value.id)
    guides.value = g.data || []
  } catch {
    guides.value = []
    guidesLoadError.value = true
    showToast('作业指导书加载失败')
  }
}

async function reloadDetail() {
  try {
    const res = await getOrderByNo(orderNo.value)
    detail.value = res.data
    loadError.value = false
    if (detail.value && detail.value.id) {
      await reloadGuides()
    }
  } catch {
    // http 拦截器已提示业务/网络错误；此处只提供回扫码入口，不伪装成功
    detail.value = null
    loadError.value = true
  }
}

async function openGuide(file) {
  try {
    const res = await getKnowledgeFileRaw(file.id)
    const blob = res.data
    const fileType = getKnowledgeFileType(file)
    if (['jpg', 'jpeg', 'png', 'webp'].includes(fileType)) {
      const url = URL.createObjectURL(blob)
      showImagePreview([url])
    } else if (fileType === 'pdf' || fileType === 'docx') {
      previewFile.value = { blob, fileType, fileName: file.fileName }
    } else {
      // xlsx 等手机无法预览，触发下载
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = file.fileName || 'file'
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    }
  } catch (error) {
    console.error('[H5 作业指导书] 预览失败', error)
    showToast('预览失败，请稍后重试')
  }
}

function closePreview() {
  previewFile.value = null
}

async function reloadCandidates() {
  candidatesLoadError.value = false
  if (!form.value.operationId) {
    candidates.value = []
    return
  }
  try {
    const c = await getReportCandidates(Number(form.value.operationId))
    candidates.value = c.data || []
    if (myUserId && !participantIds.value.includes(myUserId)) {
      participantIds.value = [myUserId]
    }
  } catch {
    candidates.value = []
    candidatesLoadError.value = true
    showToast('参与人列表加载失败')
  }
}

async function onOpChange() {
  form.value.defectId = null
  participantIds.value = myUserId ? [myUserId] : []
  candidates.value = []
  candidatesLoadError.value = false
  if (!form.value.operationId) { defects.value = []; return }
  const res = await getDefectsByOp(Number(form.value.operationId))
  defects.value = res.data || []
  await reloadCandidates()
}

function validate() {
  if (!form.value.operationId) { showToast('请选择工序'); return false }
  if (form.value.defectQty > 0 && !form.value.defectId) {
    showToast('有不良品时必须选择不良品原因')
    return false
  }
  if (shareEnabled.value) {
    const ids = [...new Set(participantIds.value.map(Number))]
    if (ids.length < 1) { showToast('请勾选参与人'); return false }
    if (myUserId && !ids.includes(myUserId)) {
      showToast('参与人须包含自己'); return false
    }
  }
  const hours = form.value.hours || 0
  const minutes = form.value.minutes || 0
  if (hours < 0 || minutes < 0 || minutes > 59) {
    showToast('时长填写不正确')
    return false
  }
  if (hours * 60 + minutes > 1440) {
    showToast('单次报工时长不能超过24小时')
    return false
  }
  return true
}

function newClientRequestId() {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID()
  return 'r-' + Date.now() + '-' + Math.random().toString(36).slice(2, 10)
}

function buildPayload() {
  if (!pendingClientRequestId.value) pendingClientRequestId.value = newClientRequestId()
  const payload = {
    orderId: detail.value.id,
    operationId: Number(form.value.operationId),
    goodQty: form.value.goodQty || 0,
    defectQty: form.value.defectQty || 0,
    defectId: form.value.defectId,
    durationMinutes: (form.value.hours || 0) * 60 + (form.value.minutes || 0),
    clientRequestId: pendingClientRequestId.value,
    ext: form.value.ext
  }
  if (shareEnabled.value) {
    payload.participantUserIds = [...new Set(participantIds.value.map(Number))]
  }
  return payload
}

function clearQty() {
  form.value.goodQty = 0
  form.value.defectQty = 0
  form.value.defectId = null
  form.value.hours = 0
  form.value.minutes = 0
  pendingClientRequestId.value = ''
}

function reviewLabel(s) {
  if (s === 0) return '待复核'
  if (s === 1) return '已通过'
  if (s === 2) return '已退回'
  return ''
}

function formatReceipt(data) {
  const list = data?.reports || []
  if (!list.length) return '报工成功'
  const unmatched = list.some(r => r.unitPrice == null)
  const miss = unmatched ? ' · 未配工价' : ''
  if (list.length === 1) {
    const r = list[0]
    return `${r.operationName || '工序'} 良品${r.goodQty} 不良${r.defectQty}（${reviewLabel(r.reviewStatus)}）${miss}`
  }
  const good = list.reduce((s, r) => s + (r.goodQty || 0), 0)
  return `已为 ${list.length} 人分摊，合计良品 ${good}（${reviewLabel(list[0].reviewStatus)}）${miss}`
}

async function doSubmit() {
  if (!validate()) return false
  submitting.value = true
  try {
    const res = await submitReport(buildPayload())
    pendingClientRequestId.value = ''
    return res?.data || true
  } catch (e) {
    // 保留 pendingClientRequestId，同一批重试沿用；勿每重试换新标识
    throw e
  } finally {
    submitting.value = false
  }
}

async function onContinue() {
  try {
    const data = await doSubmit()
    if (!data) return
    showSuccessToast(formatReceipt(data === true ? null : data))
    clearQty()
    try {
      await reloadDetail()
    } catch {
      showToast('报工已成功，进度刷新失败，请返回后重进')
    }
    await nextTick()
    goodInput.value?.focus()
  } catch {
    // http 拦截器已提示
  }
}

async function onSubmit() {
  try {
    const data = await doSubmit()
    if (!data) return
    showSuccessToast(formatReceipt(data === true ? null : data))
    router.replace(reportBackPath)
  } catch {
    // http 拦截器已提示
  }
}
</script>

<style scoped>
.h5-page {
  padding: var(--h5-page-padding);
  padding-bottom: calc(var(--h5-tabbar-height) + var(--h5-action-height) + 28px + env(safe-area-inset-bottom));
  min-height: 100vh;
  background: var(--app-bg);
  box-sizing: border-box;
}
.order-no { font-size: 20px; font-weight: 700; margin: 0; color: var(--app-text-1); flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.title-row { display: flex; align-items: center; gap: 8px; margin-bottom: 4px; }
.product { color: var(--app-text-2); font-size: 15px; margin: 0 0 12px; }
.no-ops-tip {
  background: var(--app-card); border-radius: var(--h5-card-radius); padding: 20px var(--h5-card-padding);
  margin-bottom: 16px; text-align: center; color: var(--app-text-2); font-size: 15px;
  box-shadow: var(--app-shadow);
}
.load-error {
  background: var(--app-card); border-radius: var(--h5-card-radius); padding: 20px var(--h5-card-padding);
  margin-bottom: 16px; text-align: center; box-shadow: var(--app-shadow);
}
.load-error p { margin: 0 0 16px; color: var(--app-text-2); font-size: 15px; }
.load-error .btn { width: 100%; height: var(--h5-control-height); }
.summary {
  background: var(--app-card); border-radius: var(--h5-card-radius); padding: var(--h5-card-padding);
  margin-bottom: 16px; box-shadow: var(--app-shadow);
}
.summary-row { display: flex; justify-content: space-between; gap: 12px; }
.summary-row .k, .remain-block .k { font-size: 13px; color: var(--app-text-3); }
.summary-row .v { margin-left: 6px; font-size: 18px; font-weight: 700; color: var(--app-text-1); }
.remain-block { margin-top: 12px; }
.remain-block .big {
  margin-top: 2px; font-size: 40px; font-weight: 700; line-height: 1.1; color: var(--app-primary);
}
.form label { display: block; margin: 14px 0 6px; font-size: 13px; color: var(--app-text-2); }
.form input, .form select {
  width: 100%; height: var(--h5-control-height); border: 1px solid var(--app-border);
  border-radius: var(--app-radius-sm); padding: 0 12px; font-size: 16px;
  box-sizing: border-box; background: var(--app-card); color: var(--app-text-1);
}
.form input.qty-input {
  height: var(--h5-qty-height); font-size: var(--h5-qty-font); font-weight: 700; text-align: center;
}
.stepper { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; margin-top: 10px; }
.stepper button, .fill-btn {
  height: 44px; border: 1px solid var(--app-border); border-radius: var(--app-radius-sm);
  background: var(--app-card); color: var(--app-text-1); font-size: 16px;
}
.stepper button:disabled, .fill-btn:disabled { opacity: .45; }
.fill-btn {
  width: 100%; margin-top: 8px; height: var(--h5-control-height);
  border-color: var(--app-primary-border); background: var(--app-primary-light);
  color: var(--app-primary); font-weight: 600;
}
.duration-row { display: flex; align-items: center; gap: 8px; }
.duration-row input { flex: 1; width: auto; }
.duration-row span { flex: none; font-size: 14px; color: var(--app-text-2); }
.action-bar {
  position: fixed; left: 0; right: 0; z-index: 40;
  bottom: calc(var(--h5-tabbar-height) + env(safe-area-inset-bottom));
  display: flex; gap: 10px; padding: 8px 12px; box-sizing: border-box;
  background: var(--app-card); border-top: 1px solid var(--app-border);
}
.btn {
  flex: 1; height: var(--h5-action-height); border-radius: var(--app-radius-sm); font-size: 16px; border: none;
}
.btn:disabled { opacity: .6; }
.btn-outline { background: var(--app-card); color: var(--app-primary); border: 1.5px solid var(--app-primary); }
.btn-primary { background: var(--app-primary); color: var(--app-card); }
.full-tip { margin: 12px 0 0; font-size: 13px; color: var(--app-warning); text-align: center; }
.share-toggle { display: flex; align-items: center; gap: 8px; margin-top: 4px; font-weight: 600; }
.share-box {
  background: var(--app-bg); border-radius: var(--app-radius-sm); padding: 10px 12px; margin-top: 8px;
}
.share-hint { margin: 0 0 8px; font-size: 12px; color: var(--app-text-3); }
.share-person { display: flex; align-items: center; gap: 8px; padding: 6px 0; font-size: 15px; font-weight: 400; }
.share-preview { margin-top: 8px; padding-top: 8px; border-top: 1px dashed var(--app-border); }
.share-preview-row { font-size: 13px; color: var(--app-text-2); padding: 2px 0; }
.guide-block {
  background: var(--app-card); border-radius: var(--h5-card-radius); padding: var(--h5-card-padding);
  margin-bottom: 16px; box-shadow: var(--app-shadow);
}
.guide-toggle {
  width: 100%; height: 40px; border: none; background: var(--app-primary-light);
  color: var(--app-primary); border-radius: var(--app-radius-sm); font-size: 15px; font-weight: 600;
}
.guide-list { margin-top: 10px; }
.guide-empty { margin: 0; padding: 8px 0; color: var(--app-text-3); font-size: 13px; }
.guide-empty.err { color: #cf1322; }
.link-btn {
  margin-left: 6px; border: none; background: transparent;
  color: var(--app-primary); font-size: inherit; padding: 0; text-decoration: underline;
}
.field-err { margin: 0 0 10px; font-size: 13px; color: #cf1322; }
.share-hint.err { color: #cf1322; }
.guide-item {
  display: flex; flex-direction: column; align-items: flex-start; gap: 2px;
  width: 100%; padding: 10px 0; border: none; border-bottom: 1px solid var(--app-border);
  background: none; text-align: left;
}
.guide-item:last-child { border-bottom: none; }
.guide-name { font-size: 15px; color: var(--app-text-1); }
.guide-meta { font-size: 12px; color: var(--app-text-3); }
.doc-overlay {
  position: fixed; inset: 0; z-index: 1000;
  display: flex; flex-direction: column;
  background: var(--app-card); padding-top: env(safe-area-inset-top);
}
.doc-overlay-head {
  display: flex; align-items: center; justify-content: space-between; gap: 8px;
  padding: 12px var(--h5-card-padding); flex: none;
  border-bottom: 1px solid var(--app-border);
}
.doc-overlay-title { font-size: 15px; font-weight: 600; color: var(--app-text-1); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.doc-overlay-close {
  flex: none; border: none; padding: 6px 16px; border-radius: var(--app-radius-sm);
  background: var(--app-primary-light); color: var(--app-primary); font-size: 14px; font-weight: 600;
}
</style>
