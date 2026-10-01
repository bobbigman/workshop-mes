<template>
  <div class="h5-page">
    <div class="header">
      <FactoryBadge />
      <h2>工单</h2>
      <button type="button" class="mine-btn" @click="goMine">我的报工</button>
      <button v-if="canWage" type="button" class="mine-btn wage" @click="goWage">我的工资</button>
      <input
        v-model="keyword"
        class="search"
        type="search"
        placeholder="搜索工单号/产品"
        @input="onSearchInput"
        @keyup.enter="loadList"
      />
      <H5LogoutBtn />
    </div>

    <div class="tabs">
      <div
        v-for="t in tabs"
        :key="t.key"
        class="tab"
        :class="{ active: statusTab === t.key }"
        @click="onTab(t.key)"
      >
        {{ t.label }} {{ t.count }}
      </div>
    </div>

    <div v-if="list.length === 0" class="empty">暂无工单</div>
    <div
      v-for="row in list"
      :key="row.id"
      class="card"
      @click="goReport(row)"
    >
      <div class="row1">
        <span class="tag" :class="'s' + row.status">{{ statusLabel(row.status) }}</span>
        <span class="no">{{ row.orderNo }}</span>
      </div>
      <div class="product">
        <span v-if="row.productCode" class="code">{{ row.productCode }}</span>
        {{ row.productName }}
      </div>
      <div class="progress-head">
        <span>已完成</span>
        <span class="val">{{ row.doneQty ?? 0 }} / {{ row.planQty ?? row.qty }}</span>
        <span class="pct">{{ progressPct(row) }}%</span>
      </div>
      <div class="bar" aria-hidden="true">
        <div class="bar-fill" :style="{ width: progressPct(row) + '%' }" />
      </div>
      <div class="meta">
        <span class="remain">剩余 <b>{{ row.remainQty ?? 0 }}</b></span>
        <span class="time" :class="dueClass(row)">交期 {{ formatDue(row.dueDate) }}</span>
      </div>
      <div v-if="opLine(row)" class="op-line">{{ opLine(row) }}</div>
      <div class="go">去报工 ›</div>
    </div>

    <TabBar active="home" />
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { getOrderList, getOrderStatusCounts } from '@/api/prod'
import TabBar from './TabBar.vue'
import FactoryBadge from '@/components/FactoryBadge.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'
import { canUse, FEATURE, currentTier } from '@/utils/licenseTier'

const router = useRouter()
const canWage = canUse(currentTier(), FEATURE.PieceWage)
const statusTab = ref('all')
const keyword = ref('')
const list = ref([])
const counts = ref({ all: 0, notStarted: 0, doing: 0, done: 0 })
let searchTimer = null

const tabs = ref([
  { key: 'all', label: '全部', count: 0 },
  { key: '0', label: '未开始', count: 0 },
  { key: '1', label: '执行中', count: 0 },
  { key: '2', label: '已结束', count: 0 }
])

const STATUS = { 0: '未开始', 1: '执行中', 2: '已结束', 3: '已取消' }
function statusLabel(s) { return STATUS[s] ?? s }
function formatDue(t) { return t ? String(t).replace('T', ' ').slice(0, 16) : '—' }
function dueClass(row) { return row.dueState === 'overdue' ? 'due-overdue' : row.dueState === 'warning' ? 'due-warning' : '' }

function progressPct(row) {
  if (row.progressPercent != null) return row.progressPercent
  const plan = Number(row.planQty ?? row.qty) || 0
  const done = Number(row.doneQty) || 0
  if (plan <= 0) return 0
  return Math.min(100, Math.round((done / plan) * 100))
}

function opLine(row) {
  const ops = row.ops || []
  if (!ops.length) return ''
  const idx = ops.findIndex(o => o.planQty > 0 && (o.doneQty || 0) < o.planQty)
  if (idx < 0) return '工序已完成'
  const cur = ops[idx].operationName
  const next = ops[idx + 1]?.operationName
  return next ? `当前工序：${cur}　下道：${next}` : `当前工序：${cur}`
}

onMounted(async () => {
  await loadCounts()
  await loadList()
})

function syncTabCounts() {
  tabs.value = [
    { key: 'all', label: '全部', count: counts.value.all },
    { key: '0', label: '未开始', count: counts.value.notStarted },
    { key: '1', label: '执行中', count: counts.value.doing },
    { key: '2', label: '已结束', count: counts.value.done }
  ]
}

async function loadCounts() {
  const res = await getOrderStatusCounts()
  const d = res.data || {}
  counts.value = {
    all: d.all || 0,
    notStarted: d.notStarted || 0,
    doing: d.doing || 0,
    done: d.done || 0
  }
  syncTabCounts()
}

async function loadList() {
  const params = { page: 1, pageSize: 100, excludeCancelled: true }
  if (statusTab.value !== 'all') params.status = Number(statusTab.value)
  const kw = keyword.value.trim()
  if (kw) params.keyword = kw
  const res = await getOrderList(params)
  list.value = res.data.list || []
}

function onSearchInput() {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => loadList(), 300)
}

async function onTab(key) {
  statusTab.value = key
  await loadList()
}

function goReport(row) {
  router.push({ path: '/h5/report', query: { order: row.orderNo } })
}

function goMine() {
  router.push('/h5/my-reports')
}

function goWage() {
  router.push('/h5/my-wage')
}
</script>

<style scoped>
.h5-page {
  padding: var(--h5-page-padding);
  padding-bottom: calc(var(--h5-tabbar-height) + 16px + env(safe-area-inset-bottom));
  min-height: 100vh;
  background: var(--app-bg);
  box-sizing: border-box;
}
.header { display: flex; align-items: center; flex-wrap: wrap; gap: 10px; margin-bottom: 12px; }
h2 { margin: 0; font-size: 18px; flex: none; color: var(--app-text-1); }
.mine-btn {
  flex: none; height: 32px; padding: 0 10px; border: 1px solid var(--app-primary);
  border-radius: 16px; background: var(--app-primary-light); color: var(--app-primary);
  font-size: 13px; white-space: nowrap;
}
.search {
  flex: 1; height: var(--h5-control-height); border: 1px solid var(--app-border); border-radius: 24px;
  padding: 0 14px; font-size: 15px; background: var(--app-card); outline: none; color: var(--app-text-1);
}
.tabs { display: flex; gap: 8px; overflow-x: auto; padding-bottom: 4px; margin-bottom: 12px; }
.tab {
  flex: none; padding: 8px 12px; border-radius: 16px; background: var(--app-card);
  font-size: 13px; color: var(--app-text-2); border: 1px solid var(--app-border); white-space: nowrap;
}
.tab.active { background: var(--app-primary); color: var(--app-card); border-color: var(--app-primary); }
.empty { text-align: center; color: var(--app-text-4); padding: 40px 0; }
.card {
  background: var(--app-card); border-radius: var(--h5-card-radius); padding: var(--h5-card-padding);
  margin-bottom: 10px; box-shadow: var(--app-shadow);
}
.row1 { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
.no { margin-left: auto; font-weight: 700; font-size: 15px; color: var(--app-text-1); }
.tag { font-size: 12px; padding: 2px 8px; border-radius: 4px; }
.tag.s0 { background: var(--app-border-light); color: var(--app-text-2); }
.tag.s1 { background: var(--app-primary-light); color: var(--app-primary); }
.tag.s2 { background: color-mix(in srgb, var(--app-success) 14%, var(--app-card)); color: var(--app-success); }
.product { font-size: 16px; font-weight: 600; color: var(--app-text-1); margin-bottom: 12px; }
.product .code { margin-right: 6px; }
.progress-head {
  display: flex; align-items: baseline; gap: 8px;
  font-size: 13px; color: var(--app-text-3);
}
.progress-head .val { font-size: 16px; font-weight: 700; color: var(--app-text-1); }
.progress-head .pct { margin-left: auto; font-weight: 600; color: var(--app-text-2); }
.bar {
  height: 8px; margin: 8px 0 12px; border-radius: 4px; overflow: hidden;
  background: var(--app-border-light);
}
.bar-fill { height: 100%; border-radius: 4px; background: var(--app-primary); }
.meta { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; }
.remain { font-size: 13px; color: var(--app-text-2); }
.remain b { font-size: 22px; color: var(--app-primary); margin-left: 2px; }
.time { font-size: 12px; color: var(--app-text-3); }
.due-warning { color: var(--app-warning); }
.due-overdue { color: var(--app-danger); font-weight: 600; }
.op-line { margin-top: 10px; font-size: 13px; color: var(--app-text-2); line-height: 1.4; }
.go { margin-top: 8px; text-align: right; font-size: 14px; color: var(--app-primary); font-weight: 600; }
</style>
