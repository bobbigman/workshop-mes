<template>
  <main class="h5-help-page">
    <header class="h5-help-header"><router-link to="/h5/home">‹ 返回工作台</router-link><h1>更多功能</h1></header>
    <p v-if="loading" role="status">正在加载可用功能…</p>
    <p v-else-if="error" class="help-error" role="alert">功能配置加载失败 <van-button size="small" @click="load">重试</van-button></p>
    <van-cell-group inset>
      <template v-if="!loading && !error && !tasksOnly">
        <van-cell title="生产报表" label="查看生产统计" is-link :to="{ path: '/stat', query: { from: 'h5' } }" />
        <van-cell title="执行监控" label="查看执行情况" is-link :to="{ path: '/execution-monitor', query: { from: 'h5' } }" />
        <van-cell title="车间看板" label="查看车间进度" is-link :to="{ path: '/board', query: { from: 'h5' } }" />
      </template>
      <van-cell title="帮助" label="AI操作帮助、手册和常见问题" is-link to="/h5/help" />
      <van-cell v-if="managementActions.length" title="管理操作" label="代报工、补报与管理后台（按角色）" is-link @click="managementOpen = true" />
    </van-cell-group>
    <van-action-sheet v-model:show="managementOpen" title="管理操作" :actions="managementActions" cancel-text="取消" close-on-click-action @select="openManagement" />
    <p v-if="!loading && !error && tasksOnly" class="help-note">当前仅看我的任务。任务、报工记录和可用的工资入口请返回工作台查看。</p>
  </main>
</template>
<script setup>
import { computed, ref, onMounted } from 'vue'
import { Cell as VanCell, CellGroup as VanCellGroup, Button as VanButton, ActionSheet as VanActionSheet } from 'vant'
import 'vant/lib/index.css'
import '@/styles/h5Help.css'
import { useRouter } from 'vue-router'
import { managementDestination, canOpenReportManagement } from '@/utils/reportManagement'
import { getWorkerView } from '@/api/prod'
import { setWorkerViewMode, isWorkerTasksOnlyView } from '@/utils/workerView'
const router = useRouter()
const role = Number(localStorage.getItem('role') || 0)
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
  if (item.action !== 'backend' && !canOpenReportManagement(item.action, role)) return
  managementOpen.value = false
  router.push(item.action === 'backend' ? '/order' : managementDestination(item.action))
}
const loading = ref(true), error = ref(false), tasksOnly = ref(true)
async function load() {
  loading.value = true; error.value = false
  try {
    const res = await getWorkerView()
    setWorkerViewMode(res.data?.workerViewMode)
    tasksOnly.value = isWorkerTasksOnlyView()
  } catch (err) { error.value = true; console.error('[更多功能] 读取视角失败', err) }
  finally { loading.value = false }
}
onMounted(load)
</script>
