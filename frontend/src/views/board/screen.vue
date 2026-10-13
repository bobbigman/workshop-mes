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
      </div>
      <h1 class="top-center">车间生产监控大屏</h1>
      <div class="top-right">
        <span class="clock">{{ clockText }}</span>
        <button type="button" class="exit-btn" title="退出大屏，回到看板管理页" @click="exitScreen">✕ 退出大屏</button>
      </div>
    </header>

    <div v-if="loading && !hasData" class="load-mask">加载中…</div>

    <template v-else>
      <div v-if="data.openAbnormalCount > 0" class="ticker danger-bar">
        <span class="ticker-label">异常 {{ data.openAbnormalCount }}</span>
        <div class="ticker-track">
          <div class="ticker-inner">
            <span v-for="(a, i) in tickerLoop" :key="i" class="ticker-item">
              【{{ a.typeLabel }}】{{ a.description }}
              <template v-if="a.orderNo"> · {{ a.orderNo }}</template>
              · {{ formatTime(a.reportedAt) }}
            </span>
          </div>
        </div>
      </div>
      <div v-else class="ticker ok-bar">
        <span class="ticker-label ok">✓ 正常</span>
        <div class="ticker-track ok"><span class="ok-msg">✓ 现场运行正常</span></div>
      </div>

      <div class="body">
        <aside class="col-left">
          <div class="stat-grid">
            <div class="stat"><div class="n">{{ notStarted }}</div><div class="l">未开始</div></div>
            <div class="stat"><div class="n">{{ data.doingOrders }}</div><div class="l">执行中</div></div>
            <div class="stat"><div class="n">{{ data.doneOrders }}</div><div class="l">已完成</div></div>
            <div class="stat"><div class="n warn">{{ data.dueWarnCount }}</div><div class="l">临期</div></div>
            <div class="stat"><div class="n danger">{{ data.dueOverdueCount }}</div><div class="l">超期</div></div>
            <div class="stat"><div class="n" :class="{ danger: data.openAbnormalCount > 0 }">{{ data.openAbnormalCount || 0 }}</div><div class="l">异常</div></div>
          </div>
          <div class="output-row">
            <div class="output-card good">
              <div class="l">良品</div>
              <div class="n">{{ formatNum(data.totalGood) }}</div>
            </div>
            <div class="output-card bad">
              <div class="l">不良</div>
              <div class="n">{{ formatNum(data.totalDefect) }}</div>
            </div>
          </div>
        </aside>

        <main class="col-center">
          <div class="gauge-panel">
            <div ref="gaugeRef" class="gauge"></div>
            <div class="gauge-side">
              <div class="side-item">
                <div class="side-l">在制</div>
                <div class="side-n">{{ data.doingOrders || 0 }}</div>
              </div>
              <div class="side-item">
                <div class="side-l">已完成</div>
                <div class="side-n done">{{ data.doneOrders || 0 }}</div>
              </div>
            </div>
          </div>

          <div class="panel progress-list">
            <h3>工单达成率 Top8</h3>
            <div v-for="o in progressOrders" :key="o.orderNo" class="prog-item">
              <div class="row">
                <span :class="dueClass(o)">{{ o.orderNo }} · {{ o.productName }}</span>
                <span>{{ o.doneQty }}/{{ o.qty }}<template v-if="o.progressPercent != null"> · {{ o.progressPercent }}%</template></span>
              </div>
              <div class="bar"><i :class="{ full: pct(o) >= 100 }" :style="{ width: pct(o) + '%' }"></i></div>
            </div>
            <p v-if="!progressOrders.length" class="empty">暂无工单</p>
          </div>

          <div class="panel defect-panel">
            <h3>不良品项分布</h3>
            <div v-if="!defectRanks.length" class="empty ok">暂无不良</div>
            <div v-else ref="chartRef" class="chart"></div>
          </div>
        </main>

        <aside class="col-right">
          <div class="panel due-panel">
            <h3>交期预警 <em v-if="dueOrders.length">{{ dueOrders.length }}</em></h3>
            <p class="caption">超期红 / 临期黄 · Top6</p>
            <div v-if="!dueOrders.length" class="empty">暂无交期预警</div>
            <div v-for="o in dueOrders" :key="o.orderNo" class="due-item" :class="dueClass(o)">
              <strong>{{ o.orderNo }}</strong>
              <span>{{ o.productName }}</span>
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
  dueWarnCount: 0, dueOverdueCount: 0,
  openAbnormalCount: 0, abnormalTicker: [], orders: []
})
const defectRanks = ref([])
let gauge = null
let chart = null
let timer = null
let clock = null

const notStarted = computed(() =>
  Math.max(0, (data.value.totalOrders || 0) - (data.value.doingOrders || 0) - (data.value.doneOrders || 0))
)

/** 在制工单完成率：有明细按数量加权；否则按已完成/(执行中+已完成) */
const achieveRate = computed(() => {
  const orders = data.value.orders || []
  let qty = 0
  let done = 0
  for (const o of orders) {
    qty += Number(o.qty) || 0
    done += Number(o.doneQty) || 0
  }
  if (qty > 0) return Math.min(100, Math.round((done / qty) * 100))
  const active = (data.value.doingOrders || 0) + (data.value.doneOrders || 0)
  if (!active) return 0
  return Math.min(100, Math.round(((data.value.doneOrders || 0) / active) * 100))
})

const tickerLoop = computed(() => {
  const list = data.value.abnormalTicker || []
  if (list.length === 0) return []
  return [...list, ...list]
})

const progressOrders = computed(() => (data.value.orders || []).slice(0, 8))

const dueOrders = computed(() =>
  (data.value.orders || [])
    .filter(o => o.dueState === 'overdue' || o.dueState === 'warning')
    .slice(0, 6)
)

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
  window.addEventListener('resize', onResize)
})

onUnmounted(() => {
  clearInterval(timer)
  clearInterval(clock)
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
function dueLabel(o) {
  return o.dueState === 'overdue' ? '已超期' : o.dueState === 'warning' ? '临期' : ''
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

async function load() {
  if (!hasData.value) loading.value = true
  try {
    const [boardRes, defectRes] = await Promise.all([
      getBoard(),
      getDefectStat({ period: 'month' })
    ])
    data.value = {
      ...boardRes.data,
      openAbnormalCount: boardRes.data.openAbnormalCount || 0,
      abnormalTicker: boardRes.data.abnormalTicker || []
    }
    const rank = { overdue: 0, warning: 1, normal: 2 }
    data.value.orders = (data.value.orders || []).slice().sort(
      (a, b) => (rank[a.dueState] ?? 3) - (rank[b.dueState] ?? 3)
    )
    defectRanks.value = (defectRes.data?.distribution || [])
      .filter(d => (d.totalDefect || 0) > 0)
      .slice(0, 8)
    hasData.value = true
    await nextTick()
    renderGauge()
    renderDefectChart()
  } finally {
    loading.value = false
  }
}

function renderGauge() {
  if (!gaugeRef.value) return
  if (!gauge) gauge = echarts.init(gaugeRef.value)
  const rate = achieveRate.value
  gauge.setOption({
    series: [{
      type: 'gauge',
      startAngle: 210,
      endAngle: -30,
      min: 0,
      max: 100,
      radius: '78%',
      center: ['50%', '55%'],
      progress: {
        show: true,
        width: 16,
        itemStyle: {
          color: {
            type: 'linear', x: 0, y: 0, x2: 1, y2: 0,
            colorStops: [
              { offset: 0, color: '#1A8CFF' },
              { offset: 1, color: '#36E0FF' }
            ]
          }
        }
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
        offsetCenter: [0, '72%'],
        color: 'rgba(180,210,240,0.75)',
        fontSize: 18
      },
      detail: {
        valueAnimation: true,
        formatter: '{value}%',
        color: '#36E0FF',
        fontSize: 96,
        fontFamily: '"DIN Alternate", "Roboto Mono", system-ui, sans-serif',
        fontWeight: 700,
        offsetCenter: [0, '5%']
      },
      data: [{ value: rate, name: '今日达成率' }]
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

.ticker {
  display: flex;
  align-items: stretch;
  flex: none;
  border-radius: 8px;
  overflow: hidden;
  border: 1px solid var(--danger);
  min-height: 40px;
}
.ticker.ok-bar { border-color: var(--ok); }
.ticker-label {
  flex: none;
  background: var(--danger);
  color: #fff;
  font-weight: 700;
  padding: 0 16px;
  display: flex;
  align-items: center;
  font-size: 16px;
}
.ticker-label.ok { background: var(--ok); color: #0B1E3F; }
.ticker-track { flex: 1; overflow: hidden; background: #2A0E14; }
.ticker-track.ok {
  background: rgba(82, 224, 138, 0.12);
  display: flex;
  align-items: center;
}
.ok-msg { padding: 10px 16px; color: var(--ok); font-size: 18px; font-weight: 700; }
.ticker-inner {
  display: inline-flex;
  gap: 48px;
  white-space: nowrap;
  padding: 10px 0;
  animation: ticker-scroll 28s linear infinite;
}
.ticker-item { color: #fff; font-size: 16px; font-weight: 600; padding-left: 16px; }
@keyframes ticker-scroll {
  from { transform: translateX(0); }
  to { transform: translateX(-50%); }
}

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

.output-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}
.output-card {
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 16px 12px;
  text-align: center;
}
.output-card .l { font-size: 16px; color: var(--muted); margin-bottom: 6px; }
.output-card .n {
  font-size: 64px;
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
  grid-template-columns: 1fr 160px;
  gap: 12px;
  align-items: center;
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 8px 16px 8px 8px;
  min-height: 280px;
}
.gauge { width: 100%; height: 340px; }
.gauge-side { display: flex; flex-direction: column; gap: 20px; }
.side-item {
  background: rgba(11, 30, 63, 0.45);
  border: 1px solid rgba(54, 224, 255, 0.2);
  border-radius: 8px;
  padding: 16px 12px;
  text-align: center;
}
.side-l { font-size: 16px; color: var(--muted); margin-bottom: 6px; }
.side-n {
  font-size: 48px;
  font-weight: 700;
  color: var(--cyan);
  font-variant-numeric: tabular-nums;
  line-height: 1.1;
}
.side-n.done { color: var(--ok); }

.progress-list { flex: 1; overflow: auto; }
.prog-item { padding: 10px 0; border-bottom: 1px solid rgba(54, 224, 255, 0.12); }
.prog-item:last-child { border-bottom: none; }
.row {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  font-size: 20px;
  margin-bottom: 8px;
  color: var(--text);
}
.row span:last-child { color: var(--muted); flex: none; font-variant-numeric: tabular-nums; }
.due-warning { color: var(--warn); }
.due-overdue { color: var(--danger); font-weight: 700; }
.bar {
  height: 14px;
  background: rgba(54, 224, 255, 0.12);
  border-radius: 7px;
  overflow: hidden;
}
.bar i {
  display: block;
  height: 100%;
  background: var(--cyan);
  border-radius: 7px;
}
.bar i.full { background: var(--ok); }

.defect-panel { flex: none; }
.chart { height: 200px; width: 100%; }
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
.due-item span { font-size: 16px; color: var(--muted); font-weight: 400; }
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
  .output-card .n { font-size: 48px; }
  .top-center { font-size: 26px; }
  .top-right { font-size: 32px; }
}
</style>
