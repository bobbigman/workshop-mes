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

    <el-dialog v-model="dlg" :title="editingId ? '编辑产品' : copyFromId ? '复制产品' : '创建产品'" :close-on-click-modal="!saving" :close-on-press-escape="!saving" :show-close="!saving" width="520px">
      <el-alert v-if="copyFromId" title="请填写新编号；作业指导书、图纸和独立计价规则不会被复制，需要在新工序中重新上传或设置" type="info" :closable="false" show-icon style="margin-bottom:12px" />
      <el-alert v-if="detailLocked" title="已审核，表单已锁定。请先反审核再改。" type="warning" :closable="false" show-icon style="margin-bottom:12px" />
      <el-form :model="form" :disabled="saving || detailLocked" label-width="100px">
        <el-form-item label="产品编号" required><el-input v-model="form.code" /></el-form-item>
        <el-form-item label="产品名称" required><el-input v-model="form.name" /></el-form-item>
        <el-form-item v-if="isAdmin" label="重点产品">
          <el-checkbox v-model="form.isRiskFocus">标记为重点产品（风险简报优先展示）</el-checkbox>
        </el-form-item>
        <el-form-item label="单位">
          <el-select v-model="form.unitId" clearable placeholder="选择计量单位（如：件、把、打）" style="width:100%">
            <el-option v-for="u in unitOptions" :key="u.id" :label="u.name" :value="u.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="工艺路线">
          <el-select v-model="form.routingId" :disabled="!!form.newRouting" clearable filterable placeholder="选择" style="width:100%">
            <el-option v-for="r in routingOptions" :key="r.id" :label="r.name" :value="r.id" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="!editingId && form.routingId" label="调整工艺">
          <el-button v-if="!form.newRouting" :loading="loadingRouting" @click="copyRouting">复制工艺并修改</el-button>
          <el-button v-else @click="form.newRouting = null">沿用原工艺</el-button>
        </el-form-item>
        <template v-if="form.newRouting">
          <el-form-item label="新路线编号" required><el-input v-model="form.newRouting.code" /></el-form-item>
          <el-form-item label="新路线名称" required><el-input v-model="form.newRouting.name" /></el-form-item>
          <el-form-item label="工序顺序"><RoutingSteps v-model="form.newRouting.steps" :options="opOptions" /></el-form-item>
        </template>
        <el-form-item label="供应商"><el-input v-model="form.supplier" /></el-form-item>
        <el-form-item label="单价"><el-input-number v-model="form.price" :min="0" :precision="2" /></el-form-item>
        <ProductAttributes v-model="form.customValues" :fields="attributeFields" />
      </el-form>
      <div v-if="editingId" class="kf-block">
        <div class="kf-title">作业指导书 / 图纸</div>
        <KnowledgeFiles ref-type="product" :ref-id="editingId" />
      </div>
      <div v-if="editingId && detailMeta" class="sys-meta">
        <div>审核：{{ detailMeta.auditStatus === 1 ? '已审核' : '未审核' }}
          · 审核人 {{ detailMeta.auditedByName || '—' }}
          · 审核时间 {{ formatTime(detailMeta.auditedAt) }}</div>
        <div>禁用：禁用人 {{ detailMeta.forbidByName || '—' }}
          · 禁用时间 {{ formatTime(detailMeta.forbidAt) }}
          · 反禁用人 {{ detailMeta.unforbidByName || '—' }}
          · 反禁用时间 {{ formatTime(detailMeta.unforbidAt) }}</div>
      </div>
      <template #footer>
        <el-button :disabled="saving" @click="dlg=false">取消</el-button>
        <el-button type="primary" :disabled="loadingRouting || detailLocked" :loading="saving" @click="onSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
<script setup>
import { ref, computed, onMounted, nextTick } from 'vue'
import { getProductList, getProduct, createProduct, updateProduct, deleteProduct, forbidProduct, unforbidProduct, auditProduct, unauditProduct, getUnitList, getRoutingList, getRouting, getOperationList } from '@/api/base'
import ProductAttributes from '@/components/ProductAttributes.vue'
import { getCustomFields } from '@/api/prod'
import RoutingSteps from '@/components/RoutingSteps.vue'
import ImportDialog from '@/components/ImportDialog.vue'
import KnowledgeFiles from '@/components/KnowledgeFiles.vue'
import { ArrowDown } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'

const isAdmin = Number(localStorage.getItem('role')) === 1
const copyFromId = ref(null), saving = ref(false), loadingRouting = ref(false)
const list = ref([]), dlg = ref(false), editingId = ref(null)
const page = ref(1), pageSize = ref(20), total = ref(0)
const tableRef = ref(null)
const selectedRows = ref([])
const selectedOne = computed(() => selectedRows.value.length === 1 ? selectedRows.value[0] : null)
const form = ref({ code: '', name: '', unitId: null, routingId: null, supplier: '', price: null, isRiskFocus: false, customValues: {} })
const attributeFields = ref([])
const unitOptions = ref([]), routingOptions = ref([]), opOptions = ref([])

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
const detailMeta = ref(null)
const detailLocked = computed(() => detailMeta.value?.auditStatus === 1)

onMounted(async () => {
  attributeFields.value = (await getCustomFields('product')).data || []
  await load()
  unitOptions.value = (await getUnitList()).data || []
  routingOptions.value = ((await getRoutingList({ page: 1, pageSize: 200 })).data.list) || []
})

function formatTime(t) {
  return t ? String(t).replace('T', ' ').slice(0, 19) : '—'
}

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
  if (row) openEdit(row)
}

function onCreateMenu(cmd) {
  if (cmd === 'create') {
    openCreate()
    return
  }
  if (cmd === 'copy') {
    const row = requireOne('复制')
    if (row) openCopy(row)
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

function openCreate() {
  copyFromId.value = null
  editingId.value = null
  detailMeta.value = null
  form.value = { code: '', name: '', unitId: null, routingId: null, supplier: '', price: null, isRiskFocus: false, customValues: {} }
  dlg.value = true
}
async function openEdit(row) {
  copyFromId.value = null
  const res = await getProduct(row.id)
  editingId.value = row.id
  const d = res.data
  detailMeta.value = {
    auditStatus: d.auditStatus,
    auditedByName: d.auditedByName,
    auditedAt: d.auditedAt,
    forbidByName: d.forbidByName,
    forbidAt: d.forbidAt,
    unforbidByName: d.unforbidByName,
    unforbidAt: d.unforbidAt
  }
  form.value = { code: d.code, name: d.name, unitId: d.unitId, routingId: d.routingId, supplier: d.supplier, price: d.price, isRiskFocus: !!d.isRiskFocus, customValues: d.customValues || {} }
  dlg.value = true
  if (d.auditStatus === 1) {
    ElMessage.info('已审核的产品不能直接改，请先反审核')
  }
}
async function openCopy(row) {
  await openEdit(row)
  editingId.value = null
  copyFromId.value = row.id
  detailMeta.value = null
  form.value.code = ''
  form.value.isRiskFocus = false
}

async function onSave() {
  if (saving.value) return
  if (!form.value.code.trim() || !form.value.name.trim()) { ElMessage.error('产品编号和名称都是必填项，请填写后再保存；产品编号在系统中必须唯一'); return }
  if (form.value.newRouting) {
    const r = form.value.newRouting
    if (!r.code.trim() || !r.name.trim() || !r.steps.length || r.steps.some(s => !s.operationId)) {
      ElMessage.error('请填写新路线的编号和名称，并为每一道工序选择对应的加工步骤，不能留空'); return
    }
    if (new Set(r.steps.map(s => s.operationId)).size !== r.steps.length) {
      ElMessage.error('同一条工艺路线中不能重复添加同一道工序，请检查工序顺序列表并去掉重复项'); return
    }
  }
  saving.value = true
  try {
    const payload = { ...form.value, customValues: Object.fromEntries(Object.entries(form.value.customValues || {}).map(([k,v])=>[k,v==null?null:String(v)])) }
    if (editingId.value) await updateProduct(editingId.value, payload)
    else await createProduct({ ...payload, copyFromId: copyFromId.value })
    ElMessage.success(editingId.value ? '保存成功' : '已保存为未审核；请勾选后点「业务操作 → 审核」，审核后才能建工单选用')
    dlg.value = false
    await load()
    if (form.value.newRouting) routingOptions.value = (await getRoutingList({ page: 1, pageSize: 200 })).data.list || []
  } finally { saving.value = false }
}
async function copyRouting() {
  if (loadingRouting.value) return
  const sourceId = form.value.routingId
  const targetForm = form.value
  loadingRouting.value = true
  try {
    const [route, ops] = await Promise.all([
      getRouting(sourceId), getOperationList({ page: 1, pageSize: 1000 })
    ])
    if (!dlg.value || form.value !== targetForm || form.value.routingId !== sourceId || editingId.value) return
    opOptions.value = ops.data.list || []
    const d = route.data
    form.value.newRouting = {
      copyFromId: d.id, code: '', name: d.name,
      steps: d.steps.length ? d.steps.map((s, i) => ({ operationId: s.operationId, seq: i + 1 })) : [{ operationId: null }]
    }
  } finally { loadingRouting.value = false }
}
async function onDelete(row) {
  await ElMessageBox.confirm('确认删除该产品吗？若已被工单或工序价目表引用则不能删除；删除后无法恢复', '提示', { type: 'warning' })
  await deleteProduct(row.id); ElMessage.success('已删除'); await load()
}
</script>
<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; align-items: center; }
.toolbar-field { width: 150px; }
.toolbar-match { width: 100px; }
.toolbar-value { width: 200px; }
.kf-block { margin-top: 4px; }
.kf-title { font-weight: 600; margin-bottom: 8px; color: #303133; }
.sys-meta { margin-top: 12px; padding-top: 10px; border-top: 1px solid #ebeef5; color: #909399; font-size: 12px; line-height: 1.7; }
</style>
