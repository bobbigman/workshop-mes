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

    <div v-if="canWage" class="today-card">
      <div class="today-label">今日计件</div>
      <div class="today-val">今日 {{ todayGoodQty }} 件 / ￥{{ formatMoney(todayWage) }}</div>
      <div class="today-tip">按已复核+待复核报工估算，以工资单为准</div>
    </div>

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
        <option v-for="(o, index) in (detail?.tasks || [])" :key="o.operationId" :value="String(o.operationId)">
          {{ index + 1 }}. {{ o.operationName }}
        </option>
      </select>

      <div v-if="canAssign && currentTask" class="assign-box">
        <div class="assign-head">
          <span>派工：{{ assignLabel }}</span>
          <button type="button" class="link-btn" @click="toggleAssignPanel">
            {{ assignPanelOpen ? '收起' : '派工' }}
          </button>
        </div>
        <div v-if="assignPanelOpen" class="assign-panel">
          <p class="share-hint">个人可多选，或选一个班组（互斥）。清空即取消。派工不拦报工。</p>
          <div class="assign-mode">
            <label><input type="radio" value="person" v-model="assignMode" /> 个人</label>
            <label><input type="radio" value="dept" v-model="assignMode" /> 班组</label>
          </div>
          <template v-if="assignMode === 'person'">
            <div v-if="assignSuggestList.length" class="assign-suggest">
              <div class="assign-suggest-title">推荐派给谁</div>
              <button
                v-for="(s, i) in assignSuggestList"
                :key="s.userId"
                type="button"
                class="assign-suggest-item"
                :class="{ top: i === 0 }"
                @click="pickSuggest(s.userId)"
              >
                <span class="name">{{ s.name }}<template v-if="isSuggestPicked(s.userId)"> · 已选</template></span>
                <span class="meta">近30天 {{ s.reportCount }} 次 · 良品 {{ s.goodQty }}</span>
              </button>
            </div>
            <p v-if="assignSuggestError" class="share-hint err">
              推荐名单加载失败
              <button type="button" class="link-btn" @click="loadAssignSuggest">重试</button>
            </p>
            <label v-for="w in assignWorkersList" :key="w.id" class="share-person">
              <input type="checkbox" :value="w.id" v-model="assignSelectedIds" />
              {{ w.name }}
            </label>
            <p v-if="assignWorkersError" class="share-hint err">
              工人列表加载失败
              <button type="button" class="link-btn" @click="loadAssignWorkers">重试</button>
            </p>
          </template>
          <template v-else>
            <label v-for="d in assignDeptList" :key="d.id" class="share-person">
              <input type="radio" name="assignDept" :value="d.id" v-model="assignSelectedDeptId" />
              {{ d.name }}
            </label>
            <p v-if="!assignDeptList.length && !assignDeptError" class="share-hint">暂无可派班组（需工序已配报工权限部门）</p>
            <p v-if="assignDeptError" class="share-hint err">
              班组列表加载失败
              <button type="button" class="link-btn" @click="loadAssignDepts">重试</button>
            </p>
          </template>
          <button type="button" class="btn btn-primary assign-save" :disabled="assignSaving" @click="saveAssign">
            保存派工
          </button>
        </div>
      </div>

      <label class="share-toggle">
        <input v-model="shareEnabled" type="checkbox" />
        多人共同报工
      </label>
      <div v-if="shareEnabled" class="share-box">
        <p class="share-hint">勾选参与人（须含自己）。按工时填分钟；选平均按良品数平分，旁边是分到的个数。</p>
        <div class="share-mode">
          <label><input type="radio" :value="1" v-model.number="shareMode" /> 平均</label>
          <label><input type="radio" :value="2" v-model.number="shareMode" /> 按工时</label>
        </div>
        <label v-for="c in candidates" :key="c.id" class="share-person">
          <input type="checkbox" :value="c.id" v-model="participantIds" />
          <span class="share-name">{{ c.name }}</span>
          <template v-if="participantIds.map(Number).includes(Number(c.id))">
            <template v-if="shareMode === 2">
              <input
                v-model.number="participantDurations[c.id]"
                class="share-dur"
                type="number"
                min="0"
                max="1440"
              />
              <span class="share-dur-unit">分钟</span>
            </template>
            <span v-else class="share-dur-unit">{{ shareGood(c.id) }} 个</span>
          </template>
        </label>
        <p v-if="candidatesLoadError" class="share-hint err">
          参与人列表加载失败
          <button type="button" class="link-btn" @click="reloadCandidates">重试</button>
        </p>
        <p v-else-if="candidates.length === 0" class="share-hint">该工序暂无可选参与人</p>
        <div v-if="sharePreview.length" class="share-preview">
          <div v-for="p in sharePreview" :key="p.userId" class="share-preview-row">
            {{ p.name }}：良品 {{ p.good }} 个 / 不良 {{ p.defect }} 个
          </div>
        </div>
      </div>

      <label>本次良品</label>
      <input
        ref="goodInput"
        :value="form.goodQty || ''"
        class="qty-input"
        type="text"
        inputmode="none"
        readonly
        aria-label="本次良品数量，点击打开数字键盘"
        placeholder="点击输入数量"
        :disabled="submitting || reviewing || locked"
        @click="qtyKeyboardVisible = true"
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

      <div v-if="form.operationId" class="work-clock">
        <van-button v-if="!workSession" block type="primary" :loading="clockLoading" @click="startClock">开工</van-button>
        <p v-else>作业中… {{ elapsedClock }} <small>提交后结束本次计时</small></p>
        <p v-if="clockError" role="alert">{{ clockError }} <van-button size="mini" @click="refreshClock">重试</van-button></p>
        <p v-if="clockPriceType === 2 && !workSession">计时工资，请先开工</p>
      </div>
      <label>报工时长（未修改则自动计时）</label>
      <div class="duration-row">
        <input v-model.number="form.hours" @input="durationEdited = true" type="number" min="0" max="24" />
        <span>小时</span>
        <input v-model.number="form.minutes" @input="durationEdited = true" type="number" min="0" max="59" />
        <span>分钟</span>
      </div>

      <p v-if="locked" class="full-tip">该工序已报满，不可继续报工</p>
    </div>

    <div class="action-bar" v-if="detail && !loadError && !noReportableOps">
      <button class="btn btn-outline" :disabled="submitting || reviewing || locked || clockBlocked" @click="onContinue">继续报工</button>
      <button class="btn btn-primary" :disabled="submitting || reviewing || locked || clockBlocked" @click="onSubmit">提交报工</button>
    </div>
    <van-number-keyboard :show="qtyKeyboardVisible" title="本次良品数量" close-button-text="完成"
      @blur="qtyKeyboardVisible = false" @input="appendGoodQty" @delete="deleteGoodQty" />
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
import { onUnmounted, ref, computed, watch, onMounted, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { showToast, showSuccessToast, showImagePreview, showConfirmDialog, NumberKeyboard as VanNumberKeyboard } from 'vant'
import 'vant/es/toast/style'
import 'vant/es/image-preview/style'
import 'vant/es/dialog/style'
import 'vant/es/number-keyboard/style'
import { getOrderByNo, submitReport, getDefectsByOp, getCustomFields, getReportCandidates, getTodayPieceSummary } from '@/api/prod'
import { startWorkSession, getCurrentWorkSession } from '@/api/workSession'
import { isUpstreamSoftConfirm } from '@/utils/upstreamConfirm'
import { assign, assignWorkers, assignDepartments, assignSuggest } from '@/api/assign'
import { getKnowledgeFileByOrder, getKnowledgeFileRaw } from '@/api/knowledge'
import TabBar from './TabBar.vue'
import FactoryBadge from '@/components/FactoryBadge.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'
import DocPreview from '@/components/DocPreview.vue'
import { getKnowledgeFileType } from '@/utils/knowledgeFile'
import { canUse, FEATURE, currentTier } from '@/utils/licenseTier'

const route = useRoute()
const router = useRouter()
const reportBackPath = '/h5/home'
const orderNo = ref(route.query.order || '')
const detail = ref(null)
const loadError = ref(false)
const canWage = canUse(currentTier(), FEATURE.PieceWage)
const todayGoodQty = ref(0)
const todayWage = ref(0)

function formatMoney(n) {
  return Number(n || 0).toFixed(2)
}

async function loadTodaySummary() {
  if (!canWage) return
  try {
    const res = await getTodayPieceSummary()
    todayGoodQty.value = res.data?.todayGoodQty ?? 0
    todayWage.value = res.data?.todayWage ?? 0
  } catch (error) {
    console.error('[今日计件] 加载失败', error)
  }
}
const defects = ref([])
const submitting = ref(false)
const reviewing = ref(false)
const qtyKeyboardVisible = ref(false)
const goodInput = ref(null)
const guides = ref([])
const guidesLoadError = ref(false)
const guideVisible = ref(false)
const previewFile = ref(null) // { blob, fileType, fileName }
const reportFields = ref([])
const reportFieldsLoadError = ref(false)
const form = ref({ operationId: '', goodQty: 0, defectQty: 0, defectId: null, hours: 0, minutes: 0, ext: {} })
const workSession = ref(null)
const clockPriceType = ref(0)
const clockLoading = ref(false)
const clockError = ref('')
const clockNow = ref(Date.now())
const clockOffset = ref(0)
const durationEdited = ref(false)
const clockBlocked = computed(() => clockLoading.value || !!clockError.value || (clockPriceType.value === 2 && !workSession.value))
const elapsedClock = computed(() => {
  const seconds = Math.max(0, Math.floor((clockNow.value + clockOffset.value - new Date(workSession.value?.startTime || clockNow.value).getTime()) / 1000))
  return `${Math.floor(seconds / 3600)}小时${Math.floor(seconds % 3600 / 60)}分${seconds % 60}秒`
})
const clockTimer = setInterval(() => { clockNow.value = Date.now() }, 1000)
onUnmounted(() => clearInterval(clockTimer))
let clockRequest = 0
async function refreshClock() {
  const version = ++clockRequest
  workSession.value = null
  clockPriceType.value = 0
  clockError.value = ''
  if (!detail.value?.id || !form.value.operationId) return
  clockLoading.value = true
  try {
    const res = await getCurrentWorkSession({ workOrderId: detail.value.id, operationId: Number(form.value.operationId) })
    if (version !== clockRequest) return
    workSession.value = res.data?.session || null
    clockPriceType.value = res.data?.priceType || 0
    clockOffset.value = new Date(res.data.serverTime).getTime() - Date.now()
  } catch (e) {
    if (version === clockRequest) clockError.value = e?.msg || e?.message || '加载开工记录失败'
  } finally { if (version === clockRequest) clockLoading.value = false }
}
async function startClock() {
  clockLoading.value = true
  try {
    await startWorkSession({ workOrderId: detail.value.id, operationId: Number(form.value.operationId) })
    await refreshClock()
  } catch (e) { showToast(e?.msg || e?.message || '开工失败') }
  finally { clockLoading.value = false }
}
watch(() => [detail.value?.id, form.value.operationId], refreshClock)
const shareEnabled = ref(false)
const shareMode = ref(1)
const candidates = ref([])
const candidatesLoadError = ref(false)
const participantIds = ref([])
const participantDurations = ref({})
const pendingClientRequestId = ref('')
const role = Number(localStorage.getItem('role') || 0)
const canAssign = role === 1 || role === 3
const assignPanelOpen = ref(false)
const assignMode = ref('person')
const assignWorkersList = ref([])
const assignWorkersError = ref(false)
const assignSelectedIds = ref([])
const assignSuggestList = ref([])
const assignSuggestError = ref(false)
const assignDeptList = ref([])
const assignDeptError = ref(false)
const assignSelectedDeptId = ref(null)
const assignSaving = ref(false)
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

const currentTask = computed(() => {
  if (!detail.value || !form.value.operationId) return null
  return (detail.value.tasks || []).find(x => String(x.operationId) === String(form.value.operationId)) || null
})

const assignLabel = computed(() => {
  const t = currentTask.value
  if (!t) return '未派工'
  if (t.assigneeDepartmentId) return t.assigneeName || `班组·${t.assigneeDepartmentName || ''}`
  const list = t.assignees || []
  if (list.length) return list.map(a => a.userName || a.userId).join('、')
  return t.assigneeName || '未派工'
})

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
  const goods = new Array(n).fill(0)
  const defectsArr = new Array(n).fill(0)
  if (shareMode.value === 2) {
    const filled = ids.map(id => Number(participantDurations.value[id] || 0)).filter(d => d > 0)
    if (filled.length === 0) {
      const goodBase = Math.floor(good / n)
      const goodRem = good % n
      const defectBase = Math.floor(defect / n)
      const defectRem = defect % n
      ids.forEach((id, i) => {
        const isMe = id === myUserId
        goods[i] = goodBase + (isMe ? goodRem : 0)
        defectsArr[i] = defectBase + (isMe ? defectRem : 0)
      })
    } else {
      // 未填时长：权重=已填均值（均分兜底拿一份）；落库时长仍为 0，不计时工资
      const avgFilled = Math.max(1, Math.floor(filled.reduce((s, d) => s + d, 0) / filled.length))
      const weights = ids.map(id => {
        const d = Number(participantDurations.value[id] || 0)
        return d > 0 ? d : avgFilled
      })
      const sumW = weights.reduce((s, w) => s + w, 0)
      let gAlloc = 0
      let dAlloc = 0
      let reporterIdx = 0
      ids.forEach((id, i) => {
        goods[i] = Math.floor((good * weights[i]) / sumW)
        defectsArr[i] = Math.floor((defect * weights[i]) / sumW)
        gAlloc += goods[i]
        dAlloc += defectsArr[i]
        if (id === myUserId) reporterIdx = i
      })
      goods[reporterIdx] += good - gAlloc
      defectsArr[reporterIdx] += defect - dAlloc
    }
  } else {
    const goodBase = Math.floor(good / n)
    const goodRem = good % n
    const defectBase = Math.floor(defect / n)
    const defectRem = defect % n
    ids.forEach((id, i) => {
      const isMe = id === myUserId
      goods[i] = goodBase + (isMe ? goodRem : 0)
      defectsArr[i] = defectBase + (isMe ? defectRem : 0)
    })
  }
  return ids.map((id, i) => {
    const c = candidates.value.find(x => x.id === id)
    return {
      userId: id,
      name: c?.name || (id === myUserId ? (localStorage.getItem('userName') || '我') : String(id)),
      good: goods[i],
      defect: defectsArr[i]
    }
  })
})

function shareGood(id) {
  const hit = sharePreview.value.find(p => p.userId === Number(id))
  return hit ? hit.good : 0
}

watch(() => form.value.defectQty, (n) => {
  if (!(Number(n) > 0)) form.value.defectId = null
})

function appendGoodQty(key) {
  if (submitting.value || reviewing.value || locked.value || !/^[0-9]$/.test(String(key))) return
  const n = Number(`${Number(form.value.goodQty) || ''}${key}`)
  if (!Number.isSafeInteger(n) || n > 2147483647) { showToast('数量过大，请核对'); return }
  form.value.goodQty = n
}
function deleteGoodQty() {
  if (submitting.value || reviewing.value) return
  form.value.goodQty = Number(String(form.value.goodQty || '').slice(0, -1)) || 0
}

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
  loadTodaySummary()
  await reloadDetail()
  await tryPreselectOp()
  await tryOpenAssignFromQuery()
  await loadReportFields()
  if (canAssign) await loadAssignWorkers()
})

/** 扫工序码带入的 op：合法且在工单工序里 → 预选；否则退回手动选，不报错 */
async function tryPreselectOp() {
  if (!detail.value || loadError.value) return
  if (route.query.task != null) {
    const task = (detail.value.tasks || []).find(t => String(t.id) === String(route.query.task))
    if (!task) return
    form.value.operationId = String(task.operationId)
    await onOpChange()
    return
  }
  const raw = route.query.op
  if (raw == null || String(raw).trim() === '') return
  const id = Number(raw)
  if (!Number.isInteger(id) || id <= 0) return
  const hit = (detail.value.tasks || []).some(t => Number(t.operationId) === id)
  if (!hit) return
  form.value.operationId = String(id)
  await onOpChange()
}

/** 工单列表「派工」入口：?assign=1 时预选首道可派工序并展开派工面板 */
async function tryOpenAssignFromQuery() {
  if (!canAssign || !detail.value || loadError.value) return
  if (String(route.query.assign || '') !== '1') return
  if (!form.value.operationId) {
    const tasks = (detail.value.tasks || []).filter(t => t.id)
    const prefer = tasks.find(t => !(t.assignees || []).length && !t.assigneeUserId && !t.assigneeDepartmentId) || tasks[0]
    if (prefer) {
      form.value.operationId = String(prefer.operationId)
      await onOpChange()
    }
  }
  if (form.value.operationId) {
    syncAssignSelection()
    assignPanelOpen.value = true
    await loadAssignDepts()
    await loadAssignSuggest()
  }
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
  participantDurations.value = {}
  candidates.value = []
  candidatesLoadError.value = false
  assignPanelOpen.value = false
  syncAssignSelection()
  assignDeptList.value = []
  assignSuggestList.value = []
  assignSuggestError.value = false
  if (!form.value.operationId) { defects.value = []; return }
  const res = await getDefectsByOp(Number(form.value.operationId))
  defects.value = res.data || []
  await reloadCandidates()
}

function syncAssignSelection() {
  const t = currentTask.value
  if (!t) {
    assignSelectedIds.value = []
    assignSelectedDeptId.value = null
    assignMode.value = 'person'
    return
  }
  if (t.assigneeDepartmentId) {
    assignMode.value = 'dept'
    assignSelectedDeptId.value = t.assigneeDepartmentId
    assignSelectedIds.value = []
    return
  }
  assignMode.value = 'person'
  assignSelectedDeptId.value = null
  const list = t.assignees || []
  if (list.length) {
    assignSelectedIds.value = list.map(a => a.userId)
  } else if (t.assigneeUserId) {
    assignSelectedIds.value = [t.assigneeUserId]
  } else {
    assignSelectedIds.value = []
  }
}

async function toggleAssignPanel() {
  assignPanelOpen.value = !assignPanelOpen.value
  if (assignPanelOpen.value) {
    await loadAssignWorkers()
    await loadAssignDepts()
    await loadAssignSuggest()
  }
}

function isSuggestPicked(userId) {
  return assignSelectedIds.value.map(Number).includes(Number(userId))
}

function pickSuggest(userId) {
  const worker = assignWorkersList.value.find(w => Number(w.id) === Number(userId))
  const id = worker ? worker.id : Number(userId)
  if (isSuggestPicked(id)) return
  assignSelectedIds.value = [...assignSelectedIds.value, id]
}

async function loadAssignSuggest() {
  if (!canAssign) return
  const t = currentTask.value
  if (!t?.id) {
    assignSuggestList.value = []
    return
  }
  assignSuggestError.value = false
  try {
    const res = await assignSuggest(t.id)
    assignSuggestList.value = res.data || []
  } catch {
    assignSuggestList.value = []
    assignSuggestError.value = true
  }
}

async function loadAssignWorkers() {
  if (!canAssign) return
  assignWorkersError.value = false
  try {
    const res = await assignWorkers()
    assignWorkersList.value = res.data || []
  } catch {
    assignWorkersList.value = []
    assignWorkersError.value = true
  }
}

async function loadAssignDepts() {
  if (!canAssign) return
  const t = currentTask.value
  if (!t?.id) {
    assignDeptList.value = []
    return
  }
  assignDeptError.value = false
  try {
    const res = await assignDepartments(t.id)
    assignDeptList.value = res.data || []
  } catch {
    assignDeptList.value = []
    assignDeptError.value = true
  }
}

async function saveAssign() {
  const t = currentTask.value
  if (!t?.id) {
    showToast('该工序暂无可派工任务行')
    return
  }
  assignSaving.value = true
  try {
    if (assignMode.value === 'dept') {
      const deptId = assignSelectedDeptId.value ? Number(assignSelectedDeptId.value) : null
      await assign(t.id, [], deptId || null)
      showSuccessToast(deptId ? '已派给班组' : '已取消派工')
    } else {
      const ids = [...new Set(assignSelectedIds.value.map(Number).filter(x => x > 0))]
      await assign(t.id, ids)
      showSuccessToast(ids.length ? '已派工' : '已取消派工')
    }
    const res = await getOrderByNo(detail.value.orderNo)
    detail.value = res.data
    syncAssignSelection()
    assignPanelOpen.value = false
  } catch (e) {
    showToast(e?.msg || e?.message || '派工失败')
  } finally {
    assignSaving.value = false
  }
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
    sessionId: workSession.value?.id || null,
    durationMinutes: durationEdited.value ? (form.value.hours || 0) * 60 + (form.value.minutes || 0) : null,
    clientRequestId: pendingClientRequestId.value,
    ext: form.value.ext
  }
  if (shareEnabled.value) {
    payload.shareMode = shareMode.value === 2 ? 2 : 1
    const ids = [...new Set(participantIds.value.map(Number))]
    if (shareMode.value === 2) {
      payload.participants = ids.map(id => ({
        userId: id,
        durationMinutes: participantDurations.value[id] === undefined || participantDurations.value[id] === '' ? null : Number(participantDurations.value[id])
      }))
    } else {
      payload.participantUserIds = ids
    }
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
  durationEdited.value = false
  refreshClock()
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

async function doSubmit(confirmUpstream = false) {
  if (submitting.value || !validate()) return false
  submitting.value = true
  try {
    const res = await submitReport({ ...buildPayload(), confirmUpstream: !!confirmUpstream })
    pendingClientRequestId.value = ''
    return res?.data || true
  } catch (e) {
    // 保留 pendingClientRequestId，同一批重试沿用；勿每重试换新标识
    throw e
  } finally {
    submitting.value = false
  }
}

/** docs/150：soft 先提交；code=1002 弹确认后带 confirmUpstream 重提 */
async function doSubmitWithUpstreamConfirm() {
  if (submitting.value || reviewing.value) return false
  qtyKeyboardVisible.value = false
  {
    if (!validate()) return false
    const snapshot = JSON.stringify(form.value)
    reviewing.value = true
    try {
      await showConfirmDialog({ title: '核对本次报工', message: `${detail.value.orderNo} · ${detail.value.productName}\n${currentTask.value?.operationName || ''}\n良品 ${form.value.goodQty} 件，不良 ${form.value.defectQty || 0} 件`, confirmButtonText: '确认提交' })
      if (snapshot !== JSON.stringify(form.value)) { showToast('内容已变化，请重新核对'); return false }
    } finally { reviewing.value = false }
  }
  try {
    return await doSubmit(false)
  } catch (e) {
    if (!isUpstreamSoftConfirm(e)) throw e
    await showConfirmDialog({
      title: '上级工序提示',
      message: e.message || '紧邻上一道复核通过量不足，是否继续报工？'
    })
    return await doSubmit(true)
  }
}

async function onContinue() {
  try {
    const data = await doSubmitWithUpstreamConfirm()
    if (!data) return
    showSuccessToast(formatReceipt(data === true ? null : data))
    clearQty()
    loadTodaySummary()
    try {
      await reloadDetail()
    } catch {
      showToast('报工已成功，进度刷新失败，请返回后重进')
    }
    await nextTick()
    goodInput.value?.focus()
  } catch {
    // soft 取消或 http 已提示
  }
}

async function onSubmit() {
  try {
    const data = await doSubmitWithUpstreamConfirm()
    if (!data) return
    showSuccessToast(formatReceipt(data === true ? null : data))
    router.replace(reportBackPath)
  } catch {
    // soft 取消或 http 已提示
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
.today-card {
  background: var(--app-card); border-radius: var(--h5-card-radius); padding: 12px 14px;
  margin-bottom: 12px; box-shadow: var(--app-shadow);
}
.today-label { font-size: 12px; color: var(--app-text-3); margin-bottom: 2px; }
.today-val { font-size: 17px; font-weight: 700; color: var(--app-primary); }
.today-tip { margin-top: 4px; font-size: 11px; color: var(--app-text-4); }
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
.form input:not([type="checkbox"]):not([type="radio"]),
.form select {
  width: 100%; height: var(--h5-control-height); border: 1px solid var(--app-border);
  border-radius: var(--app-radius-sm); padding: 0 12px; font-size: 16px;
  box-sizing: border-box; background: var(--app-card); color: var(--app-text-1);
}
.form input[type="checkbox"],
.form input[type="radio"] {
  width: 20px; height: 20px; min-width: 20px; margin: 0; padding: 0; flex: none;
  accent-color: var(--app-primary);
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
.form label.share-toggle { display: flex; align-items: center; gap: 8px; margin-top: 14px; font-size: 16px; font-weight: 600; color: var(--app-text-1); }
.form input.share-dur { width: 72px; height: 32px; flex: none; padding: 0 6px; }
.share-box {
  background: var(--app-bg); border-radius: var(--app-radius-sm); padding: 10px 12px; margin-top: 8px;
}
.share-mode { display: flex; gap: 16px; margin-bottom: 8px; font-size: 14px; }
.form .share-mode label { display: flex; align-items: center; gap: 4px; margin: 0; font-size: 14px; font-weight: 400; color: var(--app-text-1); }
.share-hint { margin: 0 0 8px; font-size: 12px; color: var(--app-text-3); }
.form label.share-person { display: flex; align-items: center; gap: 8px; margin: 0; padding: 6px 0; font-size: 15px; font-weight: 400; color: var(--app-text-1); flex-wrap: wrap; }
.share-name { flex: 1; min-width: 4em; }
.share-dur { width: 72px; height: 32px; padding: 0 6px; border: 1px solid var(--app-border); border-radius: 6px; }
.share-dur-unit { flex: none; font-size: 15px; color: var(--app-text-1); }
.share-preview { margin-top: 8px; padding-top: 8px; border-top: 1px dashed var(--app-border); }
.share-preview-row { font-size: 13px; color: var(--app-text-2); padding: 2px 0; }
.assign-box {
  background: var(--app-bg); border-radius: var(--app-radius-sm); padding: 10px 12px; margin: 8px 0 12px;
}
.assign-head { display: flex; align-items: center; justify-content: space-between; gap: 8px; font-size: 14px; }
.assign-panel { margin-top: 8px; }
.assign-suggest { margin: 0 0 8px; }
.assign-suggest-title { font-size: 12px; color: var(--app-text-3); margin-bottom: 4px; }
.assign-suggest-item {
  display: block; width: 100%; text-align: left; margin: 0 0 6px; padding: 8px 10px;
  border: 1px solid var(--app-border); border-radius: 8px; background: var(--app-card);
  color: inherit; font: inherit;
}
.assign-suggest-item.top { border-color: var(--app-primary); background: var(--app-primary-light); }
.assign-suggest-item .name { display: block; font-size: 15px; font-weight: 600; }
.assign-suggest-item.top .name { color: var(--app-primary); }
.assign-suggest-item .meta { display: block; margin-top: 2px; font-size: 12px; color: var(--app-text-3); }
.assign-mode { display: flex; gap: 16px; margin-bottom: 8px; font-size: 14px; color: var(--app-text-2); }
.assign-save { width: 100%; height: 40px; margin-top: 8px; }
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
