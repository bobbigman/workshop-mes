<template>
  <div>
    <el-tabs v-model="statusTab" @tab-change="load">
      <el-tab-pane label="待处理" name="0" />
      <el-tab-pane label="已恢复" name="1" />
      <el-tab-pane label="全部" name="" />
    </el-tabs>

    <el-table :data="list" v-loading="loading" :row-class-name="rowClass">
      <el-table-column label="类型" width="110">
        <template #default="{ row }">{{ row.typeLabel }}</template>
      </el-table-column>
      <el-table-column prop="description" label="描述" min-width="200" show-overflow-tooltip />
      <el-table-column label="工单" width="140">
        <template #default="{ row }">{{ row.orderNo || '—' }}</template>
      </el-table-column>
      <el-table-column prop="reporterName" label="上报人" width="100" />
      <el-table-column label="上报时间" width="170">
        <template #default="{ row }">{{ formatTime(row.reportedAt) }}</template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="row.status === 0 ? 'danger' : 'success'" size="small">
            {{ row.status === 0 ? '待处理' : '已恢复' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="照片" width="80">
        <template #default="{ row }">
          <a v-if="row.imagePath" :href="'/' + row.imagePath" target="_blank" rel="noopener">查看</a>
          <span v-else>—</span>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="120" fixed="right">
        <template #default="{ row }">
          <el-button v-if="row.status === 0" link type="primary" @click="onResolve(row)">标记恢复</el-button>
          <span v-else class="muted">{{ row.handlerName || '—' }}</span>
        </template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-pagination
        v-model:current-page="page"
        :page-size="pageSize"
        :total="total"
        layout="total, prev, pager, next"
        @current-change="load"
      />
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getAbnormalList, resolveAbnormal } from '@/api/prod'

const route = useRoute()
const statusTab = ref('0')
const list = ref([])
const loading = ref(false)
const page = ref(1)
const pageSize = 20
const total = ref(0)

function formatTime(t) {
  return t ? String(t).replace('T', ' ').slice(0, 19) : '—'
}

async function load() {
  loading.value = true
  try {
    const params = { page: page.value, pageSize }
    if (statusTab.value !== '') params.status = Number(statusTab.value)
    const res = await getAbnormalList(params)
    list.value = res.data?.list || []
    total.value = res.data?.total || 0
    await nextTick()
    const focusId = route.query.focusId
    if (focusId) {
      const el = document.querySelector(`[data-abnormal-id="${focusId}"]`)
      el?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    }
  } finally {
    loading.value = false
  }
}

async function onResolve(row) {
  let note = ''
  try {
    const { value } = await ElMessageBox.prompt('可选填写处理说明', '标记恢复', {
      confirmButtonText: '确认恢复',
      cancelButtonText: '取消',
      inputPlaceholder: '处理说明（可空）',
      inputValue: ''
    })
    note = value || ''
  } catch {
    return
  }
  await resolveAbnormal(row.id, { handleNote: note || undefined })
  ElMessage.success('已标记恢复')
  await load()
}

onMounted(() => {
  if (route.query.status != null && route.query.status !== '')
    statusTab.value = String(route.query.status)
  load()
})

function rowClass({ row }) {
  return String(route.query.focusId || '') === String(row.id) ? 'em-focus' : ''
}
</script>

<style scoped>
.pager { margin-top: 16px; display: flex; justify-content: flex-end; }
.muted { color: var(--app-text-3); font-size: 13px; }
.em-focus { outline: 2px solid var(--app-primary); outline-offset: -2px; }
:deep(.em-focus) { outline: 2px solid var(--app-primary); outline-offset: -2px; }
</style>
