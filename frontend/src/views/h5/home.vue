<template>
  <div class="h5-page">
    <div class="header">
      <FactoryBadge />
      <h2>工作台</h2>
      <H5LogoutBtn />
    </div>

    <button
      v-if="canScan"
      type="button"
      class="scan-btn"
      @click="router.push('/h5/scan')"
    >去扫码报工</button>

    <section class="block">
      <h3 class="block-title">我的任务</h3>
      <div v-if="list.length === 0" class="empty">暂无派给我的任务，可扫码报工</div>
      <div
        v-for="row in list"
        :key="row.taskId"
        class="card"
        @click="goReport(row)"
      >
        <div class="row1">
          <span class="tag" :class="'s' + row.orderStatus">{{ statusLabel(row.orderStatus) }}</span>
          <span class="no">{{ row.orderNo }}</span>
        </div>
        <div class="product">{{ row.productName }} · {{ row.operationName }}</div>
        <div class="meta">
          <span class="remain">剩余 <b>{{ row.remainQty }}</b></span>
          <span class="time" :class="dueClass(row)">交期 {{ formatDue(row.dueDate) }}</span>
        </div>
        <div class="go">去报工 ›</div>
      </div>
    </section>

    <section class="block">
      <h3 class="block-title">快捷入口</h3>
      <div class="shortcuts">
        <button type="button" class="shortcut" @click="router.push('/h5/orders')">工单</button>
        <button type="button" class="shortcut" @click="router.push('/h5/my-reports')">我的报工</button>
        <button
          v-if="canWage"
          type="button"
          class="shortcut wage"
          @click="router.push('/h5/my-wage')"
        >我的工资</button>
      </div>
    </section>

    <TabBar active="home" />
    <PwaGuide
      v-if="showPwaGuide"
      @dismiss-this-time="onPwaDismissThisTime"
      @dismiss-forever="onPwaDismissForever"
    />
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { myTasks } from '@/api/assign'
import TabBar from './TabBar.vue'
import FactoryBadge from '@/components/FactoryBadge.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'
import PwaGuide from '@/components/PwaGuide.vue'
import { canUse, FEATURE, currentTier } from '@/utils/licenseTier'

const router = useRouter()
const list = ref([])
const showPwaGuide = ref(false)
const canScan = canUse(currentTier(), FEATURE.ScanReport)
const canWage = canUse(currentTier(), FEATURE.PieceWage)

const STATUS = { 0: '未开始', 1: '执行中', 2: '已结束', 3: '已取消' }
function statusLabel(s) { return STATUS[s] ?? s }
function formatDue(t) { return t ? String(t).replace('T', ' ').slice(0, 16) : '—' }
function dueClass(row) { return row.dueState === 'overdue' ? 'due-overdue' : row.dueState === 'warning' ? 'due-warning' : '' }

function goReport(row) {
  router.push({ path: '/h5/report', query: { order: row.orderNo } })
}

function maybeShowPwaGuide() {
  try {
    if (Number(localStorage.getItem('role') || 0) !== 2) return
    if (window.matchMedia('(display-mode: standalone)').matches) return
    if (localStorage.getItem('pwaGuideDismissed') === '1') return
    showPwaGuide.value = true
  } catch (error) {
    console.error('[PwaGuide] 引导渲染失败', error)
  }
}

function onPwaDismissThisTime() {
  showPwaGuide.value = false
}

function onPwaDismissForever() {
  localStorage.setItem('pwaGuideDismissed', '1')
  showPwaGuide.value = false
}

onMounted(() => {
  load()
  maybeShowPwaGuide()
})

async function load() {
  const res = await myTasks()
  list.value = res.data || []
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
.header { display: flex; align-items: center; gap: 10px; margin-bottom: 12px; }
h2 { margin: 0; font-size: 18px; flex: none; color: var(--app-text-1); }
.scan-btn {
  width: 100%;
  height: 48px;
  margin-bottom: 16px;
  border: 0;
  border-radius: var(--h5-card-radius);
  background: var(--app-primary);
  color: #fff;
  font-size: 17px;
  font-weight: 700;
  cursor: pointer;
}
.scan-btn:active { opacity: 0.9; }
.block { margin-bottom: 16px; }
.block-title {
  margin: 0 0 10px;
  font-size: 15px;
  font-weight: 600;
  color: var(--app-text-2);
}
.empty { text-align: center; color: var(--app-text-4); padding: 28px 0; font-size: 14px; }
.card {
  background: var(--app-card); border-radius: var(--h5-card-radius); padding: var(--h5-card-padding);
  margin-bottom: 10px; box-shadow: var(--app-shadow);
}
.row1 { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
.no { margin-left: auto; font-weight: 700; font-size: 15px; color: var(--app-text-1); word-break: break-all; }
.tag { font-size: 12px; padding: 2px 8px; border-radius: 4px; }
.tag.s0 { background: var(--app-border-light); color: var(--app-text-2); }
.tag.s1 { background: var(--app-primary-light); color: var(--app-primary); }
.tag.s2 { background: color-mix(in srgb, var(--app-success) 14%, var(--app-card)); color: var(--app-success); }
.tag.s3 { background: color-mix(in srgb, var(--app-danger) 14%, var(--app-card)); color: var(--app-danger); }
.product { font-size: 16px; font-weight: 600; color: var(--app-text-1); margin-bottom: 12px; }
.meta { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; }
.remain { font-size: 13px; color: var(--app-text-2); }
.remain b { font-size: 22px; color: var(--app-primary); margin-left: 2px; }
.time { font-size: 12px; color: var(--app-text-3); }
.due-warning { color: var(--app-warning); }
.due-overdue { color: var(--app-danger); font-weight: 600; }
.go { margin-top: 8px; text-align: right; font-size: 14px; color: var(--app-primary); font-weight: 600; }
.shortcuts { display: flex; flex-wrap: wrap; gap: 8px; }
.shortcut {
  flex: 1 1 30%;
  min-width: 96px;
  height: 40px;
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius-sm);
  background: var(--app-card);
  color: var(--app-text-1);
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
}
.shortcut.wage { color: var(--app-primary); border-color: color-mix(in srgb, var(--app-primary) 35%, var(--app-border)); }
</style>
