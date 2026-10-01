<template>
  <div class="h5-page">
    <div class="header">
      <FactoryBadge />
      <h2>我的任务</h2>
      <H5LogoutBtn />
    </div>

    <div v-if="list.length === 0" class="empty">暂无派给我的任务</div>
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

    <TabBar active="tasks" />
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { myTasks } from '@/api/assign'
import TabBar from './TabBar.vue'
import FactoryBadge from '@/components/FactoryBadge.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'

const router = useRouter()
const list = ref([])

const STATUS = { 0: '未开始', 1: '执行中', 2: '已结束', 3: '已取消' }
function statusLabel(s) { return STATUS[s] ?? s }
function formatDue(t) { return t ? String(t).replace('T', ' ').slice(0, 16) : '—' }
function dueClass(row) { return row.dueState === 'overdue' ? 'due-overdue' : row.dueState === 'warning' ? 'due-warning' : '' }

function goReport(row) {
  router.push({ path: '/h5/report', query: { order: row.orderNo } })
}

onMounted(load)

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
.empty { text-align: center; color: var(--app-text-4); padding: 40px 0; }
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
</style>
