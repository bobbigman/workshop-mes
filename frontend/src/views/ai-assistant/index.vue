<template>
  <div class="ai-page">
    <ValueTip :text="VALUE_TIPS.aiAssistant" />
    <div class="ai-toolbar">
      <el-radio-group v-model="mode" :disabled="busy" @change="onModeChange">
        <el-radio-button value="progress">查进度</el-radio-button>
        <el-radio-button value="guide">问工艺</el-radio-button>
        <el-radio-button value="support">智能客服</el-radio-button>
      </el-radio-group>
      <div class="ai-status">
        <el-tag v-if="!status.enabled" type="info">已关闭</el-tag>
        <el-tag v-else-if="!status.configured" type="warning">未配置</el-tag>
        <el-tag v-else type="success">已就绪</el-tag>
        <span v-if="status.model" class="muted">模型已配置</span>
        <el-button size="small" :disabled="!status.enabled || busy" :loading="testing" @click="onTest">连接测试</el-button>
        <el-button size="small" :disabled="busy" @click="onNewChat">新对话</el-button>
      </div>
    </div>

    <el-alert
      v-if="!privacyAck"
      type="warning"
      show-icon
      :closable="false"
      title="使用说明"
      description="AI 会将本次问题及必要业务资料发送至服务器配置的模型服务。请勿输入工资、密码等敏感内容。本期仅支持查询，不能改数或下单。"
      class="mb"
    />
    <el-button v-if="!privacyAck" type="primary" size="small" class="mb" @click="ackPrivacy">我知道了</el-button>

    <el-alert v-if="banner" :type="bannerType" show-icon :closable="false" :title="banner" class="mb" />

    <div class="examples" v-if="privacyAck">
      <span class="muted">示例：</span>
      <el-button
        v-for="q in examples"
        :key="q"
        link
        type="primary"
        :disabled="busy || !canChat"
        @click="useExample(q)"
      >{{ q }}</el-button>
    </div>

    <div ref="chatRef" class="chat">
      <div v-for="(m, i) in messages" :key="i" class="bubble" :class="m.role">
        <div class="bubble-role">{{ m.role === 'user' ? '我' : '助手' }}</div>
        <div class="bubble-text">{{ m.text }}</div>

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

        <div v-if="m.workOrders" class="block">
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
        </div>

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

        <div v-if="m.sources?.length" class="block">
          <div class="block-title">来源</div>
          <el-card v-for="s in m.sources" :key="s.sourceId" shadow="never" class="src">
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
        :rows="2"
        maxlength="2000"
        show-word-limit
        :disabled="!canChat || busy"
        placeholder="用日常语言提问，例如：明天到期还没完成的工单有哪些？"
        @keydown.ctrl.enter="onSend"
      />
      <div class="composer-actions">
        <el-button type="primary" :disabled="!canChat || !input.trim()" :loading="busy" @click="onSend">发送</el-button>
        <el-button :disabled="!busy" @click="onStop">停止</el-button>
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
  sendAiMessage,
  testAiConnection
} from '@/api/aiAssistant'
import { getOrder } from '@/api/prod'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'

const router = useRouter()
const role = Number(localStorage.getItem('role') || 0)
if (role !== 1) {
  ElMessage.error('仅管理员可使用 AI 助手')
  router.replace('/order')
}

const mode = ref('progress')
const status = ref({ enabled: false, configured: false, model: null })
const privacyAck = ref(false)
const conversationId = ref('')
const input = ref('')
const messages = ref([])
const busy = ref(false)
const testing = ref(false)
const banner = ref('')
const bannerType = ref('info')
const chatRef = ref(null)
let abortCtrl = null

const detailDlg = ref(false)
const detail = ref(null)

const examples = computed(() =>
  mode.value === 'progress'
    ? ['明天到期还没完成的工单有哪些？', '查一下工单进度']
    : mode.value === 'guide'
      ? ['某产品裁切要注意什么？', '按产品编号问工艺要点']
      : ['如何补报工单？', '为什么这个产品的完成数是 80？']
)

const canChat = computed(() => status.value.enabled && status.value.configured && privacyAck.value)

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

function ackPrivacy() {
  privacyAck.value = true
}

function useExample(q) {
  input.value = q
}

async function onTest() {
  testing.value = true
  try {
    const res = await testAiConnection()
    const d = res.data
    if (d?.ok) ElMessage.success('连接成功')
    else ElMessage.error(d?.message || '连接失败')
  } catch (e) {
    /* http 拦截器通常已提示；无响应时再补一句 */
    if (!e?.message) ElMessage.error('连接测试失败')
  } finally {
    testing.value = false
  }
}

async function onNewChat() {
  if (conversationId.value) {
    try {
      await deleteAiConversation(conversationId.value)
    } catch {
      ElMessage.warning('旧会话清理失败，已开始新对话')
    }
  }
  conversationId.value = ''
  messages.value = []
  input.value = ''
  abortCtrl?.abort()
  busy.value = false
}

function onModeChange() {
  onNewChat()
}

async function onSend() {
  if (!canChat.value || busy.value) return
  const text = input.value.trim()
  if (!text) return

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
  if (!v) return '—'
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v).slice(0, 10)
  return d.toISOString().slice(0, 10)
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
.ai-page { display: flex; flex-direction: column; height: calc(100vh - 120px); gap: 10px; }
.ai-toolbar { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 8px; }
.ai-status { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.muted { color: #888; font-size: 12px; }
.mb { margin-bottom: 4px; }
.examples { display: flex; flex-wrap: wrap; gap: 4px; align-items: center; }
.chat {
  flex: 1; overflow: auto; border: 1px solid var(--el-border-color); border-radius: 8px;
  padding: 12px; background: #fafafa;
}
.bubble { margin-bottom: 14px; max-width: 920px; }
.bubble.user { margin-left: auto; }
.bubble-role { font-size: 12px; color: #888; margin-bottom: 4px; }
.bubble-text {
  white-space: pre-wrap; word-break: break-word;
  background: #fff; border: 1px solid var(--el-border-color); border-radius: 8px; padding: 10px 12px;
}
.bubble.user .bubble-text { background: #ecf5ff; }
.block { margin-top: 8px; }
.block-title { font-size: 13px; margin-bottom: 6px; }
.cand { margin: 0 6px 6px 0; }
.src { margin-bottom: 6px; }
.excerpt { color: #666; font-size: 12px; margin-top: 4px; white-space: pre-wrap; }
.warn { color: #b88230; font-size: 12px; margin-top: 6px; }
.pager { margin-top: 8px; display: flex; justify-content: flex-end; }
.composer { display: flex; gap: 10px; align-items: flex-end; }
.composer-actions { display: flex; flex-direction: column; gap: 6px; }
</style>
