<template>
  <div>
    <ValueTip :text="VALUE_TIPS.order" />
    <div class="sticky-toolbar-block">
    <div class="toolbar">
      <!-- 快捷过滤：字段 + 比较符 + 值（对标金蝶） -->
      <el-select
        v-model="filterFieldKey"
        placeholder="筛选字段"
        class="toolbar-field"
        @change="onFilterFieldChange"
      >
        <el-option
          v-for="f in quickFilterFields"
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
        v-if="currentFilterMeta?.valueType === 'single'"
        v-model="filterValue"
        clearable
        filterable
        allow-create
        default-first-option
        placeholder="选择或输入选项值"
        class="toolbar-value"
        @keyup.enter="onSearch"
      >
        <el-option
          v-for="opt in (currentFilterMeta.options || [])"
          :key="opt"
          :label="opt"
          :value="opt"
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
      <el-select
        v-else-if="isProductFilterField"
        v-model="filterValue"
        clearable
        filterable
        allow-create
        default-first-option
        :placeholder="productFilterPlaceholder"
        class="toolbar-value"
        @keyup.enter="onSearch"
      >
        <el-option
          v-for="p in productOptions"
          :key="p.id"
          :label="productFilterOptionLabel(p)"
          :value="productFilterOptionValue(p)"
        />
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
      <el-dropdown v-if="isAdmin" trigger="click" @command="onCreateMenu">
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
      <el-button v-if="isAdmin" @click="onToolbarEdit">编辑</el-button>
      <el-dropdown v-if="isAdmin" trigger="click" @command="onAuditMenu">
        <el-button>
          审核<el-icon class="el-icon--right"><ArrowDown /></el-icon>
        </el-button>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item command="approve">审核</el-dropdown-item>
            <el-dropdown-item command="unaudit">反审核</el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>
      <el-dropdown v-if="isAdmin" trigger="click" @command="onBizMenu">
        <el-button>
          业务操作<el-icon class="el-icon--right"><ArrowDown /></el-icon>
        </el-button>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item command="start">开始</el-dropdown-item>
            <el-dropdown-item command="finish">关闭工单</el-dropdown-item>
            <el-dropdown-item command="hang">挂起</el-dropdown-item>
            <el-dropdown-item command="unhang">恢复生产</el-dropdown-item>
            <el-dropdown-item command="withdraw">撤回</el-dropdown-item>
            <el-dropdown-item command="cancel">作废</el-dropdown-item>
            <el-dropdown-item command="restore">恢复</el-dropdown-item>
            <el-dropdown-item command="print" divided>打印</el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>
      <el-button @click="onToolbarReport">报工</el-button>
      <el-button v-if="canAssign" @click="onToolbarAssign">派工</el-button>
      <el-button v-if="isAdmin" @click="onToolbarDelete">删除</el-button>
      <el-button v-if="isAdmin && canPrint" @click="printSettingDlg = true">打印设置</el-button>
    </div>

    <div class="status-tabs" role="tablist" aria-label="工单状态">
      <button
        v-for="t in statusTabs"
        :key="t.key || 'all'"
        type="button"
        class="status-tab"
        role="tab"
        :class="{ active: statusTab === t.key }"
        :aria-selected="statusTab === t.key"
        @click="onStatusTab(t.key)"
      >{{ t.label }} {{ t.count }}</button>
    </div>
    </div>

    <!-- 桌面：表格；手机：卡片，避免窄屏挤成碎字 -->
    <mes-table
      ref="tableRef"
      table-key="views/order/index:1"
      legacy-order-widths
      class="pc-table"
      :data="list"
      border
      show-overflow-tooltip
      @selection-change="onSelectionChange"
      @row-click="onRowClick"
    >
      <el-table-column type="selection" width="48" fixed />
      <el-table-column prop="orderNo" column-key="orderNo" label="工单编号" min-width="200" class-name="col-order-no">
        <template #default="{ row }">
          <el-button link type="primary" @click="openDetail(row)">{{ row.orderNo }}</el-button>
        </template>
      </el-table-column>
      <el-table-column prop="productName" column-key="productName" label="产品" min-width="120" />
      <el-table-column prop="qty" column-key="qty" label="数量" width="110" />
      <el-table-column column-key="progress" label="进度" width="160">
        <template #default="{ row }">{{ row.doneQty ?? 0 }}/{{ row.planQty ?? row.qty }}</template>
      </el-table-column>
      <el-table-column column-key="remainQty" label="剩余" width="110">
        <template #default="{ row }">{{ row.remainQty ?? 0 }}</template>
      </el-table-column>
      <el-table-column column-key="status" label="状态" width="140">
        <template #default="{ row }">
          <el-tag :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag>
          <el-tag v-if="row.reopened && row.status === 1" size="small" type="info" style="margin-left:4px">已重新打开</el-tag>
        </template>
      </el-table-column>
      <el-table-column column-key="dueDate" label="交期" width="180">
        <template #default="{ row }">
          <span :class="dueStateClass(row)">{{ formatDue(row.dueDate) }}</span>
        </template>
      </el-table-column>
      <el-table-column
        v-for="f in listColumns"
        :key="'cf-' + f.id"
        :column-key="'cf-' + f.id"
        :label="f.fieldName"
        min-width="120"
        show-overflow-tooltip
      >
        <template #default="{ row }">{{ extCell(row, f.id) }}</template>
      </el-table-column>
      <el-table-column prop="createdAt" column-key="createdAt" label="创建时间" width="180">
        <template #default="{ row }">{{ formatDue(row.createdAt) }}</template>
      </el-table-column>
    </mes-table>

    <div class="m-list">
      <div v-if="list.length === 0" class="m-empty">暂无工单</div>
      <div v-for="row in list" :key="row.id" class="m-card">
        <div class="m-top">
          <el-tag size="small" :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag>
          <el-tag v-if="row.reopened && row.status === 1" size="small" type="info">已重新打开</el-tag>
          <span class="m-no">{{ row.orderNo }}</span>
        </div>
        <div class="m-product">{{ row.productName }}</div>
        <div class="m-meta">
          <span>进度 {{ row.doneQty ?? 0 }}/{{ row.planQty ?? row.qty }}</span>
          <span>剩余 {{ row.remainQty ?? 0 }}</span>
          <span :class="dueStateClass(row)">交期 {{ formatDue(row.dueDate) }}</span>
        </div>
        <div class="m-actions">
          <el-button size="small" @click="openDetail(row)">详情</el-button>
          <el-button v-if="isAdmin && canEdit(row) && canEditCreator(row)" size="small" type="primary" plain @click="openEdit(row)">编辑</el-button>
          <el-button v-if="canReport(row)" size="small" type="success" @click="goReport(row)">报工</el-button>
          <el-button v-if="canPrint" size="small" @click="onPrint(row)">打印</el-button>
          <el-dropdown v-if="isAdmin" trigger="click">
            <el-button size="small">更多</el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item v-for="a in actions(row)" :key="a.key" @click="onTransition(row, a.key)">{{ a.label }}</el-dropdown-item>
                <el-dropdown-item @click="onCopy(row)">复制</el-dropdown-item>
                <el-dropdown-item divided style="color:#f53f3f" @click="onDelete(row)">删除</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </div>
    </div>
    <mes-pager v-model:page="page" v-model:page-size="pageSize" :total="total" @change="load" />

    <!-- 打印设置 / 流转卡 -->
    <PrintSettingDialog v-model="printSettingDlg" />
    <PrintLabelDialog v-model="printLabelDlg" :order-id="printOrderId" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onActivated, nextTick } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { ArrowDown } from '@element-plus/icons-vue'
import { getOrderList, getOrderStatusCounts, updateOrder, transitionOrder, deleteOrder, getCustomFields, getDocumentPermission } from '@/api/prod'
import { getProductList } from '@/api/base'
import { ElMessage, ElMessageBox } from 'element-plus'
import PrintSettingDialog from './PrintSettingDialog.vue'
import PrintLabelDialog from './PrintLabelDialog.vue'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'
import { canUse, FEATURE, currentTier } from '@/utils/licenseTier'
import { canEditByCreator } from '@/utils/documentPermission'

const router = useRouter()
const tableRef = ref(null)
const route = useRoute()
const role = Number(localStorage.getItem('role') || 0)
const isAdmin = role === 1
const canAssign = role === 1 || role === 3
const onlyCreatorEditWorkOrder = ref(false)
const canPrint = canUse(currentTier(), FEATURE.PrintLabel)
const listPrimed = ref(false)
const list = ref([]), statusTab = ref('1')
const page = ref(1), pageSize = ref(20), total = ref(0)
const selectedRows = ref([])
const selectedOne = computed(() => selectedRows.value.length === 1 ? selectedRows.value[0] : null)
function rowStatus(row) { return Number(row?.status) }
const counts = ref({ all: 0, notStarted: 0, doing: 0, done: 0, cancelled: 0, draft: 0, pendingAudit: 0, suspended: 0 })
const statusTabs = computed(() => [
  { key: '', label: '全部', count: counts.value.all },
  { key: '4', label: '草稿', count: counts.value.draft },
  { key: '5', label: '已保存', count: counts.value.pendingAudit },
  { key: '0', label: '已审核', count: counts.value.notStarted },
  { key: '1', label: '报工中', count: counts.value.doing },
  { key: '6', label: '已挂起', count: counts.value.suspended },
  { key: '2', label: '已结束', count: counts.value.done },
  { key: '3', label: '已作废', count: counts.value.cancelled }
])
const printSettingDlg = ref(false), printLabelDlg = ref(false), printOrderId = ref(null)
const productOptions = ref([]), customFields = ref([])
/** 快捷过滤：默认「工单号/产品 + 包含」 */
const filterFieldKey = ref('keyword')
const filterOp = ref('contains')
const filterValue = ref('')

const SYS_FILTER_FIELDS = [
  { key: 'keyword', label: '工单号/产品', valueType: 'text' },
  { key: 'orderNo', label: '工单号', valueType: 'text' },
  { key: 'productCode', label: '产品编号', valueType: 'text' },
  { key: 'productName', label: '产品名称', valueType: 'text' },
  { key: 'qty', label: '数量', valueType: 'number' },
  { key: 'dueDate', label: '计划交期', valueType: 'date' }
]

const quickFilterFields = computed(() => {
  const customs = (customFields.value || []).map(f => ({
    key: `cf_${f.id}`,
    label: f.fieldName,
    valueType: f.fieldType === 'single' ? 'single' : f.fieldType === 'number' ? 'number' : 'text',
    options: f.options || []
  }))
  return [...SYS_FILTER_FIELDS, ...customs]
})

const currentFilterMeta = computed(() =>
  quickFilterFields.value.find(f => f.key === filterFieldKey.value) || SYS_FILTER_FIELDS[0]
)

const quickFilterOps = computed(() => {
  const t = currentFilterMeta.value?.valueType || 'text'
  if (t === 'number' || t === 'date') {
    return [
      { value: 'eq', label: '等于' },
      { value: 'gt', label: '大于' },
      { value: 'lt', label: '小于' }
    ]
  }
  if (t === 'single') {
    return [{ value: 'eq', label: '等于' }]
  }
  return [
    { value: 'contains', label: '包含' },
    { value: 'eq', label: '等于' }
  ]
})

const filterValuePlaceholder = computed(() => {
  const t = currentFilterMeta.value?.valueType
  if (t === 'number') return '输入数字'
  return '输入筛选值'
})

/** 产品编号/名称：可搜索下拉，复用建单产品列表；允许手输做「包含」 */
const isProductFilterField = computed(() =>
  filterFieldKey.value === 'productCode' || filterFieldKey.value === 'productName'
)
const productFilterPlaceholder = computed(() =>
  filterFieldKey.value === 'productCode' ? '选择或输入产品编号' : '选择或输入产品名称'
)
function productFilterOptionLabel(p) {
  return `${p.code || ''} ${p.name || ''}`.trim()
}
function productFilterOptionValue(p) {
  return filterFieldKey.value === 'productCode' ? (p.code || '') : (p.name || '')
}

const listColumns = computed(() =>
  (customFields.value || [])
    .filter(f => f.showInOrderList)
    .slice()
    .sort((a, b) => a.id - b.id)
)

const STATUS = { 0: '已审核', 1: '报工中', 2: '已结束', 3: '已作废', 4: '草稿', 5: '已保存', 6: '已挂起' }
function statusLabel(s) { return STATUS[s] ?? s }
function statusTag(s) {
  if (s === 4 || s === 5) return ''
  if (s === 0) return 'info'
  if (s === 1) return 'warning'
  if (s === 2) return 'success'
  if (s === 6) return 'warning'
  return 'danger'
}
function formatDue(t) { return t ? String(t).replace('T', ' ').slice(0, 16) : '—' }
function dueStateClass(row) { return row.dueState === 'overdue' ? 'due-overdue' : row.dueState === 'warning' ? 'due-warning' : '' }
function canEdit(row) {
  const s = rowStatus(row)
  return s === 4 || s === 5
}
function canEditCreator(row) {
  return canEditByCreator(onlyCreatorEditWorkOrder.value, row?.createdBy, role)
}
function canReport(row) {
  const s = rowStatus(row)
  return s === 0 || s === 1
}
function goReport(row) { router.push({ path: '/report', query: { order: row.orderNo } }) }
function extCell(row, fieldId) {
  const ext = row.ext || {}
  const v = ext[fieldId] ?? ext[String(fieldId)]
  return v == null || v === '' ? '—' : v
}

function actions(row) {
  const a = []
  const s = rowStatus(row)
  if (s === 0) a.push({ key: 'start', label: '开始', type: 'success' })
  if (s === 1) a.push({ key: 'finish', label: '关闭工单', type: 'success' })
  if (s === 1) a.push({ key: 'hang', label: '挂起', type: 'warning' })
  if (s === 6) a.push({ key: 'unhang', label: '恢复生产', type: 'primary' })
  if (s === 1) a.push({ key: 'withdraw', label: '撤回', type: 'warning' })
  if (s === 2) a.push({ key: 'withdraw', label: '撤回', type: 'warning' })
  if (s === 0 || s === 1 || s === 4 || s === 5 || s === 6) a.push({ key: 'cancel', label: '作废', type: 'danger' })
  if (s === 3) a.push({ key: 'restore', label: '恢复', type: 'primary' })
  return a
}

function onSelectionChange(rows) {
  selectedRows.value = rows || []
}

/** 点行即单选，避免只点了行却没勾上导致顶栏全灰 */
function onRowClick(row, column, event) {
  if (event?.target?.closest?.('button, a, .el-checkbox, input, label')) return
  const table = tableRef.value
  if (!table?.toggleRowSelection) return
  table.clearSelection?.()
  table.toggleRowSelection(row, true)
}

function requireOne(actionLabel) {
  if (selectedRows.value.length > 1) {
    ElMessage.warning(`「${actionLabel}」一次只能选一张工单，请先只勾选一张`)
    return null
  }
  if (!selectedOne.value) {
    ElMessage.warning(`请先勾选一张工单再${actionLabel}`)
    return null
  }
  return selectedOne.value
}

function onCreateMenu(cmd) {
  if (cmd === 'create') {
    router.push({ path: '/order/new' })
    return
  }
  if (cmd === 'copy') {
    const row = requireOne('复制')
    if (row) router.push({ path: '/order/new', query: { copyFrom: String(row.id) } })
  }
}

async function onAuditMenu(cmd) {
  const row = requireOne(cmd === 'unaudit' ? '反审核' : '审核')
  if (!row) return
  const s = rowStatus(row)
  const stayAll = statusTab.value === ''
  const keepId = row.id
  if (cmd === 'approve') {
    if (s !== 4 && s !== 5) {
      ElMessage.error('只有「草稿」或「已保存」的工单可以审核')
      return
    }
    try {
      await updateOrder(row.id, { saveAction: 'approve', asDraft: false, quoteTokens: {} })
      ElMessage.success('已审核锁价，工人可在报工页看到并开始报工')
      if (!stayAll) statusTab.value = '0'
      page.value = 1
      await load({ reselectId: stayAll ? keepId : null })
    } catch (_) {
      // http 拦截器已提示；缺价时引导进编辑补齐
      ElMessage.info('请在编辑里补全取价后再点「审核」')
      openEdit(row)
    }
    return
  }
  if (cmd === 'unaudit') {
    if (s !== 0 && s !== 1) {
      ElMessage.error('当前不需要反审核')
      return
    }
    await ElMessageBox.confirm(
      `反审核后工单「${row.orderNo}」退回「已保存」，单价解锁，需重新审核才能报工。确认反审核？`,
      '反审核',
      { type: 'warning' }
    )
    await transitionOrder(row.id, 'unaudit')
    ElMessage.success('已反审核，单价已解锁')
    if (!stayAll) statusTab.value = '5'
    page.value = 1
    await load({ reselectId: stayAll ? keepId : null })
  }
}

function onBizMenu(cmd) {
  const labels = { start: '开始', finish: '关闭工单', hang: '挂起', unhang: '恢复生产', withdraw: '撤回', cancel: '作废', restore: '恢复', print: '打印' }
  const row = requireOne(labels[cmd] || '操作')
  if (!row) return
  if (cmd === 'print') {
    if (!canPrint) {
      ElMessage.warning('当前账号未开通打印能力')
      return
    }
    onPrint(row)
    return
  }
  const allowed = actions(row).some(a => a.key === cmd)
  if (!allowed) {
    ElMessage.warning(`当前状态「${statusLabel(row.status)}」不能${labels[cmd]}`)
    return
  }
  onTransition(row, cmd)
}

function onToolbarEdit() {
  const rows = selectedRows.value || []
  if (!rows.length) {
    ElMessage.warning('请先勾选工单再编辑')
    return
  }
  const blocked = rows.filter(r => !canEdit(r))
  if (blocked.length) {
    ElMessage.warning(blocked.map(r => {
      const s = rowStatus(r)
      if (s === 0 || s === 1) {
        return `${r.orderNo}（${statusLabel(s)}）不能编辑。无报工请先反审核；有报工请复制新单`
      }
      return `${r.orderNo}（${statusLabel(s)}）不能编辑，只能看详情`
    }).join('\n'))
    return
  }
  const denied = rows.filter(r => !canEditCreator(r))
  if (denied.length) {
    ElMessage.warning('这张工单不是你创建的，不能改。请联系创建人或管理员处理。')
    return
  }
  if (rows.length > 1) {
    ElMessage.warning('一次只能编辑一张工单，请只勾选一张')
    return
  }
  openEdit(rows[0])
}

function onToolbarReport() {
  const row = requireOne('报工')
  if (row && canReport(row)) goReport(row)
  else if (row) ElMessage.warning('只有「已审核」或「报工中」的工单可以报工')
}

function onToolbarAssign() {
  const row = requireOne('派工')
  if (row) openDetail(row, { focus: 'assign' })
}

function onToolbarDelete() {
  const row = requireOne('删除')
  if (row) onDelete(row)
}

async function clearSelection() {
  selectedRows.value = []
  await nextTick()
  tableRef.value?.clearSelection?.()
}

/** 刷新后按 id 勾回（仅「全部」页签审核/反审核后用） */
async function reselectById(id) {
  if (id == null) return
  await nextTick()
  const row = list.value.find(r => r.id === id)
  const table = tableRef.value
  if (!row || !table?.toggleRowSelection) return
  table.toggleRowSelection(row, true)
}

onMounted(async () => {
  try {
    const perm = await getDocumentPermission()
    onlyCreatorEditWorkOrder.value = !!perm.data?.onlyCreatorEditWorkOrder
  } catch {
    onlyCreatorEditWorkOrder.value = false
  }
  await loadFields()
  await load()
  const openId = route.query.openId
  if (openId) {
    router.replace({ path: `/order/${Number(openId)}`, query: { mode: 'browse' } })
  }
  // 快捷过滤「产品编号/名称」
  const products = await getProductList({ page: 1, pageSize: 200 })
  productOptions.value = products.data.list || []
  listPrimed.value = true
})

onActivated(() => {
  if (listPrimed.value) load()
})

async function loadFields() {
  const fields = await getCustomFields('work_order')
  customFields.value = fields.data || []
  // 自定义字段被删：回到默认「工单号/产品」
  if (String(filterFieldKey.value).startsWith('cf_')) {
    const id = Number(String(filterFieldKey.value).slice(3))
    if (!customFields.value.some(f => f.id === id)) {
      filterFieldKey.value = 'keyword'
      filterOp.value = 'contains'
      filterValue.value = ''
    }
  }
}

function onFilterFieldChange() {
  filterValue.value = ''
  const t = currentFilterMeta.value?.valueType
  if (t === 'number' || t === 'date' || t === 'single') filterOp.value = 'eq'
  else filterOp.value = 'contains'
  // 若当前比较符不适用于新字段，纠正
  const allowed = new Set(quickFilterOps.value.map(o => o.value))
  if (!allowed.has(filterOp.value)) {
    filterOp.value = quickFilterOps.value[0]?.value || 'eq'
  }
}

async function loadCounts() {
  const res = await getOrderStatusCounts()
  const d = res.data || {}
  counts.value = {
    all: d.all || 0,
    notStarted: d.notStarted || 0,
    doing: d.doing || 0,
    done: d.done || 0,
    cancelled: d.cancelled || 0,
    draft: d.draft || 0,
    pendingAudit: d.pendingAudit || 0,
    suspended: d.suspended || 0
  }
}

async function load(opts = {}) {
  await loadFields()
  await loadCounts()
  const params = {
    page: page.value,
    pageSize: pageSize.value,
    filterField: filterFieldKey.value || 'keyword',
    filterOp: filterOp.value || 'contains'
  }
  if (statusTab.value === '') {
    // 「全部」与数量一致：不含已作废、已挂起
    params.excludeCancelled = true
  } else {
    params.status = statusTab.value
  }
  if (filterValue.value != null && String(filterValue.value).trim() !== '') {
    params.filterValue = String(filterValue.value).trim()
  }
  const res = await getOrderList(params)
  list.value = res.data.list || []
  total.value = res.data.total || 0
  await clearSelection()
  if (opts.reselectId != null) await reselectById(opts.reselectId)
}

function onSearch() {
  page.value = 1
  load()
}
function onStatusTab(key) {
  if (statusTab.value === key) return
  statusTab.value = key
  page.value = 1
  load()
}

function onReset() {
  filterFieldKey.value = 'keyword'
  filterOp.value = 'contains'
  filterValue.value = ''
  statusTab.value = '1'
  page.value = 1
  load()
}

function openEdit(row) {
  if (!canEdit(row)) {
    const s = rowStatus(row)
    if (s === 0 || s === 1) {
      ElMessage.warning(`${row.orderNo || ''}（${statusLabel(s)}）不能编辑。无报工请先反审核；有报工请复制新单`)
    } else {
      ElMessage.warning(`当前状态「${statusLabel(s)}」不能编辑，只能浏览`)
    }
    openDetail(row)
    return
  }
  if (!canEditCreator(row)) {
    ElMessage.warning('这张工单不是你创建的，不能改。请联系创建人或管理员处理。')
    openDetail(row)
    return
  }
  router.push({ path: `/order/${row.id}`, query: { mode: 'edit' } })
}

/** @param {{ id: number }} row @param {{ focus?: 'assign' }} [opts] */
function openDetail(row, opts = {}) {
  const query = { mode: 'browse' }
  if (opts.focus === 'assign') query.focus = 'assign'
  router.push({ path: `/order/${row.id}`, query })
}

async function onTransition(row, action) {
  const labelMap = { start: '开始', finish: '关闭工单', hang: '挂起', unhang: '恢复生产', withdraw: '撤回', cancel: '作废', restore: '恢复' }
  let tip = `确认${labelMap[action]}${action === 'finish' ? '' : '工单'} ${row.orderNo}？操作后立即生效`
  if (action === 'finish') {
    tip = `关闭后进入【已结束】：不能再报工/补报，也不能改数量、交期；历史报工保留。可在【已结束】点【撤回】再打开（报满也不会自动结束，需再点【关闭工单】）。这和【作废】不同——作废是这单不做了，走【已作废】再【恢复】。确认关闭工单 ${row.orderNo}？`
  } else if (action === 'hang') {
    tip = `挂起后进入【已挂起】：暂时不能报工/补报，也不算在产。以后还要做时，在【已挂起】点【恢复生产】回报工中。与【作废】不同：挂起是暂停，作废是这单不做了。确认挂起工单 ${row.orderNo}？`
  } else if (action === 'unhang') {
    tip = `恢复生产后回到【报工中】，可以继续报工。确认恢复生产工单 ${row.orderNo}？`
  } else if (action === 'withdraw' && row.status === 2) {
    tip = `这张单当前在【已结束】。撤回后回到报工中，可以继续报工；即使数量报满也不会自动结束，需要再点【关闭工单】。历史报工记录保留不删。确认撤回工单 ${row.orderNo}？`
  } else if (action === 'withdraw' && row.status === 1) {
    tip = `撤回后工单退回已审核；已报的良品、不良记录和工资数据都保留不删。确认撤回工单 ${row.orderNo}？`
  } else if (action === 'cancel') {
    tip = `作废后这张单不能再报工和补报；历史报工记录保留，可在【已作废】页签点【恢复】重新启用。与【关闭工单】不同：作废表示这单不做了，不是完工收口。确认作废工单 ${row.orderNo}？`
  } else if (action === 'restore') {
    tip = `作废后点【恢复】，工单回到已审核，可继续报工。确认恢复工单 ${row.orderNo}？`
  }
  await ElMessageBox.confirm(tip, '提示', { type: 'warning' })
  await transitionOrder(row.id, action)
  let okMsg = '操作已完成'
  if (action === 'finish') okMsg = '已关闭，进入已结束'
  else if (action === 'hang') okMsg = '已挂起'
  else if (action === 'unhang') okMsg = '已恢复生产'
  else if (action === 'withdraw' && row.status === 2) okMsg = '已重新打开'
  else if (action === 'withdraw' && row.status === 1) okMsg = '已退回已审核'
  ElMessage.success(okMsg)
  await load()
}

function onPrint(row) {
  printOrderId.value = row.id
  printLabelDlg.value = true
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除工单「${row.orderNo}」吗？该工单的所有报工记录将一并删除且无法恢复，请谨慎操作`, '提示', { type: 'warning' })
  await deleteOrder(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<style scoped>
.toolbar { display: flex; align-items: center; gap: 12px; margin-bottom: var(--app-section-gap); flex-wrap: wrap; }
.toolbar-field { width: 150px; }
.toolbar-match { width: 100px; }
.toolbar-value { width: 200px; }
.toolbar-value { width: 180px; }
.status-tabs { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: var(--app-section-gap); }
.status-tab {
  flex: none; padding: 8px 16px; border-radius: 16px; background: var(--app-card);
  font-size: 14px; line-height: 1.2; color: var(--app-text-2); border: 1px solid var(--app-border);
  white-space: nowrap; cursor: pointer;
}
.status-tab:hover:not(.active) {
  border-color: var(--app-primary-border); color: var(--app-primary); background: var(--app-primary-light);
}
.status-tab.active {
  background: var(--app-primary); color: var(--app-card); border-color: var(--app-primary); font-weight: 600;
}
:deep(td.col-order-no) { font-weight: 600; }
:deep(td.col-order-no .cell) { white-space: nowrap; }
.due-warning { color: var(--app-warning); }
.due-overdue { color: var(--app-danger); font-weight: 600; }

.m-list { display: none; }
.m-empty { text-align: center; color: var(--app-text-3); padding: 40px 0; }
.m-card {
  background: var(--app-card); border-radius: var(--app-radius);
  padding: 12px; margin-bottom: 10px; box-shadow: var(--app-shadow);
}
.m-top { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }
.m-no { font-weight: 700; font-size: 15px; color: var(--app-text-1); word-break: break-all; }
.m-product { font-size: 14px; color: var(--app-text-2); margin-bottom: 8px; }
.m-meta {
  display: flex; flex-wrap: wrap; gap: 8px 12px;
  font-size: 12px; color: var(--app-text-3); margin-bottom: 10px;
}
.m-actions { display: flex; flex-wrap: wrap; gap: 6px; }

@media (max-width: 768px) {
  .pc-table { display: none; }
  .m-list { display: block; }
}
</style>
