<template>
  <div class="h5-page">
    <header class="header">
      <FactoryBadge />
      <h2>工作台</h2>
      <span class="login-user" :title="userName">{{ userName }}</span>
      <H5LogoutBtn />
    </header>

    <button v-if="canScan" type="button" class="scan-btn" @click="router.push('/h5/scan')">
      <span class="scan-copy"><strong>扫码报工</strong><span>扫工单二维码，填写本次数量</span></span>
      <span class="scan-icon"><van-icon name="scan" aria-hidden="true" /></span>
    </button>

    <section v-if="canWage" class="today-summary" aria-label="今日计件">
      <div v-if="summaryLoading" class="summary-state" role="status">今日计件加载中…</div>
      <div v-else-if="summaryError" class="summary-state" role="alert">今日计件加载失败 <button type="button" @click="loadTodaySummary">重试</button></div>
      <template v-else>
        <div class="today-cards">
          <div class="today-card good"><span>今日良品报工累计</span><strong>{{ todayGoodQty }}</strong></div>
          <div class="today-card wage"><span>预计计件收入</span><strong>¥{{ formatMoney(todayWage) }}</strong></div>
        </div>
        <p class="today-tip">含已复核和待复核，以工资单为准</p>
      </template>
    </section>

    <section class="task-summary" aria-label="我的任务">
      <router-link class="task-entry" to="/h5/tasks">
        <span class="task-copy">
          <strong>我的任务</strong>
          <span v-if="tasksLoading" class="entry-note" role="status">任务加载中…</span>
          <span v-else-if="loadError" class="entry-note">任务数量暂不可用</span>
          <span v-else-if="!list.length" class="entry-note">暂无派给我或本班组的任务</span>
          <span v-else class="entry-note"><b>已派任务 {{ list.length }} 项</b>（含本班组）</span>
        </span>
        <span class="entry-arrow">{{ loadError ? '查看任务' : '查看全部' }} ›</span>
      </router-link>
      <p v-if="loadError" class="load-error" role="alert">任务加载失败 <button type="button" @click="load">重试</button></p>
    </section>

    <section class="block" aria-label="常用功能">
      <h3 class="block-title">常用功能</h3>
      <p v-if="viewError" class="load-error" role="alert">查询入口配置加载失败 <button type="button" @click="refreshViewMode">重试</button></p>
      <div class="shortcuts">
        <router-link v-for="item in shortcuts" :key="item.label" class="shortcut" :to="item.to">
          <span class="shortcut-icon" :class="item.tone"><van-icon :name="item.icon" aria-hidden="true" /></span>
          <span class="shortcut-label">{{ item.label }}</span>
        </router-link>
        <button v-if="canManage" type="button" class="shortcut" @click="managementOpen = true">
          <span class="shortcut-icon slate"><van-icon name="setting-o" aria-hidden="true" /></span>
          <span class="shortcut-label">管理操作</span>
        </button>
      </div>
    </section>

    <TabBar active="home" />
    <H5ManagementMenu v-model:show="managementOpen" />
    <PwaGuide v-if="showPwaGuide" @dismiss-this-time="onPwaDismissThisTime" @dismiss-forever="onPwaDismissForever" />
  </div>
</template>

<script setup>
import { computed, ref, onMounted } from 'vue'
import { Icon as VanIcon } from 'vant'
import 'vant/lib/index.css'
import { useRouter } from 'vue-router'
import { myTasks } from '@/api/assign'
import { getWorkerView, getTodayPieceSummary } from '@/api/prod'
import TabBar from './TabBar.vue'
import FactoryBadge from '@/components/FactoryBadge.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'
import PwaGuide from '@/components/PwaGuide.vue'
import H5ManagementMenu from '@/components/H5ManagementMenu.vue'
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
const canScan = canUse(currentTier(), FEATURE.ScanReport)
const canWage = canUse(currentTier(), FEATURE.PieceWage)
const role = Number(localStorage.getItem('role') || 0)
const canManage = role === 1 || role === 3
const managementOpen = ref(false)
const shortcuts = computed(() => [
  ...(viewReady.value && showOrdersEntry.value ? [{ label: '工单', icon: 'orders-o', tone: 'blue', to: '/h5/orders' }] : []),
  ...(canWage ? [{ label: '我的工资', icon: 'gold-coin-o', tone: 'green', to: '/h5/my-wage' }] : []),
  { label: '上报异常', icon: 'warning-o', tone: 'orange', to: '/h5/abnormal' },
  { label: '帮助', icon: 'question-o', tone: 'pink', to: '/h5/help' }
])
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
    console.error('[工人视角] 读取工厂视角配置失败，暂不显示全车间查询入口', error)
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
.h5-page { padding: 0 16px calc(var(--h5-tabbar-height) + 16px + env(safe-area-inset-bottom)); height: 100vh; height: 100dvh; overflow-y: auto; scroll-padding-bottom: calc(var(--h5-tabbar-height) + 16px + env(safe-area-inset-bottom)); background: #f5f6fa; box-sizing: border-box; color: var(--app-text-1); }
.header { display: flex; align-items: center; gap: 8px; min-height: 48px; margin: 0 -16px 10px; padding: 0 16px; background: var(--app-card); }
.header :deep(.factory-badge) { flex: 1; min-width: 0; max-width: 150px; padding: 0; background: transparent; border-radius: 0; font-size: 11px; font-weight: 400; line-height: 1.4; }
h2 { margin: 0; flex-shrink: 0; font-size: 17px; }
.login-user { margin-left: auto; max-width: 56px; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 12px; color: var(--app-text-2); }
.header :deep(.h5-logout) { margin-left: 0; flex-shrink: 0; font-size: 12px; min-height: 44px; padding: 6px 0; }
.scan-btn { display: flex; justify-content: space-between; align-items: center; gap: 8px; width: 100%; min-height: 88px; padding: 14px; border: 0; border-radius: 16px; background: linear-gradient(110deg, #0968ff, #3e98ff); color: #fff; text-align: left; cursor: pointer; margin-bottom: 10px; }
.scan-copy { min-width: 0; display: flex; flex-direction: column; gap: 6px; }
.scan-copy strong { font-size: 23px; line-height: 1.2; }
.scan-copy > span { font-size: 12px; line-height: 1.5; }
.scan-icon { flex-shrink: 0; display: grid; place-items: center; width: 48px; height: 48px; border-radius: 15px; background: #f1f8ff; color: #1677ff; font-size: 30px; }
.today-summary { margin-bottom: 12px; }
.today-cards { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
.today-card { min-width: 0; padding: 8px 8px; border-radius: 14px; text-align: center; }
.today-card > span { display: block; font-size: 12px; line-height: 1.5; color: #65758b; }
.today-card strong { display: block; margin-top: 4px; font-size: 27px; line-height: 1.2; overflow-wrap: anywhere; }
.good { background: #edf3ff; color: #1677ff; }
.wage { background: #e7f6ee; color: #12975f; }
.today-tip { margin: 6px 0 0; font-size: 11px; line-height: 1.5; color: #7f899a; }
.summary-state { padding: 18px 12px; border-radius: 14px; background: #fff; font-size: 14px; }
.task-summary { background: var(--app-card); border: 1px solid #e9edf3; border-radius: 14px; margin-bottom: 12px; }
.task-entry { display: flex; align-items: center; justify-content: space-between; gap: 8px; min-height: 44px; padding: 8px 12px; box-sizing: border-box; color: var(--app-text-1); text-decoration: none; }
.task-copy { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; min-width: 0; font-size: 12px; overflow-wrap: anywhere; }
.task-copy strong { font-size: 13px; font-weight: 500; }
.entry-note { color: var(--app-text-2); }
.entry-note b { color: var(--app-primary); font-weight: 600; }
.entry-arrow { flex-shrink: 0; color: var(--app-primary); font-size: 12px; }
.load-error { margin: 0; padding: 0 12px 12px; font-size: 13px; }
.block-title { margin: 0 0 8px; font-size: 16px; font-weight: 700; }
.shortcuts { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 8px; }
.shortcut { display: flex; flex-direction: column; align-items: center; justify-content: flex-start; gap: 6px; min-width: 0; min-height: 88px; padding: 8px 3px; box-sizing: border-box; border: 1px solid #e9edf3; border-radius: 14px; background: var(--app-card); color: var(--app-text-1); text-align: center; text-decoration: none; }
.shortcut-icon { display: grid; place-items: center; width: 44px; height: 44px; border-radius: 14px; font-size: 26px; }
.shortcut-label { font-size: 13px; line-height: 1.4; overflow-wrap: anywhere; }
button.shortcut { font: inherit; cursor: pointer; }
.blue { background: #edf3ff; color: #1677ff; }
.green { background: #e7f6ee; color: #109961; }
.orange { background: #fff3e7; color: #ed8a00; }
.purple { background: #f2edff; color: #7a61e8; }
.cyan { background: #e4f6fa; color: #0896ad; }
.yellow { background: #fff9e4; color: #d5a000; }
.pink { background: #fceef4; color: #e65399; }
.slate { background: #edf1f7; color: #8190a8; }
.scan-btn:active, .shortcut:active { opacity: .8; }
@media (max-width: 350px) { .header { gap: 5px; } .header :deep(.factory-badge) { font-size: 10px; } .login-user { max-width: 36px; } .shortcuts { gap: 8px; } .shortcut-icon { width: 44px; height: 44px; } .shortcut-label { font-size: 13px; } .scan-btn { padding: 14px; } }
</style>
