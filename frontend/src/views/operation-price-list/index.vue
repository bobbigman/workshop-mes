<template>
  <div v-loading="loading">
    <PriceChangeDialog v-model="adjustVisible" :table-id="adjustId" @done="load" />
    <PriceChangeHistory v-model="historyVisible" :table-id="historyId" />
    <div class="toolbar sticky-toolbar">
      <el-select v-model="filterFieldKey" placeholder="筛选字段" class="toolbar-field" @change="onFilterFieldChange">
        <el-option v-for="f in FILTER_FIELDS" :key="f.key" :label="f.label" :value="f.key" />
      </el-select>
      <el-select v-model="filterOp" class="toolbar-match">
        <el-option v-for="o in quickFilterOps" :key="o.value" :label="o.label" :value="o.value" />
      </el-select>
      <el-select v-if="currentFilterMeta?.valueType === 'status'" v-model="filterValue" clearable placeholder="状态" class="toolbar-value" @keyup.enter="search">
        <el-option v-for="(v,k) in statuses" :key="k" :label="v" :value="String(k)" />
      </el-select>
      <el-select v-else-if="currentFilterMeta?.valueType === 'pricingType'" v-model="filterValue" clearable placeholder="工价计算方式" class="toolbar-value" @keyup.enter="search">
        <el-option v-for="(v,k) in pricingTypes" :key="k" :label="v" :value="String(k)" />
      </el-select>
      <el-select v-else-if="currentFilterMeta?.valueType === 'operation'" v-model="filterValue" clearable filterable placeholder="选择工序" class="toolbar-value" @keyup.enter="search">
        <el-option v-for="o in operations" :key="o.id" :label="o.name" :value="String(o.id)" />
      </el-select>
      <el-select
        v-else-if="isProductFilterField"
        v-model="filterValue"
        clearable
        filterable
        allow-create
        default-first-option
        :placeholder="productFilterPlaceholder"
        class="toolbar-value"
        @keyup.enter="search"
      >
        <el-option
          v-for="p in products"
          :key="p.id"
          :label="filterFieldKey === 'productCode' ? (p.code || '') : (p.name || '')"
          :value="filterFieldKey === 'productCode' ? (p.code || '') : (p.name || '')"
        />
      </el-select>
      <el-date-picker
        v-else-if="currentFilterMeta?.valueType === 'date'"
        v-model="filterValue"
        type="date"
        value-format="YYYY-MM-DD"
        :placeholder="filterValuePlaceholder"
        class="toolbar-value"
        clearable
      />
      <el-input v-else v-model="filterValue" clearable :placeholder="filterValuePlaceholder" class="toolbar-value" @keyup.enter="search" />
      <el-button @click="search">查询</el-button>
      <el-button @click="reset">重置</el-button>
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
      <el-button @click="onToolbarBrowse">查看</el-button>
      <el-button @click="onToolbarEdit">编辑</el-button>
      <el-button type="success" @click="onToolbarActivate">启用</el-button>
      <el-button type="danger" plain @click="onToolbarDeactivate">停用</el-button>
      <el-button type="warning" @click="onToolbarAdjust">调整工价</el-button>
      <el-button @click="onToolbarHistory">调价记录</el-button>
      <el-button @click="onToolbarDelete">删除</el-button>
    </div>
    <mes-table
      ref="tableRef"
      table-key="views/operation-price-list/index:1"
      :data="list"
      border
      stripe
      @selection-change="onSelectionChange"
      @row-click="onRowClick"
    >
      <el-table-column type="selection" width="48" fixed />
      <el-table-column label="单据编号" min-width="150"><template #default="{row}"><el-button link type="primary" @click="openBill(row.id,true)">{{row.code}}</el-button></template></el-table-column>
      <el-table-column prop="name" label="名称" min-width="150" />
      <el-table-column label="价目表类型 / 工序" min-width="150"><template #default="{row}">{{opName(row.operationId)}}</template></el-table-column>
      <el-table-column label="工价计算方式" width="95"><template #default="{row}">{{pricingTypes[row.pricingType]}}</template></el-table-column>
      <el-table-column label="产品范围" min-width="180"><template #default="{row}">{{productName(row.productId)}}</template></el-table-column>
      <el-table-column label="明细行数" width="95"><template #default="{row}">{{row.details.rows.length}}</template></el-table-column>
      <el-table-column label="状态" width="95"><template #default="{row}"><el-tag :type="row.status===1?'success':row.status===2?'info':'warning'">{{statuses[row.status]}}</el-tag></template></el-table-column>
      <el-table-column label="生效 ~ 失效" min-width="205"><template #default="{row}">{{day(row.effectiveFrom)}} ~ {{row.effectiveTo?day(row.effectiveTo):'长期'}}</template></el-table-column>
      <el-table-column label="更新时间" min-width="160"><template #default="{row}">{{row.updatedAt?.replace('T',' ').slice(0,19)}}</template></el-table-column>
    </mes-table>
    <mes-pager v-model:page="page" v-model:page-size="pageSize" :total="total" @change="load" />
  </div>
</template>
<script setup>
import { ref, computed, onMounted, onActivated, watch, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowDown } from '@element-plus/icons-vue'
import PriceChangeDialog from './PriceChangeDialog.vue'
import PriceChangeHistory from './PriceChangeHistory.vue'
import { getOperationList, getProductList } from '@/api/base'
import { getDocumentPermission } from '@/api/prod'
import { queryPriceLists, deletePriceList, changePriceListStatus } from '@/api/operationPriceList'
import { canEditByCreator } from '@/utils/documentPermission'

const route = useRoute()
const router = useRouter()
const role = Number(localStorage.getItem('role') || 0)
const statuses = { 0: '草稿', 1: '启用', 2: '停用' }
const pricingTypes = { 1: '计件', 2: '计时', 3: '单笔固定' }
const list = ref([]), total = ref(0), page = ref(1), pageSize = ref(20)
const operations = ref([]), products = ref([])
const loading = ref(false)
const onlyCreatorEditPriceList = ref(false)
const tableRef = ref(null), selectedRows = ref([])
const selectedOne = computed(() => selectedRows.value.length === 1 ? selectedRows.value[0] : null)
const historyVisible = ref(false), historyId = ref(null)
const adjustVisible = ref(false), adjustId = ref(null)
const FILTER_FIELDS = [
  { key: 'keyword', label: '编号/名称', valueType: 'text' },
  { key: 'code', label: '单据编号', valueType: 'text' },
  { key: 'name', label: '名称', valueType: 'text' },
  { key: 'status', label: '状态', valueType: 'status' },
  { key: 'operationId', label: '工序', valueType: 'operation' },
  { key: 'pricingType', label: '工价计算方式', valueType: 'pricingType' },
  { key: 'productCode', label: '产品编号', valueType: 'product' },
  { key: 'productName', label: '产品名称', valueType: 'product' },
  { key: 'date', label: '有效日期', valueType: 'date' },
  { key: 'effectiveFrom', label: '生效日期', valueType: 'date' }
]
const filterFieldKey = ref('keyword'), filterOp = ref('contains'), filterValue = ref('')
const currentFilterMeta = computed(() => FILTER_FIELDS.find(f => f.key === filterFieldKey.value) || FILTER_FIELDS[0])
const isProductFilterField = computed(() => filterFieldKey.value === 'productCode' || filterFieldKey.value === 'productName')
const productFilterPlaceholder = computed(() => filterFieldKey.value === 'productCode' ? '选择或输入产品编号' : '选择或输入产品名称')
const quickFilterOps = computed(() => {
  const t = currentFilterMeta.value?.valueType
  if (t === 'status' || t === 'pricingType' || t === 'operation') return [{ value: 'eq', label: '等于' }]
  if (t === 'date') return filterFieldKey.value === 'date'
    ? [{ value: 'eq', label: '等于' }]
    : [{ value: 'eq', label: '等于' }, { value: 'gt', label: '大于' }, { value: 'lt', label: '小于' }]
  return [{ value: 'contains', label: '包含' }, { value: 'eq', label: '等于' }]
})
const filterValuePlaceholder = computed(() => {
  if (filterFieldKey.value === 'date') return '查当天仍有效的价目表'
  if (filterFieldKey.value === 'effectiveFrom') return '生效日期'
  return '例：JG2024001 / 车削'
})
const day = s => s?.slice(0, 10)
const opName = id => operations.value.find(o => o.id === id)?.name || `未知工序（编号 ${id}）`
const productName = id => id ? ((p => p ? `${p.code} ${p.name}` : `未知产品（编号 ${id}）`)(products.value.find(p => p.id === id))) : '通用表（不限产品）'
const primed = ref(false)

function onFilterFieldChange() {
  filterValue.value = ''
  const t = currentFilterMeta.value?.valueType
  filterOp.value = (t === 'status' || t === 'pricingType' || t === 'operation' || t === 'date') ? 'eq' : 'contains'
  const allowed = new Set(quickFilterOps.value.map(o => o.value))
  if (!allowed.has(filterOp.value)) filterOp.value = quickFilterOps.value[0]?.value || 'eq'
}
async function applyOrderFilterQuery() {
  const opId = Number(route.query.operationId)
  if (!Number.isFinite(opId) || opId <= 0) return
  filterFieldKey.value = 'operationId'
  onFilterFieldChange()
  filterOp.value = 'eq'
  filterValue.value = String(opId)
  page.value = 1
  await load()
  await router.replace({ path: route.path, query: {} })
}
async function load() {
  const params = { page: page.value, pageSize: pageSize.value, filterField: filterFieldKey.value || 'keyword', filterOp: filterOp.value || 'contains' }
  if (filterValue.value != null && String(filterValue.value).trim() !== '') params.filterValue = String(filterValue.value).trim()
  const r = await queryPriceLists(params)
  list.value = r.data.list
  total.value = r.data.total
  selectedRows.value = []
  await nextTick()
  tableRef.value?.clearSelection?.()
}
async function search() { page.value = 1; await load() }
async function reset() { filterFieldKey.value = 'keyword'; filterOp.value = 'contains'; filterValue.value = ''; page.value = 1; await load() }
function onSelectionChange(rows) { selectedRows.value = rows || [] }
function onRowClick(row, column, event) {
  if (event?.target?.closest?.('button, a, .el-checkbox, input, label')) return
  const table = tableRef.value
  if (!table?.toggleRowSelection) return
  table.clearSelection?.()
  table.toggleRowSelection(row, true)
}
function requireOne(actionLabel) {
  if (selectedRows.value.length > 1) { ElMessage.warning(`「${actionLabel}」一次只能选一条`); return null }
  if (!selectedOne.value) { ElMessage.warning(`请先勾选一条价目表再${actionLabel}`); return null }
  return selectedOne.value
}
function openBill(key, browse) {
  router.push({ path: `/operation-price-list/${key}`, query: browse ? { mode: 'browse' } : {} })
}
function canEdit(row) { return row && row.status !== 1 }
function canEditCreator(row) {
  return canEditByCreator(onlyCreatorEditPriceList.value, row?.createdBy, role)
}
function denyIfNotCreator(row) {
  if (canEditCreator(row)) return false
  ElMessage.warning('这张价目表不是你创建的，不能改。请联系创建人或管理员处理。')
  return true
}
function onToolbarBrowse() { const row = requireOne('查看'); if (row) openBill(row.id, true) }
function onToolbarEdit() {
  const row = requireOne('编辑')
  if (!row) return
  if (!canEdit(row)) {
    ElMessage.warning('启用中的价目表不能编辑。要改结构请先停用或复制；只改工价请用「调整工价」。')
    return
  }
  if (denyIfNotCreator(row)) return
  openBill(row.id, false)
}
async function onToolbarActivate() {
  const row = requireOne('启用')
  if (!row) return
  if (denyIfNotCreator(row)) return
  if (row.status !== 2) { ElMessage.warning('只有已停用的价目表才能重新启用；草稿请打开后保存完整内容自动启用'); return }
  await changePriceListStatus(row.id, 1, row.version)
  ElMessage.success('已启用')
  await load()
}
async function onToolbarDeactivate() {
  const row = requireOne('停用')
  if (!row) return
  if (denyIfNotCreator(row)) return
  if (row.status !== 1) { ElMessage.warning('只有启用中的价目表才能停用'); return }
  try {
    await ElMessageBox.confirm(`确定停用价目表 ${row.code} 吗？停用后新单不再取这张表的价。`, '停用价目表')
  } catch (a) {
    if (a === 'cancel' || a === 'close') return
    throw a
  }
  await changePriceListStatus(row.id, 2, row.version)
  ElMessage.success('已停用')
  await load()
}
function onToolbarAdjust() {
  const row = requireOne('调整工价')
  if (!row) return
  if (denyIfNotCreator(row)) return
  if (row.status !== 1) { ElMessage.warning('只有启用中的价目表才能调整工价'); return }
  adjustId.value = row.id
  adjustVisible.value = true
}
function onToolbarHistory() { const row = requireOne('调价记录'); if (row) { historyId.value = row.id; historyVisible.value = true } }
function onCreateMenu(cmd) {
  if (cmd === 'create') { router.push({ path: '/operation-price-list/new' }); return }
  if (cmd === 'copy') {
    const row = requireOne('复制')
    if (row) router.push({ path: '/operation-price-list/new', query: { copyFrom: String(row.id) } })
  }
}
async function onToolbarDelete() {
  const row = requireOne('删除')
  if (!row) return
  if (denyIfNotCreator(row)) return
  if (row.status !== 0 || row.activatedAt) { ElMessage.warning('只能删除从未启用过的草稿价目表'); return }
  try {
    await ElMessageBox.confirm(`确定删除草稿价目表 ${row.code} 吗？删除后无法恢复`, '删除价目表')
  } catch (a) {
    if (a === 'cancel' || a === 'close') return
    throw a
  }
  await deletePriceList(row.id, row.version)
  await load()
  ElMessage.success('已删除')
}
watch(() => String(route.query.operationId || ''), async (v) => {
  if (!v || loading.value) return
  await applyOrderFilterQuery()
})
onMounted(async () => {
  loading.value = true
  try {
    const [o, p, perm] = await Promise.all([
      getOperationList({ pageSize: 1000 }),
      getProductList({ pageSize: 1000 }),
      getDocumentPermission().catch(() => ({ data: null }))
    ])
    operations.value = o.data.list
    products.value = p.data.list
    onlyCreatorEditPriceList.value = !!perm.data?.onlyCreatorEditPriceList
    await load()
    await applyOrderFilterQuery()
  } finally {
    loading.value = false
    primed.value = true
  }
})
onActivated(() => {
  if (primed.value) load()
})
</script>
<style scoped>
.toolbar{display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin:16px 0}.toolbar-field{width:150px}.toolbar-match{width:100px}.toolbar-value{width:200px}
.el-pagination{margin-top:16px}
</style>
