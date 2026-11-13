<template>
  <div>
    <div class="toolbar">
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
        v-if="currentFilterMeta?.valueType === 'bool'"
        v-model="filterValue"
        clearable
        placeholder="选择是否"
        class="toolbar-value"
        @keyup.enter="onSearch"
      >
        <el-option label="是（重点）" value="1" />
        <el-option label="否" value="0" />
      </el-select>
      <el-select
        v-else-if="currentFilterMeta?.valueType === 'status'"
        v-model="filterValue"
        clearable
        placeholder="选择启用状态"
        class="toolbar-value"
        @keyup.enter="onSearch"
      >
        <el-option label="启用" value="1" />
        <el-option label="禁用" value="0" />
        <el-option label="全部" value="all" />
      </el-select>
      <el-select
        v-else-if="currentFilterMeta?.valueType === 'auditStatus'"
        v-model="filterValue"
        clearable
        placeholder="选择审核状态"
        class="toolbar-value"
        @keyup.enter="onSearch"
      >
        <el-option label="未审核" value="0" />
        <el-option label="已审核" value="1" />
        <el-option label="全部" value="all" />
      </el-select>
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
      <el-dropdown trigger="click" @command="onCreateMenu">
        <el-button>
          新增<el-icon class="el-icon--right"><ArrowDown /></el-icon>
        </el-button>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item command="create">新增</el-dropdown-item>
            <el-dropdown-item command="copy">复制</el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>
      <el-button @click="onToolbarEdit">编辑</el-button>
      <el-dropdown v-if="isAdmin" trigger="click" @command="onBizMenu">
        <el-button>
          业务操作<el-icon class="el-icon--right"><ArrowDown /></el-icon>
        </el-button>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item command="audit">审核</el-dropdown-item>
            <el-dropdown-item command="unaudit">反审核</el-dropdown-item>
            <el-dropdown-item command="forbid" divided>禁用</el-dropdown-item>
            <el-dropdown-item command="unforbid">反禁用</el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>
      <el-button @click="onToolbarDelete">删除</el-button>
      <ImportDialog entity-type="product" title="导入产品" @done="load" />
    </div>
    <mes-table
      ref="tableRef"
      table-key="views/product/index:1"
      :data="list"
      @selection-change="onSelectionChange"
      @row-click="onRowClick"
    >
      <el-table-column type="selection" width="48" fixed />
      <el-table-column prop="code" label="产品编号" />
      <el-table-column prop="name" label="产品名称" />
      <el-table-column label="审核" width="90">
        <template #default="{ row }">
          <el-tag v-if="row.auditStatus === 1" type="success" size="small">已审核</el-tag>
          <el-tag v-else type="info" size="small">未审核</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="启用" width="90">
        <template #default="{ row }">
          <el-tag v-if="row.status === 0" type="danger" size="small">禁用</el-tag>
          <el-tag v-else type="success" size="small">启用</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="重点产品" width="100">
        <template #default="{ row }"><el-tag v-if="row.isRiskFocus" type="warning">重点</el-tag></template>
      </el-table-column>
      <el-table-column prop="unitName" label="单位" />
      <el-table-column prop="routingName" label="工艺路线" />
      <el-table-column prop="supplier" label="供应商" />
      <el-table-column prop="price" label="单价" width="90" />
    </mes-table>
    <mes-pager v-model:page="page" v-model:page-size="pageSize" :total="total" @change="load" />
  </div>
</template>
<script setup>
import { ref, computed, onMounted, onActivated, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import {
  getProductList, deleteProduct, forbidProduct, unforbidProduct,
  auditProduct, unauditProduct
} from '@/api/base'
import ImportDialog from '@/components/ImportDialog.vue'
import { ArrowDown } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'

const router = useRouter()
const isAdmin = Number(localStorage.getItem('role')) === 1
const list = ref([])
const page = ref(1), pageSize = ref(20), total = ref(0)
const tableRef = ref(null)
const selectedRows = ref([])
const selectedOne = computed(() => selectedRows.value.length === 1 ? selectedRows.value[0] : null)
const listPrimed = ref(false)

const FILTER_FIELDS = [
  { key: 'auditStatus', label: '审核状态', valueType: 'auditStatus' },
  { key: 'status', label: '启用状态', valueType: 'status' },
  { key: 'keyword', label: '编号/名称', valueType: 'text' },
  { key: 'code', label: '产品编号', valueType: 'text' },
  { key: 'name', label: '产品名称', valueType: 'text' },
  { key: 'supplier', label: '供应商', valueType: 'text' },
  { key: 'isRiskFocus', label: '重点产品', valueType: 'bool' }
]
const filterFieldKey = ref('auditStatus')
const filterOp = ref('eq')
const filterValue = ref('all')
const currentFilterMeta = computed(() =>
  FILTER_FIELDS.find(f => f.key === filterFieldKey.value) || FILTER_FIELDS[0]
)
const quickFilterOps = computed(() => {
  const t = currentFilterMeta.value?.valueType
  if (t === 'bool' || t === 'status' || t === 'auditStatus') {
    return [{ value: 'eq', label: '等于' }]
  }
  return [
    { value: 'contains', label: '包含' },
    { value: 'eq', label: '等于' }
  ]
})
const filterValuePlaceholder = computed(() =>
  filterFieldKey.value === 'code' ? '输入产品编号'
    : filterFieldKey.value === 'name' ? '输入产品名称'
      : filterFieldKey.value === 'supplier' ? '输入供应商'
        : '例：GX001 / 车削'
)

onMounted(async () => {
  await load()
  listPrimed.value = true
})

onActivated(() => {
  if (listPrimed.value) load()
})

async function load() {
  const params = {
    filterField: filterFieldKey.value || 'auditStatus',
    filterOp: filterOp.value || 'eq',
    includeDisabled: true,
    includeUnaudited: true,
    page: page.value,
    pageSize: pageSize.value
  }
  if (filterValue.value != null && String(filterValue.value).trim() !== '') {
    params.filterValue = String(filterValue.value).trim()
  }
  const res = await getProductList(params)
  list.value = res.data.list || []
  total.value = res.data.total || 0
  selectedRows.value = []
  await nextTick()
  tableRef.value?.clearSelection?.()
}

function onFilterFieldChange() {
  const t = currentFilterMeta.value?.valueType
  filterOp.value = (t === 'bool' || t === 'status' || t === 'auditStatus') ? 'eq' : 'contains'
  filterValue.value = t === 'status' ? '1' : t === 'auditStatus' ? 'all' : ''
  const allowed = new Set(quickFilterOps.value.map(o => o.value))
  if (!allowed.has(filterOp.value)) {
    filterOp.value = quickFilterOps.value[0]?.value || 'eq'
  }
}

function onSearch() {
  page.value = 1
  load()
}

function onReset() {
  filterFieldKey.value = 'auditStatus'
  filterOp.value = 'eq'
  filterValue.value = 'all'
  page.value = 1
  load()
}

function onSelectionChange(rows) {
  selectedRows.value = rows || []
}

function onRowClick(row, column, event) {
  if (event?.target?.closest?.('button, a, .el-checkbox, input, label')) return
  const table = tableRef.value
  if (!table?.toggleRowSelection) return
  table.clearSelection?.()
  table.toggleRowSelection(row, true)
}

function requireOne(actionLabel) {
  if (selectedRows.value.length > 1) {
    ElMessage.warning(`「${actionLabel}」一次只能选一条产品，请先只勾选一条`)
    return null
  }
  if (!selectedOne.value) {
    ElMessage.warning(`请先勾选一条产品再${actionLabel}`)
    return null
  }
  return selectedOne.value
}

function onToolbarEdit() {
  const row = requireOne('编辑')
  if (row) router.push({ path: `/product/${row.id}` })
}

function onCreateMenu(cmd) {
  if (cmd === 'create') {
    router.push({ path: '/product/new' })
    return
  }
  if (cmd === 'copy') {
    const row = requireOne('复制')
    if (row) router.push({ path: '/product/new', query: { copyFrom: String(row.id) } })
  }
}

function onToolbarDelete() {
  const row = requireOne('删除')
  if (row) onDelete(row)
}

async function onBizMenu(cmd) {
  const labels = { audit: '审核', unaudit: '反审核', forbid: '禁用', unforbid: '反禁用' }
  const row = requireOne(labels[cmd] || '操作')
  if (!row) return
  if (cmd === 'audit') {
    if (row.auditStatus === 1) {
      ElMessage.warning('该产品已经审核过了')
      return
    }
    const res = await auditProduct(row.id)
    ElMessage.success(res.msg || '已审核')
    await load()
    return
  }
  if (cmd === 'unaudit') {
    if (row.auditStatus !== 1) {
      ElMessage.warning('当前不需要反审核')
      return
    }
    await ElMessageBox.confirm(
      `反审核后「${row.code}」不能用于新建工单，改完需再审核。确认反审核？`,
      '反审核',
      { type: 'warning' }
    )
    const res = await unauditProduct(row.id)
    ElMessage.success(res.msg || '已反审核')
    await load()
    return
  }
  if (cmd === 'forbid') {
    if (row.status === 0) {
      ElMessage.warning('该产品已经是禁用状态')
      return
    }
    await ElMessageBox.confirm(
      `禁用后，「${row.code} ${row.name}」不能再用于新建工单，历史工单不受影响。确认禁用？`,
      '禁用产品',
      { type: 'warning' }
    )
    const res = await forbidProduct(row.id)
    ElMessage.success(res.msg || '已禁用')
    await load()
    return
  }
  if (row.status !== 0) {
    ElMessage.warning('该产品已经是启用状态')
    return
  }
  const res = await unforbidProduct(row.id)
  ElMessage.success(res.msg || '已反禁用')
  await load()
}

async function onDelete(row) {
  await ElMessageBox.confirm('确认删除该产品吗？若已被工单或工序价目表引用则不能删除；删除后无法恢复', '提示', { type: 'warning' })
  await deleteProduct(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>
<style scoped>
.toolbar { display: flex; gap: 6px; margin-bottom: 8px; flex-wrap: wrap; align-items: center; }
.toolbar-field { width: 150px; }
.toolbar-match { width: 100px; }
.toolbar-value { width: 200px; }
</style>
