<template>
  <div class="ai-page">
    <ValueTip :text="VALUE_TIPS.aiAssistant" />
    <div class="ai-toolbar">
      <div class="role-tabs" aria-label="选择提问角色">
        <button v-for="r in ROLES" :key="r.value" type="button" class="role-tab"
          :class="{ active: selectedRole === r.value }" :disabled="busy"
          :aria-expanded="selectedRole === r.value && questionsOpen" aria-controls="role-questions"
          @click="onRoleClick(r.value)">
          {{ r.label }}<span class="role-chevron" :class="{ expanded: selectedRole === r.value && questionsOpen }" aria-hidden="true">⌄</span>
        </button>
      </div>
      <div class="ai-status">
        <el-tag v-if="!status.enabled" type="info">已关闭</el-tag>
        <el-tag v-else-if="!status.configured" type="warning">未配置</el-tag>
        <el-tag v-else type="success">已就绪</el-tag>
        <el-button size="small" :disabled="busy" @click="onNewChat">新对话</el-button>
      </div>
    </div>


    <el-alert v-if="banner" :type="bannerType" show-icon :closable="false" :title="banner" class="mb" />

    <section v-if="questionsOpen" id="role-questions" class="question-panel" aria-label="常用问题" @keydown.esc="questionsOpen = false">
      <div class="question-panel-heading"><span>想了解什么？</span><el-button text size="small" @click="questionsOpen = false">收起</el-button></div>
      <div class="question-grid">
        <button v-for="(q, index) in examples" :key="q" type="button" class="question-item"
          :disabled="busy || !canChat" @click="useExample(q)">
          <span class="question-number">{{ String(index + 1).padStart(2, '0') }}</span><span>{{ q }}</span><span class="question-arrow" aria-hidden="true">↗</span>
        </button>
      </div>
    </section>

    <div ref="chatRef" class="chat">
      <div v-for="(m, i) in messages" :key="i" class="bubble" :class="m.role">
        <div class="bubble-role"><span v-if="m.role === 'assistant'" class="assistant-avatar">AI</span>{{ m.role === 'user' ? '我' : '小工单助手' }}</div>
        <div class="bubble-text" :class="{ 'is-md': m.role === 'assistant' }">
          <AiAnswer v-if="m.role === 'assistant'" :text="presentAiAnswer(m.text, m.sources)" />
          <template v-else>{{ m.text }}</template>
        </div>

        <div v-if="m.candidates?.length" class="block">
          <div class="block-title">请选择</div>
          <el-button
            v-for="(c, idx) in m.candidates"
            :key="idx"
            size="small"
            class="cand"
            :disabled="busy"
            @click="onSelectCandidate(c)"
          >
            <template v-if="c.type === 'product'">{{ c.productCode }} · {{ c.productName }}</template>
            <template v-else>工序 {{ c.operationCode || c.operationId }} · {{ c.operationName }}</template>
          </el-button>
        </div>

        <details v-if="m.workOrders" class="block work-order-data" :open="!hasAnswerTable(m.text)">
          <summary>工单原始数据 · 共 {{ m.workOrders.total }} 条<span class="muted"> · 查看详情与翻页</span></summary>
          <div class="block-title">
            共 {{ m.workOrders.total }} 条
            <span v-if="filterLabel(m.workOrders)" class="muted">（{{ filterLabel(m.workOrders) }}）</span>
            <span class="muted"> · 查询时间 {{ m.queriedAt }}</span>
          </div>
          <el-table :data="m.workOrders.list" size="small" border>
            <el-table-column prop="orderNo" label="工单号" min-width="120" />
            <el-table-column prop="productCode" label="产品编号" width="110" />
            <el-table-column prop="productName" label="产品" min-width="120" />
            <el-table-column prop="qty" label="数量" width="70" />
            <el-table-column prop="doneQty" label="完成" width="70" />
            <el-table-column label="交期" width="110">
              <template #default="{ row }">{{ formatDate(row.dueDate) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="80">
              <template #default="{ row }">{{ statusText(row.status) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="80" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" @click="openOrder(row.id)">详情</el-button>
              </template>
            </el-table-column>
          </el-table>
          <div class="pager" v-if="m.workOrders.total > m.workOrders.pageSize">
            <el-pagination
              layout="prev, pager, next"
              :total="m.workOrders.total"
              :page-size="m.workOrders.pageSize"
              :current-page="m.workOrders.page"
              @current-change="(p) => onPage(m, p)"
            />
          </div>
        </details>

        <div v-if="m.reportEvidence" class="block">
          <div class="block-title">
            报工证据 · {{ m.reportEvidence.order.orderNo }}（{{ m.reportEvidence.order.productCode }} {{ m.reportEvidence.order.productName }}）
            <span class="muted"> · 状态 {{ statusText(m.reportEvidence.order.status) }}</span>
            <span class="muted"> · 查询 {{ formatDateTime(m.reportEvidence.order.queriedAt) }}</span>
            <el-button link type="primary" size="small" :disabled="busy" @click="openOrder(m.reportEvidence.order.id)">详情</el-button>
          </div>

          <el-alert v-if="m.reportEvidence.note" type="info" :closable="false" :title="m.reportEvidence.note" class="mb" />

          <el-card shadow="never" class="src">
            <div class="block-title">
              整单累计：计划 {{ m.reportEvidence.totals.qty }} · 完成 {{ m.reportEvidence.totals.doneQty }}
            </div>
            <el-table :data="m.reportEvidence.totals.tasks" size="small" border>
              <el-table-column prop="operationName" label="工序" min-width="120" />
              <el-table-column prop="planQty" label="计划" width="70" />
              <el-table-column prop="doneQty" label="有效良品" width="90" />
              <el-table-column prop="defectQty" label="有效不良" width="90" />
            </el-table>
          </el-card>

          <el-card shadow="never" class="src">
            <div class="block-title">筛选内按工序汇总（良品：待复核 / 已通过 / 已退回）</div>
            <el-table :data="m.reportEvidence.summary" size="small" border>
              <el-table-column prop="operationName" label="工序" min-width="110" />
              <el-table-column label="待复核良" width="84">
                <template #default="{ row }">{{ row.pendingGood }}</template>
              </el-table-column>
              <el-table-column label="已通过良" width="84">
                <template #default="{ row }">{{ row.approvedGood }}</template>
              </el-table-column>
              <el-table-column label="已退回良" width="84">
                <template #default="{ row }">{{ row.rejectedGood }}</template>
              </el-table-column>
              <el-table-column label="待复核不良" width="90">
                <template #default="{ row }">{{ row.pendingDefect }}</template>
              </el-table-column>
              <el-table-column label="已通过不良" width="90">
                <template #default="{ row }">{{ row.approvedDefect }}</template>
              </el-table-column>
              <el-table-column label="已退回不良" width="90">
                <template #default="{ row }">{{ row.rejectedDefect }}</template>
              </el-table-column>
              <el-table-column label="进度口径" width="84">
                <template #default="{ row }">{{ row.progressGood }}</template>
              </el-table-column>
              <el-table-column label="通过口径" width="84">
                <template #default="{ row }">{{ row.reportGood }}</template>
              </el-table-column>
            </el-table>
          </el-card>

          <div class="block-title">
            报工明细 共 {{ m.reportEvidence.total }} 条
            <span v-if="m.reportEvidence.filter" class="muted">
              （{{ m.reportEvidence.filter.operationName || '全部工序' }}）
            </span>
          </div>
          <el-table :data="m.reportEvidence.list" size="small" border>
            <el-table-column label="报工时间" width="150">
              <template #default="{ row }">{{ formatDateTime(row.reportTime) }}</template>
            </el-table-column>
            <el-table-column prop="operationName" label="工序" min-width="100" />
            <el-table-column prop="userName" label="报工人" width="90" />
            <el-table-column prop="goodQty" label="良品" width="70" />
            <el-table-column prop="defectQty" label="不良" width="70" />
            <el-table-column prop="defectName" label="不良品名" width="110">
              <template #default="{ row }">{{ row.defectName || '—' }}</template>
            </el-table-column>
            <el-table-column label="复核" width="80">
              <template #default="{ row }">{{ reviewText(row.reviewStatus) }}</template>
            </el-table-column>
            <el-table-column prop="rejectReason" label="退回原因" min-width="110">
              <template #default="{ row }">{{ row.rejectReason || '—' }}</template>
            </el-table-column>
            <el-table-column prop="batchNo" label="批次" width="100">
              <template #default="{ row }">{{ row.batchNo || '—' }}</template>
            </el-table-column>
          </el-table>
          <div class="pager" v-if="m.reportEvidence.total > m.reportEvidence.pageSize">
            <el-pagination
              layout="prev, pager, next"
              :total="m.reportEvidence.total"
              :page-size="m.reportEvidence.pageSize"
              :current-page="m.reportEvidence.page"
              @current-change="(p) => onEvPage(m, p)"
            />
          </div>
        </div>

        <div v-if="visibleAiSources(m.sources).length" class="block">
          <div class="block-title">来源</div>
          <el-card v-for="s in visibleAiSources(m.sources)" :key="s.sourceId" shadow="never" class="src">
            <div>
              <b>{{ s.title }}</b>
              <span v-if="s.chapter" class="muted"> · {{ s.chapter }}</span>
              <span class="muted"> · {{ s.version || '未标注版本' }}</span>
              <span v-if="s.updatedAt" class="muted"> · 更新 {{ s.updatedAt }}</span>
              <span class="muted"> · {{ s.sourceId }}</span>
            </div>
            <div class="excerpt">{{ s.excerpt }}</div>
          </el-card>
        </div>

        <div v-if="m.warnings?.length" class="warn">
          <div v-for="(w, wi) in m.warnings" :key="wi">{{ w }}</div>
        </div>
      </div>
    </div>

    <div class="composer">
      <el-input
        v-model="input"
        type="textarea"
        :rows="3"
        maxlength="2000"
        :disabled="!canChat || busy"
        placeholder="想了解什么？直接问，或点击上方角色选择问题"
        @keydown.ctrl.enter="onSend"
      />
      <div class="composer-footer">
        <span class="composer-hint">{{ busy ? '正在回答，可随时停止' : 'Ctrl + Enter 发送' }}</span>
        <el-button v-if="busy" class="stop-button" @click="onStop"><span class="stop-icon" aria-hidden="true"></span>停止生成</el-button>
        <el-button v-else type="primary" :disabled="!canChat || !input.trim()" @click="onSend">发送 <span aria-hidden="true">↑</span></el-button>
      </div>
    </div>

    <el-dialog v-model="detailDlg" title="工单详情" width="620px" destroy-on-close>
      <template v-if="detail">
        <p>单号：{{ detail.orderNo }} · {{ detail.productCode }} {{ detail.productName }}</p>
        <p>数量：{{ detail.qty }} · 状态：{{ statusText(detail.status) }} · 交期：{{ formatDate(detail.dueDate) }}</p>
        <el-table :data="detail.tasks || []" size="small" border>
          <el-table-column prop="operationName" label="工序" />
          <el-table-column prop="planQty" label="计划" width="80" />
          <el-table-column prop="doneQty" label="良品" width="80" />
          <el-table-column prop="defectQty" label="不良" width="80" />
        </el-table>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useRouter } from 'vue-router'
import {
  deleteAiConversation,
  getAiStatus,
  searchAiReportEvidence,
  searchAiWorkOrders,
  sendAiMessage
} from '@/api/aiAssistant'
import { getOrder } from '@/api/prod'
import AiAnswer from '@/components/AiAnswer.vue'
import { hasAnswerTable, formatAiBusinessDate } from '@/utils/aiWorkOrderPresentation'
import { presentAiAnswer, visibleAiSources } from '@/utils/aiAssistantPresentation'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'

const router = useRouter()
const role = Number(localStorage.getItem('role') || 0)
if (role !== 1) {
  ElMessage.error('仅管理员可使用 AI 助手')
  router.replace('/order')
}

const ROLES = [{ value: 'boss', label: '老板' }, { value: 'supervisor', label: '车间主管' }, { value: 'finance', label: '财务' }, { value: 'worker', label: '车间师傅' }, { value: 'engineer', label: '电脑部门' }]
const selectedRole = ref('boss')
const questionsOpen = ref(false)
const mode = computed(() => selectedRole.value === 'boss' ? 'progress' : 'support')
const status = ref({ enabled: false, configured: false, model: null })
const conversationId = ref('')
const input = ref('')
const messages = ref([])
const busy = ref(false)
const banner = ref('')
const bannerType = ref('info')
const chatRef = ref(null)
let abortCtrl = null

const detailDlg = ref(false)
const detail = ref(null)

const QUESTIONS = {
  boss: [
    '今天要交货的工单有哪几张？',
    '明天就要交的货，进度到哪了？',
    '现在有哪几张单已经逾期了？逾期多久？',
    '客户催货时，执行中的单里哪几张剩得最多？',
    '刚下的单怎么到现在还没人动？',
    '现在车间一共在做多少张单？',
    '临期 3 天内要交的单有哪些？',
    '被取消的工单有哪些？',
    '客户在微信上突然问起某单，我在手机上怎么一秒查到？',
    '做完的工单系统会自动结束吗？'
  ],
  supervisor: [
    '今天到期的单有几张在赶？',
    '逾期最久的是哪张？卡在哪道工序？',
    '现在有几条设备异常还没处理？',
    '拖最久的那条异常是谁报的、谁在修？',
    '工人不会用手机扫码报工怎么办？',
    '报工被退回了，工人来找我闹怎么办？',
    '待复核的报工我该怎么处理？',
    '工人说自己报错数了，能自己改吗？',
    '班组长在手机上派工，工人那边能看到吗？',
    '超报被系统硬拦了，我怎么放行？'
  ],
  finance: [
    '工单计划数和完成数对不上，差在哪？',
    '不良品会不会被算进完成数里？',
    '客户投诉了，这活是谁做的、什么时候做的？',
    '返工重做的件，算不算产量？',
    '两个人合作做一批，数量怎么分？',
    '临时工和正式工，系统里怎么区分标签？',
    '交期被改过，我在哪看记录？',
    '报工记录能导出 Excel 带去对账吗？',
    '被退回的报工，会不会被算进完成数里？',
    '月底对账时，良品+不良合计怎么对得上计划数？'
  ],
  worker: [
    '工人怎么用手机扫码报工？',
    'PDA 扫码枪怎么用？',
    '扫了二维码打不开页面怎么办？',
    '报错数量了，我自己能改吗？',
    '不良品怎么报？必须填什么？',
    '一道活分三次做，能分三次报吗？',
    '班组长派给我的活，手机上在哪看？',
    '没智能手机的工人怎么报工？',
    '设备坏了我要怎么上报？',
    '怎么看这道工序的作业指导书？'
  ],
  engineer: [
    '电脑部门怎么结合 WorkBuddy，给各部门做系统培训？',
    '能把现有操作说明整理成新员工培训材料吗？',
    '同一套系统，老板、主管、财务和工人分别怎么培训？',
    '怎么把操作步骤整理成图文教程？',
    '员工总问同样的问题，怎么整理成内部常见问题？',
    '系统功能更新后，怎么整理本次培训重点？',
    '怎么用演示工单，让员工边操作边学？',
    '能根据培训内容，准备练习题和操作检查表吗？',
    '各部门的 Excel 资料，怎么整理成统一格式？',
    '内部资料交给 AI 前，哪些内容需要先处理？'
  ]
}

const examples = computed(() => QUESTIONS[selectedRole.value])

const canChat = computed(() => status.value.enabled && status.value.configured)

onMounted(async () => {
  await refreshStatus()
})

async function refreshStatus() {
  try {
    const res = await getAiStatus()
    status.value = res.data || { enabled: false, configured: false }
    if (!status.value.enabled) {
      banner.value = 'AI 助手已关闭。不影响下单与报工。需要时请联系部署人员开启。'
      bannerType.value = 'info'
    } else if (!status.value.configured) {
      banner.value = 'AI 未配置完成（缺密钥或模型）。请联系部署人员在服务器配置后重启。'
      bannerType.value = 'warning'
    } else {
      banner.value = ''
    }
  } catch (e) {
    banner.value = e.message || '无法获取 AI 状态'
    bannerType.value = 'error'
  }
}

function useExample(q) {
  if (!canChat.value || busy.value) return
  questionsOpen.value = false
  input.value = q
  return onSend()
}

async function onNewChat() {
  const oldConversationId = conversationId.value
  conversationId.value = ''
  messages.value = []
  input.value = ''
  abortCtrl?.abort()
  busy.value = false
  if (oldConversationId) {
    try {
      await deleteAiConversation(oldConversationId)
    } catch {
      ElMessage.warning('旧会话清理失败，已开始新对话')
    }
  }
}

function onRoleClick(value) {
  if (busy.value) return
  if (selectedRole.value === value) {
    questionsOpen.value = !questionsOpen.value
    return
  }
  selectedRole.value = value
  onRoleChange()
  questionsOpen.value = true
}

let previousMode = mode.value
function onRoleChange() {
  if (mode.value !== previousMode) onNewChat()
  previousMode = mode.value
}

async function onSend() {
  if (!canChat.value || busy.value) return
  const text = input.value.trim()
  if (!text) return

  questionsOpen.value = false
  messages.value.push({ role: 'user', text })
  input.value = ''
  busy.value = true
  abortCtrl = new AbortController()
  await scrollBottom()

  try {
    const res = await sendAiMessage(
      {
        conversationId: conversationId.value || undefined,
        mode: mode.value,
        message: text
      },
      abortCtrl.signal
    )
    applyAssistant(res.data)
  } catch (e) {
    if (e.name === 'CanceledError' || e.code === 'ERR_CANCELED') {
      messages.value.push({ role: 'assistant', text: '已停止。' })
    } else {
      messages.value.push({ role: 'assistant', text: e.message || '请求失败' })
    }
  } finally {
    busy.value = false
    abortCtrl = null
    await scrollBottom()
  }
}

function onStop() {
  abortCtrl?.abort()
}

async function onSelectCandidate(c) {
  if (busy.value) return
  const label =
    c.type === 'product'
      ? `选择产品 ${c.productName || ('编号 ' + c.productCode)}`
      : `选择工序 ${c.operationName}`
  messages.value.push({ role: 'user', text: label })
  busy.value = true
  abortCtrl = new AbortController()
  try {
    const body = {
      conversationId: conversationId.value || undefined,
      mode: mode.value,
      message: label,
      selectedProductCode: c.type === 'product' ? c.productCode : undefined,
      selectedOperationId: c.type === 'operation' ? c.operationId : undefined
    }
    const res = await sendAiMessage(body, abortCtrl.signal)
    applyAssistant(res.data)
  } catch (e) {
    messages.value.push({ role: 'assistant', text: e.message || '请求失败' })
  } finally {
    busy.value = false
    abortCtrl = null
    await scrollBottom()
  }
}

function applyAssistant(data) {
  if (!data) return
  conversationId.value = data.conversationId || conversationId.value
  messages.value.push({
    role: 'assistant',
    text: data.answer || '',
    candidates: data.candidates || [],
    sources: data.sources || [],
    workOrders: data.workOrders || null,
    reportEvidence: data.reportEvidence || null,
    warnings: data.warnings || [],
    queriedAt: data.queriedAt ? formatDateTime(data.queriedAt) : ''
  })
}

async function onPage(msg, page) {
  if (!conversationId.value || busy.value) return
  busy.value = true
  try {
    const res = await searchAiWorkOrders({
      conversationId: conversationId.value,
      page,
      pageSize: msg.workOrders.pageSize,
      filters: msg.workOrders.filters
    })
    msg.workOrders = res.data
    msg.queriedAt = formatDateTime(new Date().toISOString())
  } catch { /* http 已提示 */ }
  finally { busy.value = false }
}

async function onEvPage(msg, page) {
  if (!conversationId.value || busy.value) return
  const ev = msg.reportEvidence
  if (!ev) return
  busy.value = true
  try {
    const res = await searchAiReportEvidence({
      conversationId: conversationId.value,
      workOrderId: ev.order.id,
      orderNo: ev.order.orderNo,
      operationId: ev.filter?.operationId ?? null,
      reportFrom: ev.filter?.reportFrom ?? null,
      reportToExclusive: ev.filter?.reportToExclusive ?? null,
      page,
      pageSize: ev.pageSize
    })
    msg.reportEvidence = res.data
  } catch { /* http 已提示 */ }
  finally { busy.value = false }
}

async function openOrder(id) {
  try {
    const res = await getOrder(id)
    detail.value = res.data
    detailDlg.value = true
  } catch { /* http 已提示 */ }
}

function filterLabel(wo) {
  const f = wo?.filters
  if (!f) return ''
  const parts = []
  if (f.dueFrom || f.dueToExclusive) parts.push(`交期 ${f.dueFrom || '…'} ~ ${f.dueToExclusive || '…'}`)
  if (f.productCode) parts.push(`产品 ${f.productCode}`)
  if (f.keyword) parts.push(`关键词 ${f.keyword}`)
  return parts.join('，')
}

function statusText(s) {
  return ({ 0: '未开始', 1: '执行中', 2: '已结束', 3: '已取消' })[s] ?? s
}

function reviewText(s) {
  return ({ 0: '待复核', 1: '已通过', 2: '已退回' })[s] ?? s
}

function formatDate(v) {
  return formatAiBusinessDate(v)
}

function formatDateTime(v) {
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v)
  const p = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

async function scrollBottom() {
  await nextTick()
  const el = chatRef.value
  if (el) el.scrollTop = el.scrollHeight
}
</script>

<style scoped>
.ai-page { display: flex; flex-direction: column; height: calc(100vh - 120px); gap: 14px; width: 100%; max-width: 1120px; margin: 0 auto; min-width: 0; overflow: hidden; }
.ai-toolbar { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 8px; }
.ai-status { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.muted { color: #888; font-size: 12px; }
.mb { margin-bottom: 4px; }
.role-tabs { display: flex; flex-wrap: wrap; gap: 6px; }
.role-tab { display: flex; align-items: center; gap: 12px; border: 1px solid transparent; border-radius: 10px; padding: 10px 16px; background: transparent; color: #637087; cursor: pointer; font: inherit; font-size: 14px; }
.role-tab.active { background: #fff; border-color: #dce5f4; color: #315bd2; box-shadow: 0 2px 6px #26364d06; font-weight: 600; }
.role-tab:hover:not(:disabled) { background: #edf2fc; }
.role-tab:disabled, .question-item:disabled { cursor: not-allowed; opacity: .55; }
.role-chevron { transition: transform .15s; }
.role-chevron.expanded { transform: rotate(180deg); }
.question-panel { flex-shrink: 0; max-height: 38vh; overflow: auto; padding: 12px 16px 16px; background: #fff; border: 1px solid #e2e8f2; border-radius: 14px; }
.question-panel-heading { display: flex; justify-content: space-between; align-items: center; color: #637087; font-size: 13px; margin-bottom: 8px; }
.question-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 6px 14px; }
.question-item { display: flex; align-items: center; gap: 10px; text-align: left; border: 0; border-radius: 8px; padding: 10px; background: #f7f9fc; color: #34445e; font: inherit; font-size: 13px; cursor: pointer; line-height: 1.6; }
.question-item:hover:not(:disabled) { background: #edf2ff; color: #315bd2; }
.question-number { color: #9aa7bc; font-size: 11px; }
.question-arrow { margin-left: auto; color: #a1afc4; }
.role-tab:focus-visible, .question-item:focus-visible { outline: 2px solid #315bd2; outline-offset: 2px; }
.chat {
  flex: 1; min-height: 0; min-width: 0; width: 100%; box-sizing: border-box; overflow: auto; border: 0; border-radius: 16px;
  padding: 24px 12px; background: transparent;
}
.bubble { margin: 0 auto 32px; max-width: 1040px; width: 100%; min-width: 0; box-sizing: border-box; }
.bubble.user { margin-left: auto; max-width: 720px; }
.bubble-role {
  display: flex; align-items: center; gap: 10px;
  font-size: 15px; font-weight: 600; color: #26364d; margin-bottom: 14px;
}
.assistant-avatar { display: inline-grid; place-items: center; width: 32px; height: 32px; border-radius: 10px; background: #e7edff; color: #3864d9; font-size: 12px; letter-spacing: .5px; }
.assistant-caption { font-size: 12px; font-weight: 400; color: #8994a5; }
.bubble.user { margin-right: 0; }
.bubble.user .bubble-role { justify-content: flex-end; }
.bubble-text {
  box-sizing: border-box; min-width: 0; max-width: 100%;
  white-space: pre-wrap; word-break: break-word;
  background: #fff; border: 1px solid var(--app-border, #e5e6eb); border-radius: 10px; padding: 10px 14px;
  box-shadow: var(--app-shadow, 0 1px 2px rgba(29, 33, 41, .04));
}
.bubble-text.is-md {
  white-space: normal; padding: 20px 24px;
  border-radius: 16px; border-color: #e6ebf2;
  box-shadow: 0 4px 20px rgba(35, 55, 85, .035);
}
.bubble.user .bubble-text {
  background: #ecf5ff; border-color: #c6e2ff; box-shadow: none;
}
@media (max-width: 600px) {
  .chat { padding: 10px; }
  .bubble-text.is-md { padding: 12px; }
}
.block { margin-top: 8px; }
.work-order-data { padding: 12px 16px; border: 1px solid #e5eaf2; border-radius: 10px; background: #fff; }
.work-order-data summary { cursor: pointer; color: #53637b; font-size: 13px; }
.work-order-data[open] summary { margin-bottom: 14px; }
.block-title { font-size: 13px; margin-bottom: 6px; }
.cand { margin: 0 6px 6px 0; }
.src { margin-bottom: 6px; }
.excerpt { color: #666; font-size: 12px; margin-top: 4px; white-space: pre-wrap; }
.warn { color: #b88230; font-size: 12px; margin-top: 6px; }
.pager { margin-top: 8px; display: flex; justify-content: flex-end; }
.composer { flex-shrink: 0; padding: 6px 8px 8px; border: 1px solid #dce3ef; border-radius: 16px; background: #fff; box-shadow: 0 4px 20px #26364d08; }
.composer:focus-within { border-color: #91a9ef; box-shadow: 0 0 0 3px #315bd20a; }
.composer :deep(.el-textarea__inner) { box-shadow: none; resize: none; background: transparent; padding: 10px 12px; font-size: 14px; }
.composer :deep(.el-textarea.is-disabled .el-textarea__inner) { background: transparent; }
.composer-footer { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 0 4px 0 12px; }
.composer-hint { color: #9aa5b6; font-size: 12px; }
.composer-footer .el-button { border-radius: 10px; min-width: 88px; margin-left: 0; }
.stop-button { color: #34445e; border-color: #bcc8db; background: #f1f4f9; }
.stop-icon { display: inline-block; width: 10px; height: 10px; border-radius: 2px; background: currentColor; margin-right: 6px; }
@media (max-width: 640px) {
  .question-grid { grid-template-columns: 1fr; }
  .role-tab { padding: 8px 10px; gap: 6px; }
  .composer-hint { font-size: 11px; }
}
</style>
