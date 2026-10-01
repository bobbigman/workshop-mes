<template>
  <div class="em-page" v-loading="loading && !hasData">
    <div class="em-heading">
      <div><span class="em-eyebrow">生产管理 / 执行监控</span><h1>生产执行总览</h1></div>
      <div class="em-live" :class="{ err: errorTip }"><i /><span>{{ errorTip || (lastOkAt ? '更新于 ' + lastOkAt : '正在加载') }}</span><el-button :icon="Refresh" :loading="loading" @click="refresh(true)">刷新</el-button></div>
    </div>

    <div class="em-summary">
      <component :is="c.status === undefined ? 'div' : 'button'" v-for="c in summaryCards" :key="c.key"
        class="em-card" :class="[c.tone, { active: c.status !== undefined && filters.status === c.status }]"
        :type="c.status === undefined ? undefined : 'button'" @click="onStatusCard(c.status)">
        <span class="em-stat-icon"><el-icon><component :is="c.icon" /></el-icon></span>
        <div><div class="l">{{ c.label }}</div><div class="n">{{ c.value }}<small>单</small></div></div>
      </component>
      <div class="em-card red"><span class="em-stat-icon"><el-icon><Bell /></el-icon></span><div><div class="l">待处理异常</div><div class="n">{{ data.summary?.openAbnormal || 0 }}<small>项</small></div></div></div>
    </div>

    <section class="em-filter-panel">
      <div class="em-toolbar">
        <el-input v-model="filters.keyword" class="em-kw" :prefix-icon="Search" clearable placeholder="工单号 / 产品编号 / 产品名称" @keyup.enter="onFilter" @clear="onFilter" />
        <el-select v-model="filters.status" class="em-status" clearable placeholder="全部状态" @change="onFilter"><el-option :value="0" label="未开始" /><el-option :value="1" label="执行中" /><el-option :value="2" label="已结束" /></el-select>
        <el-date-picker v-model="dueRange" class="em-date" type="daterange" value-format="YYYY-MM-DD" start-placeholder="交期开始" end-placeholder="交期结束" @change="onFilter" />
        <el-button type="primary" @click="onFilter">查询</el-button><el-button @click="resetFilters">重置</el-button><el-tooltip content="工单统计随关键词、交期变化，不含已取消；异常与待办按当前权限统计。" placement="bottom"><button type="button" class="em-scope-help" aria-label="查看统计范围说明">ⓘ 统计范围</button></el-tooltip>
      </div>

    </section>

    <div class="em-mid">
      <section class="em-panel em-chains">
        <header class="em-panel-h"><h3>工序进度</h3><div class="em-legend"><span class="full">已报满</span><span class="partial">已报未满</span><span>未报工</span></div></header>
        <div class="em-panel-caption">当前页前 4 张工单 · 查看更多请使用下方明细</div>
        <div v-if="!focusOrders.length" class="em-empty">暂无符合条件的工单</div>
        <div v-for="o in focusOrders" :key="o.id" class="em-chain-row">
          <button class="em-chain-meta" @click="goOrderDetail(o)"><strong :title="o.orderNo">{{ o.orderNo }}</strong><span :title="o.productName">{{ o.productName }}</span><small>{{ statusLabel(o.status) }}<b v-if="o.dueState === 'overdue'" class="due-overdue"> · 已超期</b><b v-else-if="o.dueState === 'warning'" class="due-warning"> · 临期</b><b v-if="o.hasOpenAbnormal"> · 异常</b></small></button>
          <div class="em-ops">
            <template v-for="(op, idx) in (o.ops || [])" :key="op.operationId + '-' + idx">
              <div class="em-op" :class="opClass(op)" :title="op.status + (op.defectQty ? ' · 不良 ' + op.defectQty : '')">
                <div class="op-top"><strong :title="op.operationName">{{ op.operationName }}</strong><i /></div>
                <div class="op-bottom"><span :title="op.assigneeName || (op.isVirtual ? '按原工艺展示' : '未派工')">{{ op.assigneeName || (op.isVirtual ? '原工艺' : '未派工') }}</span><b>{{ op.planQty === 0 ? '无需报工' : op.doneQty + '/' + op.planQty }}</b></div>
              </div><span v-if="idx < o.ops.length - 1" class="em-arrow" aria-hidden="true">→</span>
            </template><span v-if="!o.ops?.length" class="muted">{{ o.progressHint || '无工序计划' }}</span>
          </div>
        </div>
      </section>

      <section class="em-panel em-progress">
        <header class="em-panel-h"><h3>工单进度</h3><span class="sub">当前页前 5 单</span></header>
        <div class="em-panel-caption">按各工序计划达成情况计算</div>
        <div v-if="!visibleProgress.length" class="em-empty">暂无工单</div>
        <button v-for="(o, i) in visibleProgress" :key="o.id" class="em-progress-row" @click="goOrderDetail(o)">
          <span class="em-rank">{{ String(i + 1).padStart(2, '0') }}</span><div class="em-progress-info"><div class="em-progress-label"><span :title="o.orderNo + ' · ' + o.productName">{{ o.productName }}</span><b>{{ o.progressPercent == null ? '—' : formatPct(o) }}</b></div><div class="em-progress-order" :title="o.orderNo">{{ o.orderNo }}</div><div class="em-track"><i :style="{ width: progressValue(o) + '%' }" :class="{ full: progressValue(o) === 100 }" /></div></div>
        </button>
      </section>

      <section class="em-panel em-exceptions">
        <header class="em-panel-h"><h3>现场异常 <em>{{ data.abnormals?.total || 0 }}</em></h3><el-button v-if="canHandleAbnormal" link type="primary" @click="goAbnormal()">查看全部</el-button></header>
        <div class="em-panel-caption">待处理 · 不随工单筛选</div>
        <div class="em-alert-list">
          <div v-if="!data.abnormals?.list?.length" class="em-empty"><el-icon class="em-ok"><CircleCheck /></el-icon><strong>当前无待处理异常</strong><span>现场上报后将在这里显示</span></div>
          <component :is="canHandleAbnormal ? 'button' : 'div'" v-for="a in (data.abnormals?.list || []).slice(0, 4)" :key="a.id" class="em-alert" @click="canHandleAbnormal && goAbnormal(a.id)"><div><span class="em-alert-type">{{ a.typeLabel }}</span><time>{{ formatTime(a.reportedAt) }}</time></div><p :title="a.description">{{ a.description }}</p><small>{{ a.orderNo || '未关联工单' }}</small></component>
        </div>
      </section>
    </div>

    <div class="em-lower">
      <section class="em-panel em-details">
        <header class="em-panel-h"><h3>生产工单明细 <span class="sub">共 {{ data.orders?.total || 0 }} 单</span></h3><span class="sub">现场进度含待复核报工</span></header>
        <el-table :data="data.orders?.list || []" size="small" height="290" empty-text="暂无符合条件的工单">
          <el-table-column prop="orderNo" label="工单号" min-width="175" show-overflow-tooltip /><el-table-column prop="productName" label="产品" min-width="110" show-overflow-tooltip />
          <el-table-column label="计划" width="70" prop="qty" align="right" /><el-table-column label="完成 / 剩余" width="100" align="right"><template #default="{ row }">{{ row.doneQty }} / {{ row.remainQty }}</template></el-table-column>
          <el-table-column label="工序达成" width="130"><template #default="{ row }"><div v-if="row.progressPercent != null" class="em-table-progress"><div class="em-track"><i :style="{ width: progressValue(row) + '%' }" /></div><span>{{ formatPct(row) }}</span></div><span v-else :title="row.progressHint">—</span></template></el-table-column>
          <el-table-column label="状态" width="90"><template #default="{ row }"><el-tag size="small" :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag></template></el-table-column>
          <el-table-column label="计划交期" width="145"><template #default="{ row }"><span :class="dueClass(row)">{{ formatDue(row.dueDate) }}</span></template></el-table-column>
          <el-table-column label="操作" width="175" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="goOrderDetail(row)">{{ canHandleAbnormal ? '详情 / 派工' : '详情' }}</el-button><el-button v-if="row.status === 0 || row.status === 1" link type="primary" @click="goReport(row)">报工</el-button><el-button v-if="row.hasOpenAbnormal && canHandleAbnormal" link type="danger" @click="goAbnormal()">异常</el-button></template></el-table-column>
        </el-table>
        <div class="em-pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="data.orders?.total || 0" :page-sizes="[10,20,50]" layout="total, sizes, prev, pager, next" @current-change="() => refresh(true)" @size-change="onPageSize" /></div>
      </section>
      <aside class="em-side">
        <section class="em-panel em-overdue"><header class="em-panel-h"><h3>交期预警 <em>{{ data.overdueOrders?.total || 0 }}</em></h3><span class="sub">超期工单</span></header><div class="em-panel-caption">随关键词、交期筛选 · 最多 10 单</div><div class="em-overdue-list"><div v-if="!data.overdueOrders?.list?.length" class="em-empty sm">暂无超期工单</div><button v-for="o in data.overdueOrders?.list || []" :key="o.id" class="em-due-item" @click="goOrderDetail(o)"><strong :title="o.orderNo">{{ o.orderNo }}</strong><span>{{ o.productName }}</span><small>交期 {{ formatDue(o.dueDate) }}</small></button></div></section>
        <section v-if="showTodos" class="em-panel em-todo-panel"><header class="em-panel-h"><h3>待处理任务</h3><span class="sub">当前权限范围</span></header><button v-if="data.todos?.pendingReviewCount != null" class="em-todo" @click="goReview"><span><i class="amber" />报工待复核</span><b>{{ data.todos.pendingReviewCount }}<small> 条 →</small></b></button><button v-if="data.todos?.unassignedOpCount != null" class="em-todo" @click="goUnassignedHint"><span><i />工序待派工</span><b>{{ data.todos.unassignedOpCount }}<small> 道 →</small></b></button></section>
      </aside>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Clock, VideoPlay, CircleCheck, AlarmClock, Warning, Bell, Refresh, Search } from '@element-plus/icons-vue'
import { getExecutionMonitor } from '@/api/executionMonitor'

const router = useRouter()
const role = Number(localStorage.getItem('role') || 0)
const canHandleAbnormal = role === 1 || role === 3
const showTodos = computed(() =>
  data.value.todos?.pendingReviewCount != null || data.value.todos?.unassignedOpCount != null)

const filters = ref({ keyword: '', status: undefined })
const dueRange = ref(null)
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)
const hasData = ref(false)
const errorTip = ref('')
const lastOkAt = ref('')
const data = ref({
  summary: {},
  orders: { list: [], total: 0 },
  abnormals: { list: [], total: 0 },
  overdueOrders: { list: [], total: 0 },
  todos: {}
})

let timer = null
let seq = 0
let inFlight = false
let errToastAt = 0

const STATUS = { 0: '未开始', 1: '执行中', 2: '已结束', 3: '已取消' }
function statusLabel(s) { return STATUS[s] ?? s }
function statusTag(s) { return s === 0 ? 'info' : s === 1 ? 'primary' : s === 2 ? 'success' : 'danger' }
function formatDue(t) { return t ? String(t).replace('T', ' ').slice(0, 16) : '—' }
function formatTime(t) { return t ? String(t).replace('T', ' ').slice(5, 16) : '' }
function dueClass(row) {
  return row.dueState === 'overdue' ? 'due-overdue' : row.dueState === 'warning' ? 'due-warning' : ''
}
function formatPct(row) {
  if (row.progressPercent == null) return row.progressHint || '—'
  return row.progressPercent + '%'
}
function opClass(op) {
  if (op.status === '已报满') return 'full'
  if (op.status === '已报未满') return 'partial'
  if (op.status === '无需报工') return 'zero'
  return 'idle'
}

const summaryCards = computed(() => {
  const s = data.value.summary || {}
  return [
    { key: 'ns', label: '未开始', value: s.notStarted || 0, status: 0, tone: 'blue', icon: Clock },
    { key: 'do', label: '执行中', value: s.doing || 0, status: 1, tone: 'blue', icon: VideoPlay },
    { key: 'dn', label: '已结束', value: s.done || 0, status: 2, tone: 'green', icon: CircleCheck },
    { key: 'dw', label: '临期', value: s.dueWarning || 0, status: undefined, tone: 'amber', icon: AlarmClock },
    { key: 'ov', label: '超期', value: s.dueOverdue || 0, status: undefined, tone: 'red', icon: Warning }
  ]
})

function buildParams() {
  const p = {
    page: page.value,
    pageSize: pageSize.value,
    keyword: filters.value.keyword || undefined,
    status: filters.value.status
  }
  if (dueRange.value?.length === 2) {
    p.dueFrom = dueRange.value[0]
    p.dueTo = dueRange.value[1]
  }
  return p
}

async function refresh(manual = false) {
  if (inFlight) return
  inFlight = true
  const my = ++seq
  if (manual || !hasData.value) loading.value = true
  try {
    const res = await getExecutionMonitor(buildParams())
    if (my !== seq) return
    data.value = res.data || data.value
    hasData.value = true
    errorTip.value = ''
    lastOkAt.value = new Date().toLocaleString('zh-CN')
    const maxPage = Math.max(1, Math.ceil((data.value.orders?.total || 0) / pageSize.value))
    if (page.value > maxPage) {
      page.value = maxPage
      inFlight = false
      loading.value = false
      return refresh(true)
    }
  } catch (e) {
    if (my !== seq) return
    errorTip.value = '刷新失败，已保留上次数据'
    const now = Date.now()
    if (now - errToastAt > 8000) {
      errToastAt = now
      ElMessage.error(e?.message || '执行监控加载失败')
    }
  } finally {
    if (my === seq) {
      loading.value = false
      inFlight = false
    }
  }
}

function onFilter() {
  page.value = 1
  refresh(true)
}
function onPageSize() {
  page.value = 1
  refresh(true)
}
function onStatusCard(status) {
  if (status === undefined) return
  filters.value.status = filters.value.status === status ? undefined : status
  onFilter()
}

function goOrderDetail(row) {
  router.push({ path: '/order', query: { openId: String(row.id) } })
}
function goReport(row) {
  router.push({ path: '/report', query: { order: row.orderNo } })
}
function goAbnormal(id) {
  const q = { status: '0' }
  if (id) q.focusId = String(id)
  router.push({ path: '/abnormal', query: q })
}
function goReview() {
  router.push({ path: '/review', query: { reviewStatus: '0' } })
}
function goUnassignedHint() {
  ElMessage.info('请打开工单详情，为未派工工序选择执行人；旧工单需要先完善工序任务')
}

function onVisibility() {
  if (document.hidden) {
    if (timer) { clearInterval(timer); timer = null }
  } else {
    refresh(true)
    startTimer()
  }
}
function startTimer() {
  if (timer) clearInterval(timer)
  timer = setInterval(() => refresh(false), 10000)
}

onMounted(() => {
  refresh(true)
  startTimer()
  document.addEventListener('visibilitychange', onVisibility)
})
onUnmounted(() => {
  if (timer) clearInterval(timer)
  document.removeEventListener('visibilitychange', onVisibility)
})

const focusOrders = computed(() => (data.value.orders?.list || []).slice(0, 4))
const visibleProgress = computed(() => (data.value.orders?.list || []).slice(0, 5))
function progressValue(o) { return Math.max(0, Math.min(100, Number(o.progressPercent) || 0)) }
function resetFilters() { filters.value = { keyword: '', status: undefined }; dueRange.value = null; onFilter() }
</script>

<style scoped>
.em-page { min-width:0; width:100%; display:flex; flex-direction:column; gap:14px; color:#243650; }
.em-heading { display:flex; justify-content:space-between; align-items:center; gap:12px; }
.em-eyebrow { color:#7c8ba0; font-size:12px; } h1 { margin:4px 0 0; font-size:23px; letter-spacing:1px; color:#152d50; }
.em-live { display:flex; align-items:center; gap:8px; color:#7c8ba0; font-size:12px; } .em-live>i { width:6px; height:6px; background:#19a577; border-radius:50%; } .em-live.err { color:#d44444; } .em-live.err>i { background:#d44444; }
.em-summary { display:grid; grid-template-columns:repeat(6,minmax(0,1fr)); gap:12px; }
.em-card { --tone:#2674e8; --tint:#edf4ff; display:flex; align-items:center; gap:13px; min-width:0; padding:15px 16px; background:white; border:1px solid #e3e9f1; border-radius:7px; text-align:left; font-family:inherit; }
button.em-card { cursor:pointer; } button.em-card:hover,.em-card.active { border-color:#75a5f5; box-shadow:0 2px 9px #2355a00a; } .em-card.green { --tone:#15a477; --tint:#e9f8f2; } .em-card.amber { --tone:#e79a21; --tint:#fff5e6; } .em-card.red { --tone:#e25560; --tint:#fff0f1; }
.em-stat-icon { display:grid; place-items:center; width:44px; height:44px; flex:none; background:var(--tint); color:var(--tone); border-radius:50%; font-size:23px; }
.em-card .l { font-size:13px; color:#6d7b8e; white-space:nowrap; } .em-card .n { margin-top:3px; font-size:30px; font-weight:650; line-height:1.15; color:#172c49; font-variant-numeric:tabular-nums; } .em-card small { font-size:12px; font-weight:400; color:#8a97a9; margin-left:7px; }
.em-filter-panel { background:#fff; border:1px solid #e3e9f1; border-radius:7px; padding:12px 15px 9px; min-width:0; }
.em-toolbar { display:flex; flex-wrap:wrap; align-items:center; gap:8px; min-width:0; } .em-toolbar .el-button+.el-button { margin-left:0; } .em-kw { width:260px; max-width:100%; } .em-status { width:130px; } .em-toolbar :deep(.em-date) { flex:0 1 280px; width:280px; min-width:230px; } .em-scope { font-size:11px; color:#8995a6; margin-top:8px; }
.em-mid { display:grid; grid-template-columns:minmax(0,1.8fr) minmax(0,.9fr) minmax(0,1fr); gap:14px; }
.em-panel { min-width:0; background:white; border:1px solid #e3e9f1; border-radius:7px; padding:15px; overflow:hidden; }
.em-panel-h { display:flex; justify-content:space-between; align-items:center; gap:8px; min-height:22px; margin-bottom:5px; } h3 { margin:0; font-size:15px; color:#183554; font-weight:650; } .em-panel-h em { display:inline-block; font-size:12px; background:#fff0f1; color:#d9505a; font-style:normal; border-radius:4px; padding:1px 5px; margin-left:5px; } .sub,.em-panel-caption { color:#8592a5; font-size:11px; font-weight:400; } .em-panel-caption { margin-bottom:9px; } .em-panel-h h3 .sub { margin-left:8px; }
.em-mid>.em-panel { height:340px; box-sizing:border-box; }
.em-legend { display:flex; gap:9px; font-size:10px; color:#78889b; } .em-legend span:before { content:''; display:inline-block; width:6px; height:6px; border-radius:2px; background:#bdc7d3; margin-right:4px; } .em-legend .full:before { background:#19a578; } .em-legend .partial:before { background:#3984e9; }
.em-chain-row { display:grid; grid-template-columns:120px minmax(0,1fr); gap:9px; align-items:center; padding:7px 0; border-bottom:1px solid #f0f3f7; } .em-chain-row:last-child { border:0; }
.em-chain-meta { border:0; background:none; text-align:left; font-family:inherit; padding:0; cursor:pointer; min-width:0; } .em-chain-meta strong,.em-chain-meta>span { display:block; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; } .em-chain-meta strong { font-size:12px; color:#294563; } .em-chain-meta>span { font-size:12px; color:#76869b; margin-top:3px; } .em-chain-meta small { font-size:10px; color:#8b99aa; } .em-chain-meta small b { color:#dc5460; }
.em-ops { min-width:0; display:flex; gap:5px; align-items:center; overflow-x:auto; padding:2px 0 4px; scrollbar-width:thin; }
.em-op { min-width:84px; flex:1 0 84px; border:1px solid #dfe6ef; border-radius:4px; padding:7px; box-sizing:border-box; background:#fafbfd; } .em-op.full { border-color:#77c9ae; background:#f5fcf9; } .em-op.partial { border-color:#8db8f0; background:#f5f9ff; } .em-op.zero { background:#f4f5f7; color:#9099a5; }
.op-top,.op-bottom { display:flex; justify-content:space-between; align-items:center; gap:5px; } .op-top strong { font-size:12px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; } .op-top i { width:5px; height:5px; background:#b9c4d2; border-radius:50%; flex:none; } .full .op-top i { background:#19a578; } .partial .op-top i { background:#3984e9; } .op-bottom { font-size:10px; margin-top:5px; } .op-bottom span { color:#8491a3; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; } .op-bottom b { color:#50637c; font-weight:500; white-space:nowrap; } .em-arrow { color:#a9b7c9; font-size:12px; flex:none; }
.em-progress-row { width:100%; display:flex; align-items:center; gap:10px; border:0; border-bottom:1px solid #f1f4f8; background:none; padding:9px 0; text-align:left; cursor:pointer; font-family:inherit; } .em-rank { color:#91a0b4; font-size:11px; } .em-progress-info { min-width:0; flex:1; } .em-progress-label { display:flex; justify-content:space-between; gap:6px; margin-bottom:8px; font-size:12px; } .em-progress-label>span { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; color:#455973; } .em-progress-label b { font-size:11px; color:#748299; font-weight:500; } .em-track { height:5px; background:#edf2f8; border-radius:4px; overflow:hidden; } .em-track i { display:block; height:100%; background:#3984e9; border-radius:4px; } .em-track i.full { background:#19a578; }
.em-alert-list { max-height:278px; overflow-y:auto; scrollbar-width:thin; } .em-alert { display:block; width:100%; text-align:left; border:0; background:none; padding:10px 0; border-bottom:1px solid #f0f3f7; font-family:inherit; } button.em-alert { cursor:pointer; } .em-alert>div { display:flex; justify-content:space-between; gap:6px; } .em-alert-type { color:#d95460; font-size:11px; background:#fff2f3; padding:2px 5px; border-radius:3px; } .em-alert time { font-size:10px; color:#8996a7; } .em-alert p { font-size:12px; color:#435974; margin:6px 0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; } .em-alert small { color:#8a97a8; font-size:10px; }
.em-empty { min-height:140px; display:flex; flex-direction:column; align-items:center; justify-content:center; gap:10px; color:#91a0b2; font-size:12px; } .em-empty strong { font-weight:500; color:#698078; } .em-empty span { font-size:11px; } .em-ok { color:#77b6a0; font-size:34px; margin-bottom:4px; } .em-empty.sm { min-height:50px; }
.em-lower { display:grid; grid-template-columns:minmax(0,2.7fr) minmax(0,1fr); gap:14px; } .em-details :deep(.el-table) { margin-top:12px; --el-table-row-hover-bg-color:#f5f8fe; } .em-details :deep(.el-table th.el-table__cell),.em-details :deep(.el-table td.el-table__cell) { padding:7px 0; font-size:12px; } .em-details :deep(.el-table .cell) { padding:0 9px; } .em-details :deep(.el-table .el-button) { font-size:12px; } .em-table-progress { display:flex; align-items:center; gap:6px; font-size:11px; } .em-table-progress .em-track { flex:1; } .em-pager { display:flex; justify-content:flex-end; margin-top:12px; overflow-x:auto; }
.em-side { min-width:0; display:flex; flex-direction:column; gap:14px; } .em-overdue-list { max-height:125px; overflow-y:auto; } .em-due-item { display:block; width:100%; background:none; border:0; border-bottom:1px solid #f0f3f7; padding:8px 0; text-align:left; font-family:inherit; cursor:pointer; } .em-due-item strong { display:block; font-size:12px; color:#485c76; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; } .em-due-item>span { font-size:11px; color:#8795a7; } .em-due-item small { display:block; margin-top:3px; color:#db5a61; font-size:11px; }
.em-todo { display:flex; align-items:center; justify-content:space-between; width:100%; border:0; background:none; padding:12px 0; font-family:inherit; cursor:pointer; } .em-todo>span { font-size:12px; color:#5e718b; } .em-todo i { display:inline-block; height:7px; width:7px; background:#3984e9; margin-right:8px; border-radius:2px; } .em-todo i.amber { background:#e8a036; } .em-todo b { font-size:20px; color:#274665; } .em-todo small { font-size:11px; font-weight:400; color:#96a1b2; }
.muted { color:#8a97a8; font-size:12px; } .due-overdue { color:#d84d59 !important; } .due-warning { color:#d59423 !important; }
@media(min-width:1600px) { .em-mid { grid-template-columns:minmax(0,2fr) minmax(0,1fr) minmax(0,1fr); } .em-chain-row { grid-template-columns:145px minmax(0,1fr); } }
@media(max-width:1200px) { .em-card { padding:15px 10px; gap:8px; } .em-stat-icon { width:34px; height:34px; font-size:19px; } .em-card .n { font-size:26px; } .em-mid { grid-template-columns:minmax(0,1.8fr) minmax(0,1fr); } .em-exceptions { grid-column:1/-1; height:auto !important; } .em-alert-list { display:flex; gap:20px; } .em-alert-list>.em-empty { width:100%; min-height:80px; } .em-alert { min-width:200px; } .em-lower { grid-template-columns:minmax(0,1fr) 260px; } }
@media(max-width:768px) { .em-heading { flex-wrap:wrap; } .em-summary { grid-template-columns:repeat(3,minmax(0,1fr)); } .em-mid,.em-lower { grid-template-columns:minmax(0,1fr); } .em-exceptions { grid-column:auto; } .em-kw { width:100%; } .em-toolbar :deep(.em-date) { flex:1 1 240px; } .em-chain-row { grid-template-columns:100px minmax(0,1fr); } .em-live { font-size:10px; } .em-panel-h { flex-wrap:wrap; } }
@media(max-width:440px) { .em-summary { grid-template-columns:repeat(2,minmax(0,1fr)); } .em-legend { gap:5px; } }
/* 首屏密度：固定行高，确保概览中的最后一行完整可见。 */
.em-heading>div:first-child { display:flex; align-items:baseline; gap:14px; }
.em-heading h1 { order:-1; font-size:21px; margin:0; }
.em-chain-row { padding:5px 0; }
.em-op { padding:6px; }
.op-top,.op-bottom { line-height:14px; }
.op-bottom { margin-top:4px; }
.em-chain-meta { line-height:15px; }
.em-chain-meta small { display:block; line-height:14px; }
.em-chains { overflow-y:auto; scrollbar-width:thin; }
@media(max-width:768px) { .em-heading>div:first-child { flex-wrap:wrap; gap:5px; } }
/* 精修：业务正文优先可读，辅助信息使用统一字号。 */
.em-chain-meta strong,.em-chain-meta>span,.op-top strong,
.em-progress-label,.em-alert p,.em-due-item strong,.em-todo>span { font-size:13px; }
.op-bottom,.em-chain-meta small,.em-panel-caption,.sub,.em-legend,
.em-rank,.em-progress-label b,.em-alert-type,.em-alert time,.em-alert small,
.em-due-item>span,.em-due-item small,.em-empty span,.em-table-progress { font-size:12px; }
.op-bottom b { font-weight:600; }
.op-top,.op-bottom { line-height:17px; }
.em-chain-meta { line-height:17px; }
.em-chain-meta small { line-height:17px; }
.em-details :deep(.el-table th.el-table__cell),
.em-details :deep(.el-table td.el-table__cell),
.em-details :deep(.el-table .el-button) { font-size:13px; }
.em-progress { overflow-y:auto; scrollbar-width:thin; }
.em-progress-order { font-size:12px; color:#7c8ba0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; margin-bottom:6px; } .em-progress-label { margin-bottom:3px; } .em-progress-row { padding:7px 0; }
.em-page { gap:12px; }
.em-filter-panel { padding:10px 15px; }
.em-card { padding-top:12px; padding-bottom:12px; }
.em-scope-help { margin-left:auto; border:0; background:transparent; font:inherit; font-size:12px; color:#6d7f95; cursor:help; padding:5px; }
.em-scope-help:focus-visible { outline:2px solid #3370ff; outline-offset:2px; }
/* 两排使用同一个右栏宽度，避免独立 fr 网格的间距误差。 */
@media(min-width:1201px) {
  .em-page { --monitor-side-width:26%; }
  .em-mid { grid-template-columns:minmax(0,2fr) minmax(0,1fr) var(--monitor-side-width); }
  .em-lower { grid-template-columns:minmax(0,1fr) var(--monitor-side-width); }
}
@media(min-width:1600px) { .em-page { --monitor-side-width:24%; } }
/* 增大字号后重新约束行高，概览末行不依赖滚动才能看到。 */
.em-mid>.em-panel { height:350px; }
.em-chain-row { padding:3px 0; }
.em-op { padding:5px 6px; }
.op-top,.op-bottom { line-height:16px; }
.em-progress-row { padding:5px 0; }
.em-progress-label { line-height:16px; margin-bottom:3px; }
.em-progress-order { line-height:14px; margin-bottom:4px; }
</style>








