<template>
  <div class="screen" :class="{ loading: loading && !hasData }">
    <div v-if="showFsMask" class="fs-mask" @click="enterFullscreen">
      <div class="fs-card">
        <div class="fs-title">点击任意处进入全屏挂墙模式</div>
        <div class="fs-sub">Esc 退出全屏 · F11 亦可切换</div>
      </div>
    </div>

    <header class="top">
      <div class="top-left">
        <span class="factory">{{ factoryName || '车间' }}</span>
        <span class="date">{{ today }}</span>
        <span class="refresh-status" :class="{ failed: refreshError }">{{ refreshStatus }}</span>
      </div>
      <h1 class="top-center">车间生产监控大屏</h1>
      <div class="top-right">
        <span class="clock">{{ clockText }}</span>
        <button type="button" class="exit-btn" title="退出大屏，回到看板管理页" @click="exitScreen">✕ 退出大屏</button>
      </div>
    </header>

    <div v-if="loading && !hasData" class="load-mask">加载中…</div>

    <div v-else-if="!hasData" class="load-mask">数据尚未加载成功，请查看上方错误信息，系统每10秒重试。</div>

    <template v-else>
      <div v-if="alertLines.length" class="alerts">
        <div class="alerts-label">待处理异常 {{ data.openAbnormalCount }}</div>
        <div class="alerts-list">
          <div v-for="a in alertLines" :key="a.id" class="alert-line">
            <em :class="'t' + a.abnormalType">{{ a.typeLabel }}</em>
            <span class="desc">{{ a.description }}</span>
            <span v-if="a.orderNo" class="meta">{{ a.orderNo }}<small v-if="alertDue(a)" class="alert-due">{{ alertDue(a) }}</small></span>
            <span class="meta">{{ formatTime(a.reportedAt) }}</span>
          </div>
          <div v-if="extraAlertCount > 0" class="alert-more">还有 {{ extraAlertCount }} 条未关闭</div>
        </div>
      </div>
      <div v-else class="alerts ok">
        <span class="ok-msg">暂无待处理异常</span>
      </div>

      <div class="body">
        <aside class="col-left">
          <h3 class="group-title">生产状态 · 工单</h3>
          <div class="stat-grid production-stats">
            <div class="stat"><div class="n">{{ notStarted }}</div><div class="l">未开始</div></div>
            <div class="stat"><div class="n">{{ data.doingOrders }}</div><div class="l">执行中</div></div>
            <div class="stat"><div class="n">{{ data.doneOrders }}</div><div class="l">已完成</div></div>
          </div>
          <h3 class="group-title">风险状态 · 可重叠</h3>
          <div class="stat-grid">
            <div class="stat"><div class="n warn">{{ data.dueWarnCount }}</div><div class="l">临期</div></div>
            <div class="stat"><div class="n danger">{{ data.dueOverdueCount }}</div><div class="l">超期</div></div>
            <div class="stat"><div class="n" :class="{ danger: data.openAbnormalCount > 0 }">{{ data.openAbnormalCount || 0 }}</div><div class="l">异常 · 条</div></div>
          </div>
          <div class="output-stack">
            <div class="output-card good">
              <div class="l">本月良品 · 报工数量</div>
              <div class="n">{{ formatMonth(data.monthGood) }}</div>
            </div>
            <div class="output-card bad">
              <div class="l">本月不良 · 报工数量</div>
              <div class="n">{{ formatMonth(data.monthDefect) }}</div>
            </div>
          </div>
        </aside>

        <main class="col-center">
          <div class="gauge-panel">
            <div class="gauge-wrap">
              <div ref="gaugeRef" class="gauge"></div>
              <div class="gauge-note">未完工单完成数量合计 ÷ 计划数量合计</div>
            </div>
            <div class="gauge-side">
              <div class="side-title">待推进工序 · 工单数</div>
              <div v-if="!stuckReady" class="side-empty">—</div>
              <div v-else-if="!stuckOps.length" class="side-empty">暂无未报满工序</div>
              <p class="side-note">按第一道未报满工序归组</p>
              <div v-for="s in stuckOps" :key="s.name" class="stuck-row">
                <span>{{ s.name }}</span>
                <strong>{{ s.count }}</strong>
              </div>
            </div>
          </div>

          <div class="panel progress-list">
            <h3>未完工单 <em>{{ openOrders.length }} 张</em></h3>
            <p class="caption">超期优先，同类按交期 · 红超期 / 黄临期</p>
            <div ref="progressRowsRef" class="progress-rows">
            <div v-for="o in progressOrders" :key="o.orderNo" class="prog-item">
              <div class="row">
                <span :class="dueClass(o)">
                  {{ o.productName }} · {{ o.orderNo }}
                  <em v-if="o.stuckOperationName" class="stuck-name">待推进：{{ o.stuckOperationName }}</em>
                </span>
                <span>{{ o.doneQty }}/{{ o.qty }}<template v-if="o.progressPercent != null"> · {{ o.progressPercent }}%</template></span>
              </div>
              <div class="bar" :class="dueClass(o)"><i :class="barClass(o)" :style="{ width: pct(o) + '%' }"></i></div>
            </div>
            <p v-if="!progressOrders.length" class="empty">暂无未完工单</p>
            </div>
            <div v-if="pageCount > 1" class="pager">
              <button type="button" @click="changePage(-1)">上一页</button>
              <span>第 {{ pageIndex + 1 }}/{{ pageCount }} 页 · 每10秒翻页</span>
              <button type="button" @click="changePage(1)">下一页</button>
            </div>
          </div>

          <div class="panel defect-panel">
            <h3>本月不良原因 · 报工数量</h3>
            <div v-if="!defectRanks.length" class="empty ok">暂无不良</div>
            <div v-else ref="chartRef" class="chart"></div>
          </div>
        </main>

        <aside class="col-right">
          <div class="panel due-panel">
            <h3>交期预警 <em v-if="dueOrders.length">{{ dueOrderTotal }}</em></h3>
            <p class="caption">优先处理 · 超期优先，同类按交期 · 前6张</p>
            <div v-if="!dueOrders.length" class="empty">暂无交期预警</div>
            <div v-for="o in dueOrders" :key="o.orderNo" class="due-item" :class="dueClass(o)">
              <strong>{{ o.productName }}</strong>
              <span>{{ o.orderNo }}</span>
              <small>{{ dueLabel(o) }} · {{ formatDue(o.dueDate) }}</small>
            </div>
          </div>
        </aside>
      </div>
    </template>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, nextTick, watch } from 'vue'
import { useRouter } from 'vue-router'
import * as echarts from 'echarts'
import { getBoard, getDefectStat } from '@/api/prod'

const factoryName = ref(localStorage.getItem('factoryName') || '')
const showFsMask = ref(true)
const router = useRouter()
function exitScreen() {
  if (document.fullscreenElement) document.exitFullscreen().catch(() => {})
  router.push('/board')
}
const gaugeRef = ref(null)
const chartRef = ref(null)
const now = ref(new Date())
const loading = ref(false)
const hasData = ref(false)
const data = ref({
  totalOrders: 0, doingOrders: 0, doneOrders: 0,
  totalGood: 0, totalDefect: 0, defectRate: 0,
  monthGood: 0, monthDefect: 0,
  dueWarnCount: 0, dueOverdueCount: 0,
  openAbnormalCount: 0, abnormalTicker: [], orders: []
})
const defectRanks = ref([])
let gauge = null
let chart = null
let timer = null
let clock = null
let pageTimer = null
let loadingRequest = false
let pageObserver = null
const progressRowsRef = ref(null)
const pageSize = ref(6)
const pageIndex = ref(0)
const lastUpdatedAt = ref(null)
const refreshError = ref('')
const refreshStatus = computed(() => refreshError.value
  ? '刷新失败 · ' + refreshError.value + (lastUpdatedAt.value ? ' · 上次成功 ' + lastUpdatedAt.value.toLocaleTimeString('zh-CN', { hour12: false }) : '')
  : lastUpdatedAt.value ? '数据更新 ' + lastUpdatedAt.value.toLocaleTimeString('zh-CN', { hour12: false }) + ' · 每10秒刷新' : '正在连接数据…')

const notStarted = computed(() =>
  Math.max(0, (data.value.totalOrders || 0) - (data.value.doingOrders || 0) - (data.value.doneOrders || 0))
)

/** 未结束：0 未开始、1 执行中。已完成(2)、已取消(3)不算。缺状态时先当未结束，避免接口未更新时整表空白。 */
function isOpenOrder(o) {
  if (o == null || o.status == null || o.status === '') return true
  return Number(o.status) < 2
}

/** 在制工单数量完成率：只加总未结束工单的完成数 / 计划数。已结束的单不掺进来。 */
const achieveRate = computed(() => {
  const orders = (data.value.orders || []).filter(isOpenOrder)
  let qty = 0
  let done = 0
  for (const o of orders) {
    qty += Number(o.qty) || 0
    done += Number(o.doneQty) || 0
  }
  if (qty <= 0) return 0
  return Math.min(100, Math.round((done / qty) * 100))
})

const achieveTone = computed(() => {
  const rate = achieveRate.value
  if (rate < 50) return 'bad'
  if (rate < 80) return 'warn'
  return 'ok'
})

function achieveColor(tone) {
  if (tone === 'bad') return '#FF4D4F'
  if (tone === 'warn') return '#FFB020'
  return '#52E08A'
}

/** 设备故障优先，最多钉 3 条，不滚动。 */
const alertLines = computed(() => {
  const list = [...(data.value.abnormalTicker || [])]
  const rank = { 1: 0, 2: 1, 3: 2 }
  list.sort((a, b) => (rank[a.abnormalType] ?? 9) - (rank[b.abnormalType] ?? 9))
  return list.slice(0, 3)
})

const extraAlertCount = computed(() =>
  Math.max(0, (data.value.openAbnormalCount || 0) - alertLines.value.length)
)

const stuckOps = computed(() => {
  const map = new Map()
  for (const o of data.value.orders || []) {
    if (!isOpenOrder(o)) continue
    const name = o.stuckOperationName
    if (!name) continue
    map.set(name, (map.get(name) || 0) + 1)
  }
  return [...map.entries()]
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name, 'zh'))
    .slice(0, 6)
})

const openOrders = computed(() => (data.value.orders || []).filter(isOpenOrder))

const pageCount = computed(() => Math.max(1, Math.ceil(openOrders.value.length / pageSize.value)))
const progressOrders = computed(() => openOrders.value.slice(pageIndex.value * pageSize.value, (pageIndex.value + 1) * pageSize.value))
function changePage(step) { pageIndex.value = (pageIndex.value + step + pageCount.value) % pageCount.value }
watch(pageCount, count => { pageIndex.value = Math.min(pageIndex.value, count - 1) })
function measureRows() {
  if (!progressRowsRef.value) return
  pageSize.value = Math.max(1, Math.floor(progressRowsRef.value.clientHeight / 62))
}

const stuckReady = computed(() =>
  (data.value.orders || []).some(o => Object.prototype.hasOwnProperty.call(o, 'stuckOperationName'))
)

const allDueOrders = computed(() => openOrders.value.filter(o => o.dueState === 'overdue' || o.dueState === 'warning'))
const dueOrderTotal = computed(() => allDueOrders.value.length)
const dueOrders = computed(() => allDueOrders.value.slice(0, 6))
function alertDue(a) {
  const order = (data.value.orders || []).find(o => o.orderNo === a.orderNo)
  return order?.dueDate ? '当前交期 ' + formatDue(order.dueDate) + ' · ' + (dueLabel(order) || '正常') : ''
}

const today = computed(() => {
  const d = now.value
  const w = ['日', '一', '二', '三', '四', '五', '六']
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} 星期${w[d.getDay()]}`
})

const clockText = computed(() => {
  const d = now.value
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
})

onMounted(async () => {
  if (document.fullscreenElement) showFsMask.value = false
  document.addEventListener('fullscreenchange', onFsChange)
  clock = setInterval(() => { now.value = new Date() }, 1000)
  await load()
  timer = setInterval(load, 10000)
  pageTimer = setInterval(() => changePage(1), 10000)
  pageObserver = new ResizeObserver(() => { measureRows(); onResize() })
  if (progressRowsRef.value) pageObserver.observe(progressRowsRef.value)
  measureRows()
  window.addEventListener('resize', onResize)
})

onUnmounted(() => {
  clearInterval(timer)
  clearInterval(clock)
  clearInterval(pageTimer)
  pageObserver?.disconnect()
  window.removeEventListener('resize', onResize)
  document.removeEventListener('fullscreenchange', onFsChange)
  gauge?.dispose()
  chart?.dispose()
  gauge = null
  chart = null
})

watch(achieveRate, () => { nextTick(renderGauge) })

function pad(n) { return String(n).padStart(2, '0') }
function onFsChange() {
  showFsMask.value = !document.fullscreenElement
  nextTick(() => { onResize() })
}
async function enterFullscreen() {
  try {
    await document.documentElement.requestFullscreen()
  } catch {
    showFsMask.value = false
  }
}
function onResize() {
  gauge?.resize()
  chart?.resize()
}
function pct(o) {
  if (o.progressPercent != null) return o.progressPercent
  return !o.qty ? 0 : Math.min(100, Math.round(o.doneQty / o.qty * 100))
}
function dueClass(o) {
  return o.dueState === 'overdue' ? 'due-overdue' : o.dueState === 'warning' ? 'due-warning' : ''
}
function barClass(o) {
  const cls = []
  const due = dueClass(o)
  if (due) cls.push(due)
  if (pct(o) >= 100) cls.push('full')
  return cls
}
function dueLabel(o) {
  const label = o.dueState === 'overdue' ? '已超期' : o.dueState === 'warning' ? '临期' : ''
  if (!label || !o.dueDate) return label
  const date = String(o.dueDate).slice(0, 10)
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return label
  const [year, month, day] = date.split('-').map(Number)
  const today = now.value
  const days = Math.round((Date.UTC(year, month - 1, day) - Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())) / 86400000)
  return days < 0 ? label + ' ' + Math.abs(days) + ' 天' : days === 0 ? label + ' · 今日交期' : label + ' · 剩余 ' + days + ' 天'
}
function formatTime(t) { return t ? String(t).replace('T', ' ').slice(5, 16) : '' }
function formatDue(t) {
  if (!t) return '—'
  const s = String(t).replace('T', ' ')
  return s.length >= 10 ? s.slice(0, 10) : s
}
function formatNum(n) {
  return Number(n || 0).toLocaleString('zh-CN')
}
function formatMonth(n) {
  if (n == null || n === '') return '—'
  return formatNum(n)
}

async function load() {
  if (loadingRequest) return
  loadingRequest = true
  if (!hasData.value) loading.value = true
  try {
    const [boardRes, defectRes] = await Promise.all([
      getBoard(),
      getDefectStat({ period: 'month' })
    ])
    if (boardRes.code !== 0 || defectRes.code !== 0) throw new Error(boardRes.code !== 0 ? boardRes.msg : defectRes.msg)
    data.value = {
      ...boardRes.data,
      openAbnormalCount: boardRes.data.openAbnormalCount || 0,
      abnormalTicker: boardRes.data.abnormalTicker || []
    }
    const rank = { overdue: 0, warning: 1, normal: 2 }
    data.value.orders = (data.value.orders || []).slice().sort(
      (a, b) => (rank[a.dueState] ?? 3) - (rank[b.dueState] ?? 3)
        || String(a.dueDate || '9999').localeCompare(String(b.dueDate || '9999'))
        || String(a.orderNo).localeCompare(String(b.orderNo))
    )
    defectRanks.value = (defectRes.data?.distribution || [])
      .filter(d => (d.totalDefect || 0) > 0)
      .slice(0, 8)
    hasData.value = true
    refreshError.value = ''
    lastUpdatedAt.value = new Date()
    await nextTick()
    measureRows()
    if (pageObserver && progressRowsRef.value) pageObserver.observe(progressRowsRef.value)
    renderGauge()
    renderDefectChart()
  } catch (error) {
    refreshError.value = error?.message || '数据请求失败'
    console.error('车间大屏刷新失败', error)
  } finally {
    loadingRequest = false
    loading.value = false
  }
}

function renderGauge() {
  if (!gaugeRef.value) return
  if (!gauge) gauge = echarts.init(gaugeRef.value)
  const rate = achieveRate.value
  const color = achieveColor(achieveTone.value)
  gauge.setOption({
    series: [{
      type: 'gauge',
      startAngle: 210,
      endAngle: -30,
      min: 0,
      max: 100,
      radius: '88%',
      center: ['50%', '58%'],
      progress: {
        show: true,
        width: 14,
        itemStyle: { color }
      },
      axisLine: {
        lineStyle: { width: 16, color: [[1, 'rgba(54,224,255,0.12)']] }
      },
      axisTick: { show: false },
      splitLine: { show: false },
      axisLabel: { show: false },
      pointer: { show: false },
      anchor: { show: false },
      title: {
        show: true,
        offsetCenter: [0, '68%'],
        color: 'rgba(180,210,240,0.75)',
        fontSize: 16
      },
      detail: {
        valueAnimation: true,
        formatter: '{value}%',
        color,
        fontSize: 44,
        fontFamily: '"DIN Alternate", "Roboto Mono", system-ui, sans-serif',
        fontWeight: 700,
        offsetCenter: [0, '8%']
      },
      data: [{ value: rate, name: '在制工单数量完成率' }]
    }]
  }, true)
  gauge.resize()
}

function renderDefectChart() {
  if (!defectRanks.value.length) {
    chart?.dispose()
    chart = null
    return
  }
  if (!chartRef.value) return
  if (!chart) chart = echarts.init(chartRef.value)
  const names = defectRanks.value.map(d => d.defectName || '(未填原因)').reverse()
  const values = defectRanks.value.map(d => d.totalDefect || 0).reverse()
  chart.setOption({
    grid: { left: 100, right: 48, top: 8, bottom: 8 },
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
    xAxis: {
      type: 'value',
      axisLabel: { color: 'rgba(180,210,240,0.65)', fontSize: 14 },
      splitLine: { lineStyle: { color: 'rgba(54,224,255,0.12)', type: 'dashed' } }
    },
    yAxis: {
      type: 'category',
      data: names,
      axisLabel: { color: '#D6E8FF', width: 90, overflow: 'truncate', fontSize: 14 },
      axisTick: { show: false },
      axisLine: { show: false }
    },
    series: [{
      type: 'bar',
      data: values,
      barMaxWidth: 18,
      itemStyle: { color: '#FF4D4F', borderRadius: [0, 4, 4, 0] },
      label: { show: true, position: 'right', color: 'rgba(180,210,240,0.75)', fontSize: 14 }
    }]
  }, true)
  chart.resize()
}
</script>

<style scoped>
.screen {
  --bg: #0B1E3F;
  --card: rgba(20, 50, 100, 0.4);
  --border: rgba(54, 224, 255, 0.3);
  --cyan: #36E0FF;
  --warn: #FFB020;
  --danger: #FF4D4F;
  --ok: #52E08A;
  --text: #D6E8FF;
  --muted: rgba(180, 210, 240, 0.65);
  min-height: 100vh;
  height: 100vh;
  overflow: hidden;
  box-sizing: border-box;
  padding: 16px 20px 20px;
  color: var(--text);
  font-family: "DIN Alternate", "Roboto Mono", system-ui, sans-serif;
  background:
    radial-gradient(ellipse at 50% 30%, rgba(30, 80, 160, 0.35) 0%, transparent 55%),
    var(--bg);
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.screen.loading { justify-content: flex-start; }
.load-mask {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--muted);
  font-size: 20px;
}

.fs-mask {
  position: fixed;
  inset: 0;
  z-index: 1000;
  background: rgba(5, 12, 28, 0.72);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.fs-card {
  text-align: center;
  padding: 40px 56px;
  border: 1px solid var(--border);
  border-radius: 12px;
  background: rgba(15, 40, 80, 0.9);
}
.fs-title { font-size: 28px; color: var(--cyan); font-weight: 700; }
.fs-sub { margin-top: 12px; font-size: 16px; color: var(--muted); }

.top {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  gap: 12px;
  flex: none;
}
.top-left { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
.factory {
  font-size: 22px;
  font-weight: 700;
  color: var(--cyan);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.date { font-size: 16px; color: var(--muted); }
.top-center {
  margin: 0;
  font-size: 32px;
  font-weight: 700;
  letter-spacing: 0.12em;
  color: #E8F4FF;
  text-align: center;
  white-space: nowrap;
}
.top-right {
  justify-self: end;
  display: flex;
  align-items: center;
  gap: 16px;
  font-size: 40px;
  font-weight: 700;
  color: var(--cyan);
  font-variant-numeric: tabular-nums;
  letter-spacing: 0.04em;
}
.exit-btn {
  flex: none;
  font-size: 14px;
  font-weight: 400;
  color: rgba(180, 210, 240, 0.35);
  background: rgba(20, 50, 100, 0.3);
  border: 1px solid rgba(54, 224, 255, 0.2);
  border-radius: 6px;
  padding: 6px 12px;
  cursor: pointer;
  opacity: 0.35;
  transition: opacity .2s, color .2s, background .2s;
}
.exit-btn:hover {
  opacity: 1;
  color: #FF6B6B;
  background: rgba(255, 77, 79, 0.15);
  border-color: rgba(255, 77, 79, 0.5);
}

.alerts {
  flex: none;
  display: flex;
  align-items: stretch;
  border-radius: 8px;
  overflow: hidden;
  border: 1px solid var(--danger);
  background: #2A0E14;
}
.alerts.ok {
  border-color: var(--ok);
  background: rgba(82, 224, 138, 0.12);
  min-height: 40px;
  align-items: center;
}
.alerts-label {
  flex: none;
  background: var(--danger);
  color: #fff;
  font-weight: 700;
  padding: 10px 16px;
  display: flex;
  align-items: center;
  font-size: 18px;
}
.alerts-list { flex: 1; min-width: 0; padding: 6px 12px; }
.alert-line {
  display: grid;
  grid-template-columns: 88px minmax(0, 1fr) auto auto;
  gap: 12px;
  align-items: start;
  min-height: 32px;
  color: #fff;
  font-size: 16px;
  font-weight: 600;
}
.alert-line em {
  font-style: normal;
  font-size: 14px;
  font-weight: 700;
  text-align: center;
  border-radius: 4px;
  padding: 2px 0;
  margin-top: 2px;
  background: var(--danger);
}
.alert-line em.t2 { background: var(--warn); color: #0B1E3F; }
.alert-line .desc {
  white-space: normal;
  overflow-wrap: anywhere;
  line-height: 1.45;
}
.alert-line .meta {
  color: rgba(255, 220, 220, 0.85);
  font-variant-numeric: tabular-nums;
  font-weight: 500;
  margin-top: 2px;
  white-space: nowrap;
}
.alert-more {
  font-size: 14px;
  color: rgba(255, 180, 180, 0.8);
  padding: 2px 0 4px;
}
.ok-msg { padding: 8px 16px; color: var(--ok); font-size: 18px; font-weight: 700; }

.body {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 280px 1fr 300px;
  gap: 14px;
}

.col-left, .col-center, .col-right {
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.stat-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}
.stat {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 14px 10px;
  text-align: center;
}
.stat .n {
  font-size: 56px;
  font-weight: 700;
  color: var(--cyan);
  line-height: 1.05;
  font-variant-numeric: tabular-nums;
}
.stat .n.warn { color: var(--warn); }
.stat .n.danger { color: var(--danger); }
.stat .l { margin-top: 6px; font-size: 16px; color: var(--muted); }

.output-stack {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.output-card {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 12px 16px;
  text-align: center;
}
.output-card .l { font-size: 16px; color: var(--muted); margin-bottom: 4px; }
.output-card .n {
  font-size: 44px;
  font-weight: 700;
  line-height: 1.05;
  font-variant-numeric: tabular-nums;
}
.output-card.good .n { color: var(--cyan); }
.output-card.bad .n { color: var(--danger); }

.panel {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 14px 16px;
  min-height: 0;
}
.panel h3 {
  margin: 0 0 10px;
  font-size: 18px;
  font-weight: 700;
  color: var(--text);
  display: flex;
  align-items: baseline;
  gap: 8px;
}
.panel h3 em {
  font-style: normal;
  font-size: 16px;
  color: var(--danger);
  font-weight: 700;
}
.caption { margin: -4px 0 12px; font-size: 14px; color: var(--muted); }

.gauge-panel {
  flex: none;
  display: grid;
  grid-template-columns: minmax(260px, 1fr) minmax(240px, 1fr);
  gap: 8px;
  align-items: center;
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 4px 16px 8px 8px;
}
.gauge-wrap { position: relative; min-width: 0; }
.gauge { width: 100%; height: 132px; }
.gauge-note {
  margin-top: -8px;
  text-align: center;
  font-size: 14px;
  color: var(--muted);
}
.gauge-side { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
.side-title { font-size: 16px; font-weight: 700; color: var(--text); }
.side-empty { font-size: 14px; color: var(--muted); padding: 8px 0; }
.stuck-row {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  align-items: baseline;
  font-size: 16px;
  padding: 4px 0;
  border-bottom: 1px solid rgba(54, 224, 255, 0.12);
}
.stuck-row span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--text);
}
.stuck-row strong {
  flex: none;
  font-size: 22px;
  color: var(--warn);
  font-variant-numeric: tabular-nums;
}

.progress-list { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
.progress-rows { flex: 1; min-height: 0; overflow: auto; }
.prog-item { min-height: 62px; box-sizing: border-box; }
.pager { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding-top: 8px; font-size: 14px; color: var(--muted); }
.pager button { color: var(--cyan); border: 1px solid var(--border); background: var(--card); padding: 4px 12px; border-radius: 4px; cursor: pointer; }
.group-title { margin: 0; font-size: 16px; color: var(--text); }
.production-stats { grid-template-columns: repeat(3, 1fr); }
.production-stats .n { font-size: 38px; }
.stat { padding: 10px 6px; }
.side-note { margin: 0; font-size: 13px; color: var(--muted); }
.gauge-side { display: grid; grid-template-columns: 1fr 1fr; column-gap: 20px; }
.gauge-side .side-title, .gauge-side .side-note, .gauge-side .side-empty { grid-column: 1 / -1; }
.refresh-status { font-size: 13px; color: var(--ok); max-width: 480px; overflow-wrap: anywhere; }
.refresh-status.failed { color: var(--danger); }
.alert-due { display: block; font-size: 12px; color: var(--warn); }
.alert-line { min-height: 40px; }
@media (max-height: 850px) {
  .screen { padding: 10px 14px; gap: 8px; }
  .col-left, .col-center, .col-right { gap: 8px; }
}
.prog-item { padding: 6px 0; border-bottom: 1px solid rgba(54, 224, 255, 0.12); }
.prog-item:last-child { border-bottom: none; }
.row {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  font-size: 18px;
  margin-bottom: 6px;
  color: var(--text);
}
.row span:first-child {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.row span:last-child { color: var(--muted); flex: none; font-variant-numeric: tabular-nums; }
.stuck-name {
  font-style: normal;
  margin-left: 8px;
  color: var(--warn);
  font-size: 16px;
  font-weight: 700;
}
.due-warning { color: var(--warn); }
.due-overdue { color: var(--danger); font-weight: 700; }
.bar {
  height: 12px;
  background: rgba(54, 224, 255, 0.12);
  border-radius: 7px;
  overflow: hidden;
}
.bar.due-overdue { background: rgba(255, 77, 79, 0.28); }
.bar.due-warning { background: rgba(255, 176, 32, 0.22); }
.bar i {
  display: block;
  height: 100%;
  background: var(--cyan);
  border-radius: 7px;
}
.bar i.due-overdue { background: var(--danger); }
.bar i.due-warning { background: var(--warn); }
.bar i.full { background: var(--ok); }

.defect-panel { flex: none; }
.chart { height: 150px; width: 100%; }
.defect-panel .empty.ok { color: var(--ok); font-weight: 700; }

.due-panel { flex: 1; overflow: auto; }
.due-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 0;
  border-bottom: 1px solid rgba(54, 224, 255, 0.12);
}
.due-item:last-child { border-bottom: none; }
.due-item strong { font-size: 22px; color: inherit; }
.due-item span { font-size: 18px; color: var(--text); font-weight: 400; }
.due-item small { font-size: 14px; color: var(--muted); font-weight: 400; }
.due-item.due-overdue small { color: var(--danger); }
.due-item.due-warning small { color: var(--warn); }

.empty {
  color: var(--muted);
  text-align: center;
  padding: 24px;
  margin: 0;
  font-size: 16px;
}

@media (max-width: 1400px) {
  .body { grid-template-columns: 240px 1fr 260px; }
  .stat .n { font-size: 48px; }
  .production-stats .n { font-size: 32px; }
  .output-card .n { font-size: 36px; }
  .top-center { font-size: 26px; }
  .top-right { font-size: 32px; }
}
@media (max-height: 850px) {
  .chart { height: 90px; }
}
</style>
