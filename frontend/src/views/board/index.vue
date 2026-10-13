<template>
  <div class="board" :class="{ loading: loading && !hasData }">
    <header>
      <div class="title-row">
        <button type="button" class="back-btn" @click="goBack">← 返回</button>
        <h1>车间看板</h1>
        <FactoryBadge />
      </div>
      <div class="header-right">
        <button type="button" class="screen-btn" title="挂墙电视大屏" @click="goScreen">📺 大屏模式</button>
        <span class="time">{{ now }}</span>
        <button
          type="button"
          class="help-btn"
          title="本页帮助（F1）"
          aria-label="本页帮助"
          @click="pageHelpRef?.toggle()"
        >？</button>
      </div>
    </header>
    <PageHelp ref="pageHelpRef" :enabled="true" />

    <ValueTip :text="VALUE_TIPS.board" tone="board" />

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
        <span class="ticker-label ok">暂无异常</span>
        <div class="ticker-track ok"><span class="ok-msg">现场运行正常</span></div>
      </div>

      <div class="stats">
        <div class="stat"><div class="n">{{ notStarted }}</div><div class="l">未开始</div></div>
        <div class="stat"><div class="n">{{ data.doingOrders }}</div><div class="l">执行中</div></div>
        <div class="stat"><div class="n">{{ data.doneOrders }}</div><div class="l">已完成</div></div>
        <div class="stat"><div class="n warn">{{ data.dueWarnCount }}</div><div class="l">临期</div></div>
        <div class="stat"><div class="n danger">{{ data.dueOverdueCount }}</div><div class="l">超期</div></div>
        <div class="stat"><div class="n" :class="{ danger: data.openAbnormalCount > 0 }">{{ data.openAbnormalCount || 0 }}</div><div class="l">待处理异常</div></div>
      </div>

      <div class="main">
        <div class="main-left">
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

          <div class="panel progress-list">
            <h3>工单达成率</h3>
            <div v-for="o in progressOrders" :key="o.orderNo" class="prog-item">
              <div class="row">
                <span :class="dueClass(o)">{{ o.orderNo }} · {{ o.productName }}</span>
                <span>{{ o.doneQty }}/{{ o.qty }}<template v-if="o.progressPercent != null"> · {{ o.progressPercent }}%</template></span>
              </div>
              <div class="bar"><i :style="{ width: pct(o) + '%' }"></i></div>
            </div>
            <p v-if="!progressOrders.length" class="empty">暂无工单</p>
          </div>

          <div class="panel defect-panel">
            <h3>不良品项分布</h3>
            <div v-if="!defectRanks.length" class="empty ok">暂无不良</div>
            <div v-else ref="chartRef" class="chart"></div>
          </div>
        </div>

        <aside class="main-right">
          <div class="panel due-panel">
            <h3>交期预警 <em v-if="dueOrders.length">{{ dueOrders.length }}</em></h3>
            <p class="caption">临期 / 超期工单</p>
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
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import * as echarts from 'echarts'
import { getBoard, getDefectStat } from '@/api/prod'
import FactoryBadge from '@/components/FactoryBadge.vue'
import ValueTip from '@/components/ValueTip.vue'
import PageHelp from '@/components/PageHelp.vue'
import { VALUE_TIPS } from '@/constants/valueTips'

const router = useRouter()
const pageHelpRef = ref(null)
function goBack() {
  router.push('/order')
}
function goScreen() {
  router.push('/board/screen')
}

const chartRef = ref(null)
const now = ref('')
const loading = ref(false)
const hasData = ref(false)
const data = ref({
  totalOrders: 0, doingOrders: 0, doneOrders: 0,
  totalGood: 0, totalDefect: 0, defectRate: 0,
  dueWarnCount: 0, dueOverdueCount: 0,
  openAbnormalCount: 0, abnormalTicker: [], orders: []
})
const defectRanks = ref([])
let chart = null
let timer = null
let clock = null

/** 未开始 = 总数 − 执行中 − 已完成（Board 未单独返回 status=0） */
const notStarted = computed(() =>
  Math.max(0, (data.value.totalOrders || 0) - (data.value.doingOrders || 0) - (data.value.doneOrders || 0))
)

const tickerLoop = computed(() => {
  const list = data.value.abnormalTicker || []
  if (list.length === 0) return []
  return [...list, ...list]
})

const progressOrders = computed(() => (data.value.orders || []).slice(0, 12))

const dueOrders = computed(() =>
  (data.value.orders || []).filter(o => o.dueState === 'overdue' || o.dueState === 'warning')
)

onMounted(async () => {
  tickClock()
  clock = setInterval(tickClock, 1000)
  await load()
  timer = setInterval(load, 10000)
  window.addEventListener('resize', onResize)
})

onUnmounted(() => {
  clearInterval(timer)
  clearInterval(clock)
  window.removeEventListener('resize', onResize)
  chart?.dispose()
  chart = null
})

function tickClock() { now.value = new Date().toLocaleString('zh-CN') }
function onResize() { chart?.resize() }
function pct(o) {
  if (o.progressPercent != null) return o.progressPercent
  return !o.qty ? 0 : Math.min(100, Math.round(o.doneQty / o.qty * 100))
}
function dueClass(o) { return o.dueState === 'overdue' ? 'due-overdue' : o.dueState === 'warning' ? 'due-warning' : '' }
function dueLabel(o) { return o.dueState === 'overdue' ? '已超期' : o.dueState === 'warning' ? '临期' : '' }
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
    renderDefectChart()
  } finally {
    loading.value = false
  }
}

function renderDefectChart() {
  if (!defectRanks.value.length) {
    chart?.dispose()
    chart = null
    return
  }
  if (!chartRef.value) return
  if (!chart) chart = echarts.init(chartRef.value)
  const text = themeColor('--app-text-1')
  const muted = themeColor('--app-text-3')
  const danger = themeColor('--app-danger')
  const names = defectRanks.value.map(d => d.defectName || '(未填原因)').reverse()
  const values = defectRanks.value.map(d => d.totalDefect || 0).reverse()
  chart.setOption({
    grid: { left: 88, right: 36, top: 8, bottom: 8 },
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
    xAxis: {
      type: 'value',
      axisLabel: { color: muted },
      splitLine: { lineStyle: { color: 'var(--app-border-light)', type: 'dashed' } }
    },
    yAxis: {
      type: 'category',
      data: names,
      axisLabel: { color: text, width: 80, overflow: 'truncate' },
      axisTick: { show: false },
      axisLine: { show: false }
    },
    series: [{
      type: 'bar',
      data: values,
      barMaxWidth: 18,
      itemStyle: { color: danger, borderRadius: [0, 4, 4, 0] },
      label: { show: true, position: 'right', color: muted, fontSize: 12 }
    }]
  }, true)
  chart.resize()
}

function themeColor(name) {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  if (!value) throw new Error(`车间看板取色失败：documentElement 上没有 CSS 变量 ${name}`)
  return value
}
</script>

<style scoped>
.board {
  min-height: 100vh;
  color: var(--app-text-1);
  padding: var(--app-page-padding-x);
  background: var(--app-bg);
  box-sizing: border-box;
}
.board.loading { min-height: 60vh; }
.load-mask {
  text-align: center;
  color: var(--app-text-3);
  padding: 80px 0;
  font-size: 16px;
}
header { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--app-section-gap); gap: 12px; }
.title-row { display: flex; align-items: center; gap: 12px; min-width: 0; }
.back-btn {
  flex: none; height: var(--app-control-height); padding: 0 12px;
  border: 1px solid var(--app-border); border-radius: var(--app-radius-sm);
  background: var(--app-card); color: var(--app-text-2); font-size: 14px; cursor: pointer;
}
.back-btn:hover { background: var(--app-aside-hover-bg); }
h1 { font-size: 24px; margin: 0; white-space: nowrap; font-weight: 600; color: var(--app-text-1); }
.header-right { display: flex; align-items: center; gap: 8px; flex: none; }
.screen-btn {
  flex: none; height: var(--app-control-height); padding: 0 12px;
  border: 1px solid var(--app-primary); border-radius: var(--app-radius-sm);
  background: color-mix(in srgb, var(--app-primary) 12%, white);
  color: var(--app-primary); font-size: 14px; cursor: pointer; font-weight: 600;
}
.screen-btn:hover { background: color-mix(in srgb, var(--app-primary) 22%, white); }
.time { color: var(--app-text-3); font-size: 14px; }
.help-btn {
  width: 32px; height: 32px; padding: 0;
  border: 1px solid var(--app-border); border-radius: var(--app-radius-sm);
  background: var(--app-card); color: var(--app-text-2); font-size: 16px; cursor: pointer;
  line-height: 1;
}
.help-btn:hover { background: var(--app-aside-hover-bg); color: var(--app-primary); }

.ticker {
  display: flex; align-items: stretch; gap: 0; margin-bottom: var(--app-section-gap);
  border-radius: var(--app-radius); overflow: hidden; border: 1px solid var(--app-danger);
}
.ticker.ok-bar { border-color: var(--app-success, #67c23a); }
.ticker-label {
  flex: none; background: var(--app-danger); color: #fff; font-weight: 700;
  padding: 0 14px; display: flex; align-items: center; font-size: 14px;
}
.ticker-label.ok { background: var(--app-success, #67c23a); }
.ticker-track { flex: 1; overflow: hidden; background: color-mix(in srgb, var(--app-danger) 10%, white); }
.ticker-track.ok { background: color-mix(in srgb, var(--app-success, #67c23a) 12%, white); display: flex; align-items: center; }
.ok-msg { padding: 10px 16px; color: var(--app-success, #67c23a); font-size: 14px; font-weight: 600; }
.ticker-inner {
  display: inline-flex; gap: 48px; white-space: nowrap; padding: 10px 0;
  animation: ticker-scroll 28s linear infinite;
}
.ticker-item { color: var(--app-danger); font-size: 14px; font-weight: 600; padding-left: 16px; }
@keyframes ticker-scroll {
  from { transform: translateX(0); }
  to { transform: translateX(-50%); }
}

.stats { display: grid; grid-template-columns: repeat(6, 1fr); gap: var(--app-section-gap); margin-bottom: var(--app-section-gap); }
.stat {
  background: var(--app-card);
  border-radius: var(--app-radius);
  padding: var(--app-card-padding);
  text-align: center;
  border: 1px solid var(--app-border);
}
.stat .n {
  font-size: 32px;
  font-weight: 700;
  color: var(--app-text-1);
  line-height: 1.15;
  font-variant-numeric: tabular-nums;
}
.stat .n.danger { color: var(--app-danger); }
.stat .n.warn { color: var(--app-warning); }
.stat .l { color: var(--app-text-3); font-size: 14px; margin-top: 8px; }

.main {
  display: grid;
  grid-template-columns: 1fr 280px;
  gap: var(--app-section-gap);
  align-items: start;
}
.main-left { display: flex; flex-direction: column; gap: var(--app-section-gap); min-width: 0; }
.main-right { min-width: 0; }

.output-row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--app-section-gap); }
.output-card {
  background: var(--app-card);
  border-radius: var(--app-radius);
  padding: 20px 24px;
  border: 1px solid var(--app-border);
}
.output-card .l { color: var(--app-text-3); font-size: 14px; margin-bottom: 6px; }
.output-card .n {
  font-size: 40px;
  font-weight: 700;
  line-height: 1.1;
  font-variant-numeric: tabular-nums;
}
.output-card.good .n { color: var(--app-primary); }
.output-card.bad .n { color: var(--app-danger); }

.panel {
  background: var(--app-card);
  border-radius: var(--app-radius);
  padding: var(--app-card-padding);
  border: 1px solid var(--app-border);
}
.panel h3 {
  margin: 0 0 8px;
  font-size: 18px;
  font-weight: 600;
  color: var(--app-text-1);
  display: flex;
  align-items: baseline;
  gap: 8px;
}
.panel h3 em {
  font-style: normal;
  font-size: 14px;
  color: var(--app-danger);
  font-weight: 700;
}
.caption { margin: -4px 0 12px; font-size: 12px; color: var(--app-text-3); }

.prog-item { padding: 12px 0; margin: 0; border-bottom: 1px solid var(--app-border-light); }
.prog-item:last-child { border-bottom: none; }
.row { display: flex; justify-content: space-between; gap: 12px; font-size: 14px; margin-bottom: 8px; color: var(--app-text-1); }
.row span:last-child { color: var(--app-text-2); flex: none; }
.due-warning { color: var(--app-warning); }
.due-overdue { color: var(--app-danger); font-weight: 600; }
.bar { height: 8px; background: var(--app-border-light); border-radius: 4px; overflow: hidden; }
.bar i { display: block; height: 100%; background: var(--app-primary); }

.chart { height: 260px; width: 100%; }
.defect-panel .empty.ok { color: var(--app-success, #67c23a); font-weight: 600; }

.due-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 10px 0;
  border-bottom: 1px solid var(--app-border-light);
}
.due-item:last-child { border-bottom: none; }
.due-item strong { font-size: 14px; color: inherit; }
.due-item span { font-size: 13px; color: var(--app-text-2); font-weight: 400; }
.due-item small { font-size: 12px; color: var(--app-text-3); font-weight: 400; }
.due-item.due-overdue small { color: var(--app-danger); }
.due-item.due-warning small { color: var(--app-warning); }

.empty { color: var(--app-text-3); text-align: center; padding: 20px; margin: 0; }

@media (max-width: 960px) {
  .main { grid-template-columns: 1fr; }
  .stats { grid-template-columns: repeat(3, 1fr); }
}
@media (max-width: 720px) {
  .stats { grid-template-columns: repeat(2, 1fr); }
  .stat .n { font-size: 28px; }
  .output-card .n { font-size: 32px; }
  h1 { font-size: 24px; }
}
</style>
