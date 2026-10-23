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

    <section class="task-summary" aria-label="我的任务">
      <router-link class="task-entry" to="/h5/tasks">
        <span>
          <strong>我的任务</strong>
          <span v-if="tasksLoading" class="entry-note" role="status">任务加载中…</span>
          <span v-else-if="loadError" class="entry-note">任务数量暂不可用</span>
          <span v-else-if="!list.length" class="entry-note">暂无派给我或本班组的任务</span>
          <span v-else class="entry-note">已派任务 {{ list.length }} 项（含本班组）</span>
        </span>
        <span class="entry-arrow">{{ loadError ? '查看任务' : '查看全部' }} ›</span>
      </router-link>
      <p v-if="loadError" class="load-error" role="alert">任务加载失败 <button type="button" @click="load">重试</button></p>
    </section>

    <section class="block" aria-label="常用功能">
      <h3 class="block-title">常用功能</h3>
      <p v-if="viewError" role="alert">工单入口配置加载失败 <button type="button" @click="refreshViewMode">重试</button></p>
      <div class="shortcuts">
        <router-link v-if="viewReady && showOrdersEntry" class="shortcut" to="/h5/orders">工单</router-link>
        <router-link v-if="canWage" class="shortcut" to="/h5/my-wage">我的工资</router-link>
        <router-link class="shortcut" to="/h5/abnormal">上报异常</router-link>
        <router-link class="shortcut" to="/h5/help">帮助</router-link>
        <router-link class="shortcut" to="/h5/more">更多功能</router-link>
        <button v-if="role === 1 || role === 3" type="button" class="shortcut" @click="managementOpen = true">管理操作</button>
      </div>
    </section>

    <section v-if="canWage" class="today-card" aria-label="今日计件">
      <div v-if="summaryLoading" role="status">今日计件加载中…</div>
      <div v-else-if="summaryError" role="alert">今日计件加载失败 <button type="button" @click="loadTodaySummary">重试</button></div>
      <template v-else>
        <div class="today-label">今日良品报工累计 <b>{{ todayGoodQty }}</b></div>
        <div class="today-val">预计计件收入 ￥{{ formatMoney(todayWage) }}</div>
        <div class="today-tip">含已复核和待复核，以工资单为准</div>
      </template>
    </section>

    <van-action-sheet
      v-model:show="managementOpen"
      title="管理操作"
      :actions="managementActions"
      cancel-text="取消"
      close-on-click-action
      @select="openManagement"
    />

    <TabBar active="home" />
    <PwaGuide
      v-if="showPwaGuide"
      @dismiss-this-time="onPwaDismissThisTime"
      @dismiss-forever="onPwaDismissForever"
    />
  </div>
</template>

<script setup>
import { computed, ref, onMounted } from 'vue'
import { ActionSheet as VanActionSheet } from 'vant'
import 'vant/lib/index.css'
import { useRouter } from 'vue-router'
import { myTasks } from '@/api/assign'
import { getWorkerView, getTodayPieceSummary } from '@/api/prod'
import TabBar from './TabBar.vue'
import FactoryBadge from '@/components/FactoryBadge.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'
import PwaGuide from '@/components/PwaGuide.vue'
import { canUse, FEATURE, currentTier } from '@/utils/licenseTier'
import { setWorkerViewMode, isWorkerTasksOnlyView } from '@/utils/workerView'
import { managementDestination } from '@/utils/reportManagement'

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
const managementOpen = ref(false)
const managementActions = computed(() => {
  if (role !== 1 && role !== 3) return []
  return [
    { name: '代报工', action: 'proxy' },
    ...(role === 1 ? [
      { name: '一键补报未完工序', action: 'batch' },
      { name: '补报开工记录', action: 'pending' }
    ] : []),
    { name: '管理后台', action: 'backend' }
  ]
})

function openManagement(item) {
  if (!managementActions.value.some(option => option.action === item.action)) return
  managementOpen.value = false
  router.push(item.action === 'backend' ? '/order' : managementDestination(item.action))
}
const todayGoodQty = ref(0)
const todayWage = ref(0)

function formatMoney(n) {
  return Number(n || 0).toFixed(2)
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
.header { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-bottom: 12px; }
h2 { margin: 0; font-size: 18px; flex: none; color: var(--app-text-1); }
.login-user { font-size: 13px; overflow-wrap: anywhere; color: var(--app-text-2); }
.scan-btn {
  width: 100%; min-height: 48px; margin-bottom: 12px; padding: 10px;
  border: 0; border-radius: var(--h5-card-radius); background: var(--app-primary);
  color: #fff; font-size: 17px; font-weight: 700; cursor: pointer;
}
.scan-btn:active { opacity: 0.9; }
.task-summary { background: var(--app-card); border-radius: var(--h5-card-radius); margin-bottom: 16px; }
.task-entry { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 14px 12px; color: var(--app-text-1); text-decoration: none; }
.task-entry > span:first-child { min-width: 0; overflow-wrap: anywhere; }
.task-entry strong { font-size: 16px; }
.entry-note { display: block; margin-top: 5px; color: var(--app-text-2); font-size: 13px; }
.entry-arrow { flex-shrink: 0; color: var(--app-primary); font-size: 14px; }
.load-error { margin: 0; padding: 0 12px 12px; font-size: 14px; }
.block { margin-bottom: 16px; }
.block-title { margin: 0 0 10px; font-size: 15px; font-weight: 600; color: var(--app-text-2); }
.shortcuts { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
.shortcut {
  display: flex; align-items: center; justify-content: center; box-sizing: border-box;
  min-width: 0; min-height: 48px; padding: 10px 8px; border: 1px solid var(--app-border);
  border-radius: var(--app-radius-sm); background: var(--app-card); color: var(--app-text-1);
  font: inherit; font-size: 14px; font-weight: 600; text-align: center; text-decoration: none;
  overflow-wrap: anywhere; cursor: pointer;
}
.today-card { background: var(--app-card); border-radius: var(--h5-card-radius); padding: 12px; margin-bottom: 16px; }
.today-label { font-size: 14px; color: var(--app-text-2); margin-bottom: 4px; }
.today-val { font-size: 16px; font-weight: 700; color: var(--app-primary); overflow-wrap: anywhere; }
.today-tip { margin-top: 5px; font-size: 12px; color: var(--app-text-2); }
</style>
