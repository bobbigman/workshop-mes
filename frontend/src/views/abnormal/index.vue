<template>
  <div>
    <el-tabs v-model="statusTab" @tab-change="onTab">
      <el-tab-pane label="待处理" name="0" />
      <el-tab-pane label="已恢复" name="1" />
      <el-tab-pane label="全部" name="" />
    </el-tabs>

    <div class="toolbar sticky-toolbar">
      <el-select
        v-model="filterFieldKey"
        placeholder="筛选字段"
        class="toolbar-field"
        @change="onFilterFieldChange"
      >
        <el-option
          v-for="f in FILTER_FIELDS"
          :key="f.key"
          :label="f.label"
          :value="f.key"
        />
      </el-select>
      <el-select v-model="filterOp" class="toolbar-match">
        <el-option
          v-for="o in quickFilterOps"
          :key="o.value"
          :label="o.label"
          :value="o.value"
        />
      </el-select>
      <el-select
        v-if="currentFilterMeta?.valueType === 'abnormalType'"
        v-model="filterValue"
        clearable
        placeholder="选择异常类型"
        class="toolbar-value"
        @keyup.enter="onSearch"
      >
        <el-option
          v-for="o in ABNORMAL_TYPE_OPTIONS"
          :key="o.value"
          :label="o.label"
          :value="String(o.value)"
        />
      </el-select>
      <el-date-picker
        v-else-if="currentFilterMeta?.valueType === 'date'"
        v-model="filterValue"
        type="date"
        value-format="YYYY-MM-DD"
        placeholder="选择日期"
        class="toolbar-value"
        clearable
      />
      <el-input
        v-else
        v-model="filterValue"
        clearable
        :placeholder="filterValuePlaceholder"
        class="toolbar-value"
        @keyup.enter="onSearch"
      />
      <el-button @click="onSearch">查询</el-button>
      <el-button @click="onReset">重置</el-button>
      <el-button @click="onToolbarResolve">标记恢复</el-button>
    </div>

    <mes-table
      ref="tableRef"
      table-key="views/abnormal/index:1"
      :data="list"
      v-loading="loading"
      :row-class-name="rowClass"
      @selection-change="onSelectionChange"
      @row-click="onRowClick"
    >
      <el-table-column type="selection" width="48" :selectable="row => row.status === 0" />
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
      <el-table-column label="处理人" width="100">
        <template #default="{ row }">
          <span :class="{ muted: row.status === 0 }">{{ row.status === 0 ? '—' : (row.handlerName || '—') }}</span>
        </template>
      </el-table-column>
      <el-table-column label="照片" width="80">
        <template #default="{ row }">
          <a v-if="row.imagePath" :href="'/' + row.imagePath" target="_blank" rel="noopener">查看</a>
          <span v-else>—</span>
        </template>
      </el-table-column>
    </mes-table>

    <mes-pager v-model:page="page" v-model:page-size="pageSize" :total="total" @change="load" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getAbnormalList, resolveAbnormal } from '@/api/prod'

const route = useRoute()
const statusTab = ref('0')
const list = ref([])
const loading = ref(false)
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const tableRef = ref(null)
const selectedRows = ref([])
const selectedOne = computed(() => selectedRows.value.length === 1 ? selectedRows.value[0] : null)
const FILTER_FIELDS = [
  { key: 'keyword', label: '工单号/描述', valueType: 'text' },
  { key: 'orderNo', label: '工单号', valueType: 'text' },
  { key: 'description', label: '描述', valueType: 'text' },
  { key: 'reporterName', label: '上报人', valueType: 'text' },
  { key: 'abnormalType', label: '异常类型', valueType: 'abnormalType' },
  { key: 'reportedAt', label: '上报时间', valueType: 'date' }
]
const ABNORMAL_TYPE_OPTIONS = [
  { value: 1, label: '设备故障' },
  { value: 2, label: '物料短缺' },
  { value: 3, label: '质量异常' }
]

const filterFieldKey = ref('keyword')
const filterOp = ref('contains')
const filterValue = ref('')

const currentFilterMeta = computed(() =>
  FILTER_FIELDS.find(f => f.key === filterFieldKey.value) || FILTER_FIELDS[0]
)
const quickFilterOps = computed(() => {
  const t = currentFilterMeta.value?.valueType || 'text'
  if (t === 'date') {
    return [
      { value: 'eq', label: '等于' },
      { value: 'gt', label: '大于' },
      { value: 'lt', label: '小于' }
    ]
  }
  if (t === 'abnormalType') {
    return [{ value: 'eq', label: '等于' }]
  }
  return [
    { value: 'contains', label: '包含' },
    { value: 'eq', label: '等于' }
  ]
})
const filterValuePlaceholder = computed(() => {
  if (currentFilterMeta.value?.valueType === 'date') return '选择日期'
  return '输入筛选值'
})

function formatTime(t) {
  return t ? String(t).replace('T', ' ').slice(0, 19) : '—'
}

function onFilterFieldChange() {
  filterValue.value = ''
  const t = currentFilterMeta.value?.valueType
  if (t === 'date' || t === 'abnormalType') filterOp.value = 'eq'
  else filterOp.value = 'contains'
  const allowed = new Set(quickFilterOps.value.map(o => o.value))
  if (!allowed.has(filterOp.value)) {
    filterOp.value = quickFilterOps.value[0]?.value || 'eq'
  }
}

async function load() {
  loading.value = true
  try {
    const params = {
      page: page.value,
      pageSize: pageSize.value,
      filterField: filterFieldKey.value || 'keyword',
      filterOp: filterOp.value || 'contains'
    }
    if (statusTab.value !== '') params.status = Number(statusTab.value)
    if (filterValue.value != null && String(filterValue.value).trim() !== '') {
      params.filterValue = String(filterValue.value).trim()
    }
    const res = await getAbnormalList(params)
    list.value = res.data?.list || []
    total.value = res.data?.total || 0
    selectedRows.value = []
    await nextTick()
    tableRef.value?.clearSelection?.()
    const focusId = route.query.focusId
    if (focusId) {
      const el = document.querySelector(`[data-abnormal-id="${focusId}"]`)
      el?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    }
  } finally {
    loading.value = false
  }
}

function onTab() {
  page.value = 1
  load()
}

function onSearch() {
  page.value = 1
  load()
}

function onReset() {
  filterFieldKey.value = 'keyword'
  filterOp.value = 'contains'
  filterValue.value = ''
  page.value = 1
  load()
}

function onSelectionChange(rows) {
  selectedRows.value = rows || []
}

function onRowClick(row, column, event) {
  if (event?.target?.closest?.('button, a, .el-checkbox, input, label')) return
  if (row.status !== 0) return
  const table = tableRef.value
  if (!table?.toggleRowSelection) return
  table.clearSelection?.()
  table.toggleRowSelection(row, true)
}

function requireOne(actionLabel) {
  if (selectedRows.value.length > 1) {
    ElMessage.warning(`「${actionLabel}」一次只能选一条，请先只勾选一条`)
    return null
  }
  if (!selectedOne.value) {
    ElMessage.warning(`请先勾选一条异常再${actionLabel}`)
    return null
  }
  return selectedOne.value
}

function onToolbarResolve() {
  const row = requireOne('标记恢复')
  if (!row) return
  if (row.status !== 0) {
    ElMessage.warning('只有待处理的异常才能标记恢复')
    return
  }
  onResolve(row)
}

async function onResolve(row) {
  let note = ''
  try {
    const { value } = await ElMessageBox.prompt('请填写处理说明（选填）', '标记恢复', {
      confirmButtonText: '确认恢复',
      cancelButtonText: '取消',
      inputPlaceholder: '请输入处理方式，如：已更换零件、已调整设备设置，不填也可提交',
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
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; align-items: center; }
.toolbar-field { width: 150px; }
.toolbar-match { width: 100px; }
.toolbar-value { width: 200px; }
.pager { margin-top: 16px; display: flex; justify-content: flex-end; }
.muted { color: var(--app-text-3); font-size: 13px; }
.em-focus { outline: 2px solid var(--app-primary); outline-offset: -2px; }
:deep(.em-focus) { outline: 2px solid var(--app-primary); outline-offset: -2px; }
</style>
