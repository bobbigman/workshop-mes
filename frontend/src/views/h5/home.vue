<template>
  <div class="h5-page">
    <div class="header">
      <FactoryBadge />
      <h2>工作台</h2>
      <span class="login-user">{{ userName }}</span>
      <H5LogoutBtn />
    </div>

    <button
      v-if="canScan"
      type="button"
      class="scan-btn"
      @click="router.push('/h5/scan')"
    >扫码报工</button>

    <section v-if="canWage" class="today-card">
      <div v-if="summaryLoading" role="status">今日计件加载中…</div>
      <div v-else-if="summaryError" role="alert">今日计件加载失败 <button type="button" @click="loadTodaySummary">重试</button></div>
      <template v-else>
        <div class="today-label">今日良品报工累计 <b>{{ todayGoodQty }}</b></div>
        <div class="today-val">预计计件收入 ￥{{ formatMoney(todayWage) }}</div>
        <div class="today-tip">含已复核和待复核，以工资单为准</div>
      </template>
    </section>

    <p v-if="loadError" role="alert">任务加载失败 <button type="button" @click="load">重试</button></p>
    <section class="block">
      <h3 class="block-title">我的任务</h3>
      <div v-if="tasksLoading" class="empty" role="status">任务加载中…</div>
      <div v-else-if="!loadError && list.length === 0" class="empty">暂无派给我的或本班组任务<span v-if="canScan">，可扫码报工</span><span v-if="viewReady && showOrdersEntry">，或从工单入口查找</span></div>
      <div
        v-for="row in list"
        :key="row.taskId"
        class="card"
        @click="goReport(row)"
      >
        <div class="row1">
          <span class="tag" :class="'s' + row.orderStatus">{{ statusLabel(row.orderStatus) }}</span>
          <span class="no">工单 {{ row.orderNo }}</span>
        </div>
        <div class="product"><template v-if="row.productCode">{{ row.productCode }} · </template>{{ row.productName }} · {{ row.operationName }}</div>
        <div class="meta">
          <span class="remain">剩余 <b>{{ row.remainQty }}</b></span>
          <span class="time" :class="dueClass(row)">{{ row.dueState === 'overdue' ? '已逾期 · ' : '交期 ' }}{{ formatDue(row.dueDate) }}</span>
        </div>
        <div class="go">去报工 ›</div>
      </div>
    </section>

    <section class="block">
      <h3 class="block-title">快捷入口</h3>
      <p v-if="viewError" role="alert">工单入口配置加载失败 <button type="button" @click="refreshViewMode">重试</button></p>
      <div class="shortcuts">
        <button
          v-if="viewReady && showOrdersEntry"
          type="button"
          class="shortcut"
          @click="router.push('/h5/orders')"
        >工单</button>
        <button type="button" class="shortcut" @click="router.push('/h5/my-reports')">我的报工</button>
        <button
          v-if="canWage"
          type="button"
          class="shortcut wage"
          @click="router.push('/h5/my-wage')"
        >我的工资</button>
      </div>
      <div class="secondary-entries">
        <button type="button" class="shortcut" @click="router.push('/h5/abnormal')">上报异常</button>
        <router-link class="shortcut" :to="{ path: '/help', query: { topic: 'mobile/登录和找到自己的工单' } }">帮助</router-link>
      </div>
      <!-- docs/141：管理员从报工端回 PC，不起眼小入口 -->
      <button
        v-if="role !== 2"
        type="button"
        class="admin-back"
        @click="router.push('/order')"
      >管理后台</button>
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
import { getWorkerView, getTodayPieceSummary } from '@/api/prod'
import TabBar from './TabBar.vue'
import FactoryBadge from '@/components/FactoryBadge.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'
import PwaGuide from '@/components/PwaGuide.vue'
import { canUse, FEATURE, currentTier } from '@/utils/licenseTier'
import { setWorkerViewMode, isWorkerTasksOnlyView } from '@/utils/workerView'

const router = useRouter()
const list = ref([])
const loadError = ref(false)
const tasksLoading = ref(true)
const summaryLoading = ref(true)
const summaryError = ref(false)
const viewReady = ref(false)
const viewError = ref(false)
const userName = localStorage.getItem('userName') || '当前用户'
const showPwaGuide = ref(false)
const showOrdersEntry = ref(false)
const role = Number(localStorage.getItem('role') || 0)
const canScan = canUse(currentTier(), FEATURE.ScanReport)
const canWage = canUse(currentTier(), FEATURE.PieceWage)
const todayGoodQty = ref(0)
const todayWage = ref(0)

function formatMoney(n) {
  return Number(n || 0).toFixed(2)
}

const STATUS = { 0: '未开始', 1: '执行中', 2: '已结束', 3: '已取消' }
function statusLabel(s) { return STATUS[s] ?? s }
function formatDue(t) { return t ? String(t).replace('T', ' ').slice(0, 10) : '—' }
function dueClass(row) { return row.dueState === 'overdue' ? 'due-overdue' : row.dueState === 'warning' ? 'due-warning' : '' }

function goReport(row) {
  router.push({ path: '/h5/report', query: { order: row.orderNo, task: row.taskId } })
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
  refreshViewMode()
  load()
  loadTodaySummary()
  maybeShowPwaGuide()
})

async function refreshViewMode() {
  viewReady.value = false
  viewError.value = false
  try {
    const res = await getWorkerView()
    setWorkerViewMode(res.data?.workerViewMode)
    showOrdersEntry.value = !isWorkerTasksOnlyView()
    viewReady.value = true
  } catch (error) {
    viewError.value = true
    console.error('[工人视角] 读取工厂视角配置失败，暂不显示工单入口', error)
  }
}

async function load() {
  tasksLoading.value = true
  loadError.value = false
  try { const res = await myTasks(); list.value = res.data || [] }
  catch (error) {
    list.value = []; loadError.value = true
    console.error('[工作台] 加载我的任务失败', error)
  } finally { tasksLoading.value = false }
}

async function loadTodaySummary() {
  if (!canWage) return
  summaryLoading.value = true
  summaryError.value = false
  try {
    const res = await getTodayPieceSummary()
    todayGoodQty.value = res.data?.todayGoodQty ?? 0
    todayWage.value = res.data?.todayWage ?? 0
  } catch (error) {
    summaryError.value = true
    console.error('[今日计件] 加载失败', error)
  } finally { summaryLoading.value = false }
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
.header { flex-wrap: wrap; display: flex; align-items: center; gap: 10px; margin-bottom: 12px; }
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
.today-card {
  background: var(--app-card);
  border-radius: var(--h5-card-radius);
  padding: 14px 16px;
  margin-bottom: 16px;
  box-shadow: var(--app-shadow);
}
.today-label { font-size: 13px; color: var(--app-text-3); margin-bottom: 4px; }
.today-val { font-size: 20px; font-weight: 700; color: var(--app-primary); }
.today-tip { margin-top: 6px; font-size: 11px; color: var(--app-text-4); }
.block { margin-bottom: 16px; }
.block-title {
  margin: 0 0 10px;
  font-size: 15px;
  font-weight: 600;
  color: var(--app-text-2);
}
.empty { text-align: center; color: var(--app-text-4); padding: 12px 0; font-size: 14px; }
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
.admin-back {
  display: block;
  margin: 14px auto 0;
  padding: 0;
  border: none;
  background: transparent;
  color: var(--app-text-4);
  font-size: 12px;
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 2px;
}

/* 任务首屏减少留白，保留触摸尺寸 */
.scan-btn { height: 48px; margin-bottom: 10px; }
.today-card { padding: 10px 12px; margin-bottom: 12px; }
.today-label, .today-val { display: inline; font-size: 15px; }
.today-label { margin-right: 8px; }
.today-tip { margin-top: 4px; color: var(--app-text-2); }
.card { padding: 12px; margin-bottom: 8px; }
.product { margin-bottom: 6px; }
.go { margin-top: 4px; min-height: 24px; }
.meta { flex-wrap: wrap; }
.login-user { font-size: 13px; overflow-wrap: anywhere; color: var(--app-text-2); }
.secondary-entries { display: flex; gap: 10px; margin-top: 10px; }
.secondary-entries .shortcut { flex: 1; text-align: center; text-decoration: none; box-sizing: border-box; }
.today-label, .today-val { display: block; font-size: 14px; overflow-wrap: anywhere; }
.product, .no { overflow-wrap: anywhere; }
</style>
