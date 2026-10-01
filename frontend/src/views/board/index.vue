<template>
  <div class="board">
    <header>
      <div class="title-row">
        <button type="button" class="back-btn" @click="goBack">← 返回</button>
        <h1>车间看板</h1>
        <FactoryBadge />
      </div>
      <div class="header-right">
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

    <div class="stats">
      <div class="stat"><div class="n">{{ data.totalOrders }}</div><div class="l">工单总数</div></div>
      <div class="stat"><div class="n">{{ data.doingOrders }}</div><div class="l">生产中</div></div>
      <div class="stat"><div class="n">{{ data.doneOrders }}</div><div class="l">已完成</div></div>
      <div class="stat"><div class="n warn">{{ data.dueWarnCount }}</div><div class="l">临期工单</div></div>
      <div class="stat"><div class="n danger">{{ data.dueOverdueCount }}</div><div class="l">超期工单</div></div>
      <div class="stat"><div class="n" :class="{ danger: data.openAbnormalCount > 0 }">{{ data.openAbnormalCount || 0 }}</div><div class="l">待处理异常</div></div>
    </div>
    <div ref="chartRef" class="chart"></div>
    <div class="progress-list">
      <h3>工单生产进度</h3>
      <div v-for="o in data.orders" :key="o.orderNo" class="prog-item">
          <div class="row"><span :class="dueClass(o)">{{ o.orderNo }} · {{ o.productName }}</span><span>{{ o.doneQty }}/{{ o.qty }}<template v-if="o.progressPercent != null"> · {{ o.progressPercent }}%</template></span></div>
        <div class="bar"><i :style="{ width: pct(o) + '%' }"></i></div>
      </div>
      <p v-if="!data.orders?.length" class="empty">暂无工单</p>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import * as echarts from 'echarts'
import { getBoard } from '@/api/prod'
import FactoryBadge from '@/components/FactoryBadge.vue'
import ValueTip from '@/components/ValueTip.vue'
import PageHelp from '@/components/PageHelp.vue'
import { VALUE_TIPS } from '@/constants/valueTips'

const router = useRouter()
const pageHelpRef = ref(null)
function goBack() {
  router.push('/order')
}

const chartRef = ref(null)
const now = ref('')
const data = ref({
  totalOrders: 0, doingOrders: 0, doneOrders: 0,
  totalGood: 0, totalDefect: 0, defectRate: 0,
  dueWarnCount: 0, dueOverdueCount: 0,
  openAbnormalCount: 0, abnormalTicker: [], orders: []
})
let chart = null
let timer = null
let clock = null

const tickerLoop = computed(() => {
  const list = data.value.abnormalTicker || []
  if (list.length === 0) return []
  return [...list, ...list]
})

onMounted(async () => {
  tickClock()
  clock = setInterval(tickClock, 1000)
  await load()
  timer = setInterval(load, 10000)
})

onUnmounted(() => {
  clearInterval(timer)
  clearInterval(clock)
  chart?.dispose()
})

function tickClock() { now.value = new Date().toLocaleString('zh-CN') }
function pct(o) {
  if (o.progressPercent != null) return o.progressPercent
  return !o.qty ? 0 : Math.min(100, Math.round(o.doneQty / o.qty * 100))
}
function dueClass(o) { return o.dueState === 'overdue' ? 'due-overdue' : o.dueState === 'warning' ? 'due-warning' : '' }
function formatTime(t) { return t ? String(t).replace('T', ' ').slice(5, 16) : '' }

async function load() {
  const res = await getBoard()
  data.value = {
    ...res.data,
    openAbnormalCount: res.data.openAbnormalCount || 0,
    abnormalTicker: res.data.abnormalTicker || []
  }
  const rank = { overdue: 0, warning: 1, normal: 2 }
  data.value.orders = (data.value.orders || []).slice().sort((a, b) => (rank[a.dueState] ?? 3) - (rank[b.dueState] ?? 3))
  await nextTick()
  renderChart()
}

function renderChart() {
  if (!chartRef.value) return
  if (!chart) chart = echarts.init(chartRef.value)
  const text = themeColor('--app-text-1')
  const good = themeColor('--app-primary')
  const bad = themeColor('--app-danger')
  chart.setOption({
    title: { text: '良品 / 不良品', left: 'center', textStyle: { color: text, fontSize: 16, fontWeight: 600 } },
    tooltip: { trigger: 'item' },
    series: [{
      type: 'pie', radius: ['40%', '65%'],
      label: { color: text },
      data: [
        { name: '良品', value: data.value.totalGood || 0, itemStyle: { color: good } },
        { name: '不良品', value: data.value.totalDefect || 0, itemStyle: { color: bad } }
      ]
    }]
  })
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
.ticker-label {
  flex: none; background: var(--app-danger); color: #fff; font-weight: 700;
  padding: 0 14px; display: flex; align-items: center; font-size: 14px;
}
.ticker-track { flex: 1; overflow: hidden; background: color-mix(in srgb, var(--app-danger) 10%, white); }
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
.chart {
  height: 300px;
  background: var(--app-card);
  border-radius: var(--app-radius);
  margin-bottom: var(--app-section-gap);
  border: 1px solid var(--app-border);
}
.progress-list {
  background: var(--app-card);
  border-radius: var(--app-radius);
  padding: var(--app-card-padding);
  border: 1px solid var(--app-border);
}
.progress-list h3 { margin: 0 0 4px; font-size: 18px; font-weight: 600; color: var(--app-text-1); }
.prog-item { padding: 12px 0; margin: 0; border-bottom: 1px solid var(--app-border-light); }
.prog-item:last-child { border-bottom: none; }
.row { display: flex; justify-content: space-between; gap: 12px; font-size: 14px; margin-bottom: 8px; color: var(--app-text-1); }
.row span:last-child { color: var(--app-text-2); flex: none; }
.due-warning { color: var(--app-warning); }
.due-overdue { color: var(--app-danger); font-weight: 600; }
.bar { height: 8px; background: var(--app-border-light); border-radius: 4px; overflow: hidden; }
.bar i { display: block; height: 100%; background: var(--app-primary); }
.empty { color: var(--app-text-3); text-align: center; padding: 20px; }
@media (max-width: 720px) {
  .stats { grid-template-columns: repeat(2, 1fr); }
  .stat .n { font-size: 28px; }
  h1 { font-size: 24px; }
}
</style>
