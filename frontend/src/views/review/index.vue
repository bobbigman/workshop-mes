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
        v-if="currentFilterMeta?.valueType === 'reviewStatus'"
        v-model="filterValue"
        clearable
        placeholder="选择复核状态"
        class="toolbar-value"
        @keyup.enter="onSearch"
      >
        <el-option
          v-for="o in REVIEW_STATUS_OPTIONS"
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
      <el-select
        v-else-if="filterFieldKey === 'orderNo'"
        v-model="filterValue"
        filterable
        clearable
        allow-create
        default-first-option
        placeholder="选择或输入工单号"
        class="toolbar-value"
        @keyup.enter="onSearch"
      >
        <el-option
          v-for="o in orderOptions"
          :key="o.value"
          :label="o.label"
          :value="o.value"
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
      <el-button type="primary" @click="onSearch">查询</el-button>
      <el-button @click="onReset">重置</el-button>
      <el-button type="success" @click="onToolbarApprove">通过</el-button>
      <el-button type="danger" @click="onToolbarReject">退回</el-button>
      <el-button type="success" plain @click="onBatch">批量通过</el-button>
    </div>
    <mes-table
      ref="tableRef"
      table-key="views/review/index:1"
      :data="list"
      :row-class-name="rowClassName"
      @selection-change="onSel"
      @row-click="onRowClick"
    >
      <el-table-column type="selection" width="48" :selectable="row => row.reviewStatus !== 2" />
      <el-table-column prop="orderNo" label="工单" width="140" />
      <el-table-column prop="productName" label="产品" />
      <el-table-column prop="operationName" label="工序" width="100" />
      <el-table-column prop="userName" label="报工人" width="90" />
      <el-table-column prop="goodQty" label="良品" width="70" />
      <el-table-column prop="defectQty" label="不良" width="70" />
      <el-table-column label="可报上限" width="160">
        <template #default="{ row }">
          {{ row.upstreamLimit == null ? '-' : row.upstreamLimit }}
        </template>
      </el-table-column>
      <el-table-column label="单价" width="130">
        <template #default="{ row }">
          <template v-if="row.unitPrice != null">¥{{ Number(row.unitPrice).toFixed(4) }}</template>
          <template v-else>
            <span class="wage-miss">这道工序还没定单价，找管理员设一下。</span>
            <el-button v-if="isAdmin" link type="primary" @click="$router.push('/operation-price-list')">去设置单价</el-button>
          </template>
        </template>
      </el-table-column>
      <el-table-column label="时长" width="90">
        <template #default="{ row }">{{ formatDuration(row.durationMinutes) }}</template>
      </el-table-column>
      <el-table-column label="状态" width="150">
        <template #default="{ row }">
          <el-tag v-if="row.reviewStatus === 0" type="warning">待复核</el-tag>
          <el-tag v-else-if="row.reviewStatus === 1" type="success">已通过</el-tag>
          <el-tag v-else type="danger">已退回</el-tag>
          <el-tooltip
            v-if="row.qtyAbnormal"
            content="这道工序报的合格品比上一道工序审过的还多，正常情况不该这样；如果是返工或补料，可以手动批准通过。"
            placement="top"
          >
            <el-tag type="warning" effect="dark" class="qty-abnormal-tag">数量异常</el-tag>
          </el-tooltip>
        </template>
      </el-table-column>
      <el-table-column prop="reportTime" label="报工时间" width="170" />
    </mes-table>
    <mes-pager v-model:page="page" v-model:page-size="pageSize" :total="total" @change="load" />

    <el-dialog v-model="rejectDlg" title="退回报工" width="420px">
      <el-form label-width="80px">
        <el-form-item label="原因" required>
          <el-input v-model="rejectReason" type="textarea" :rows="3" maxlength="256" show-word-limit placeholder="退回去的时候要写一下原因，告诉对方哪里有问题。" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="rejectDlg = false">取消</el-button>
        <el-button type="danger" @click="onReject">确定退回</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="specialDlg" title="确认特批通过这道工序" width="480px">
      <p class="dlg-body">
        这道工序报的良品 {{ specialRow?.goodQty ?? 0 }} 件，比上一道审过的 {{ specialRow?.upstreamLimit ?? 0 }} 件还多，正常不该这样；如果是返工、补料等特殊情况，确认后才能通过。
      </p>
      <el-form label-width="100px">
        <el-form-item label="特殊情况备注">
          <el-input
            v-model="specialRemark"
            type="textarea"
            :rows="2"
            maxlength="200"
            show-word-limit
            placeholder="选填，仅本次审核可见，不会保存"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="specialDlg = false">取消</el-button>
        <el-button type="warning" :loading="specialLoading" @click="confirmSpecialApprove">确认特批通过</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="batchDlg" title="以下单据数量有异常" width="480px">
      <p class="dlg-body">
        你选的里面有 {{ batchAbnormalCount }} 条数量不对（共 {{ batchTotal }} 条），系统会自动跳过这些，只通过没问题的。
      </p>
      <template #footer>
        <el-button @click="batchDlg = false">取消批量操作</el-button>
        <el-button @click="showBatchAbnormalDetail">查看异常明细</el-button>
        <el-button type="primary" :loading="batchLoading" @click="confirmBatchOkOnly">确认仅通过合规项</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { getPendingReviews, getReviewOrderOptions, approveReview, rejectReview, batchApproveReview } from '@/api/review'
import { getProductList } from '@/api/base'
import { ElMessage, ElMessageBox } from 'element-plus'

const route = useRoute()
const isAdmin = Number(localStorage.getItem('role') || 0) === 1
const list = ref([])
const selected = ref([])
const tableRef = ref(null)
const orderOptions = ref([])
const productOptions = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)

const FILTER_FIELDS = [
  { key: 'reviewStatus', label: '复核状态', valueType: 'reviewStatus' },
  { key: 'keyword', label: '工单号/产品', valueType: 'text' },
  { key: 'orderNo', label: '工单号', valueType: 'text' },
  { key: 'productCode', label: '产品编号', valueType: 'text' },
  { key: 'productName', label: '产品名称', valueType: 'text' },
  { key: 'operationName', label: '工序', valueType: 'text' },
  { key: 'userName', label: '报工人', valueType: 'text' },
  { key: 'goodQty', label: '良品数', valueType: 'number' },
  { key: 'reportTime', label: '报工时间', valueType: 'date' }
]
const REVIEW_STATUS_OPTIONS = [
  { value: 0, label: '待复核' },
  { value: 1, label: '已通过' },
  { value: 2, label: '已退回' }
]

const filterFieldKey = ref('reviewStatus')
const filterOp = ref('eq')
const filterValue = ref('0')

const currentFilterMeta = computed(() =>
  FILTER_FIELDS.find(f => f.key === filterFieldKey.value) || FILTER_FIELDS[0]
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
  if (t === 'reviewStatus') {
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

const selectedOne = computed(() => selected.value.length === 1 ? selected.value[0] : null)
const selectedPending = computed(() => selected.value.filter(r => r.reviewStatus === 0))
const rejectDlg = ref(false)
const rejectReason = ref('')
const rejectId = ref(null)

const specialDlg = ref(false)
const specialRow = ref(null)
const specialRemark = ref('')
const specialLoading = ref(false)

const batchDlg = ref(false)
const batchTotal = ref(0)
const batchAbnormalCount = ref(0)
const batchOkIds = ref([])
const batchAbnormalRows = ref([])
const batchLoading = ref(false)

onMounted(async () => {
  if (route.query.orderNo) {
    filterFieldKey.value = 'orderNo'
    filterOp.value = 'contains'
    filterValue.value = String(route.query.orderNo)
  }
  if (route.query.reviewStatus != null && route.query.reviewStatus !== '') {
    filterFieldKey.value = 'reviewStatus'
    filterOp.value = 'eq'
    filterValue.value = String(Number(route.query.reviewStatus))
  }
  await Promise.all([loadOrderOptions(), loadProductOptions(), load()])
})

async function loadOrderOptions() {
  const res = await getReviewOrderOptions()
  const rows = res.data || []
  orderOptions.value = rows.map(o => ({
    value: o.orderNo,
    label: `${o.orderNo}（${o.productName || ''}）`
  }))
}

async function loadProductOptions() {
  try {
    const products = await getProductList({ page: 1, pageSize: 200 })
    productOptions.value = products.data?.list || []
  } catch (error) {
    productOptions.value = []
    console.error('[报工复核] 加载产品列表失败', error)
  }
}

async function load() {
  const params = {
    filterField: filterFieldKey.value || 'reviewStatus',
    filterOp: filterOp.value || 'eq',
    page: page.value,
    pageSize: pageSize.value
  }
  if (filterValue.value != null && String(filterValue.value).trim() !== '') {
    params.filterValue = String(filterValue.value).trim()
  }
  const res = await getPendingReviews(params)
  list.value = res.data?.list || []
  total.value = res.data?.total || 0
  selected.value = []
  await nextTick()
  tableRef.value?.clearSelection?.()
}

function onFilterFieldChange() {
  const t = currentFilterMeta.value?.valueType
  if (t === 'number' || t === 'date' || t === 'reviewStatus') filterOp.value = 'eq'
  else filterOp.value = 'contains'
  filterValue.value = t === 'reviewStatus' ? '0' : ''
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
  filterFieldKey.value = 'reviewStatus'
  filterOp.value = 'eq'
  filterValue.value = '0'
  page.value = 1
  load()
}

function onSel(rows) { selected.value = rows || [] }

/** 点行切换勾选；批量通过仍可多选勾选框 */
function onRowClick(row, column, event) {
  if (event?.target?.closest?.('button, a, .el-checkbox, input, label')) return
  if (row.reviewStatus === 2) return
  const table = tableRef.value
  if (!table?.toggleRowSelection) return
  const checked = selected.value.some(r => r.id === row.id)
  table.toggleRowSelection(row, !checked)
}

function rowClassName({ row }) {
  return row.qtyAbnormal ? 'qty-abnormal-row' : ''
}

function formatDuration(m) {
  if (!m) return '-'
  const h = Math.floor(m / 60), min = m % 60
  if (h && min) return `${h}小时${min}分`
  if (h) return `${h}小时`
  return `${min}分`
}

function requireOne(actionLabel) {
  if (selected.value.length > 1) {
    ElMessage.warning(`「${actionLabel}」一次只能选一条，请先只勾选一条（批量请用「批量通过」）`)
    return null
  }
  if (!selectedOne.value) {
    ElMessage.warning(`请先勾选一条报工再${actionLabel}`)
    return null
  }
  return selectedOne.value
}

function onToolbarApprove() {
  const row = requireOne('通过')
  if (!row) return
  if (row.reviewStatus !== 0) {
    ElMessage.warning('只有待复核的报工才能通过')
    return
  }
  onApprove(row)
}

function onToolbarReject() {
  const row = requireOne('退回')
  if (!row) return
  if (row.reviewStatus === 2) {
    ElMessage.warning('已经退回的报工不用再退')
    return
  }
  openReject(row)
}

async function doApprove(id) {
  await approveReview(id)
  ElMessage.success('已通过')
  await Promise.all([load(), loadOrderOptions()])
}

async function onApprove(row) {
  if (row.qtyAbnormal) {
    specialRow.value = row
    specialRemark.value = ''
    specialDlg.value = true
    return
  }
  await doApprove(row.id)
}

async function confirmSpecialApprove() {
  if (!specialRow.value) return
  specialLoading.value = true
  try {
    await doApprove(specialRow.value.id)
    specialDlg.value = false
  } finally {
    specialLoading.value = false
  }
}

function openReject(row) {
  rejectId.value = row.id
  rejectReason.value = ''
  rejectDlg.value = true
}

async function onReject() {
  if (!rejectReason.value.trim()) {
    ElMessage.error('退回去的时候要写一下原因，告诉对方哪里有问题。')
    return
  }
  await rejectReview(rejectId.value, { reason: rejectReason.value.trim() })
  ElMessage.success('已退回')
  rejectDlg.value = false
  await Promise.all([load(), loadOrderOptions()])
}

async function onBatch() {
  const rows = selectedPending.value.slice()
  if (!rows.length) {
    ElMessage.warning('请先勾选待复核的报工再批量通过')
    return
  }
  const abnormal = rows.filter(r => r.qtyAbnormal)
  const ok = rows.filter(r => !r.qtyAbnormal)
  if (!abnormal.length) {
    await ElMessageBox.confirm(`确认通过选中的 ${ok.length} 条？`, '批量通过')
    const res = await batchApproveReview({ ids: ok.map(r => r.id) })
    ElMessage.success(`已通过 ${res.data?.approved ?? ok.length} 条`)
    await Promise.all([load(), loadOrderOptions()])
    return
  }
  batchTotal.value = rows.length
  batchAbnormalCount.value = abnormal.length
  batchOkIds.value = ok.map(r => r.id)
  batchAbnormalRows.value = abnormal
  batchDlg.value = true
}

function showBatchAbnormalDetail() {
  const lines = batchAbnormalRows.value.map(r =>
    `${r.orderNo} / ${r.operationName} / ${r.userName}：良品${r.goodQty}，可报上限${r.upstreamLimit ?? '-'}，同工序占用${r.sameOpOccupiedQty ?? '-'}`
  )
  ElMessageBox.alert(lines.join('<br/>') || '无', '异常明细', { dangerouslyUseHTMLString: true })
}

async function confirmBatchOkOnly() {
  const ids = batchOkIds.value.slice()
  if (!ids.length) {
    ElMessage.warning('你选的这些都有数量异常，没法直接批量通过，需要逐条处理。')
    batchDlg.value = false
    return
  }
  batchLoading.value = true
  try {
    const res = await batchApproveReview({ ids })
    ElMessage.success(`已通过 ${res.data?.approved ?? ids.length} 条（已跳过异常）`)
    batchDlg.value = false
    await Promise.all([load(), loadOrderOptions()])
  } finally {
    batchLoading.value = false
  }
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; align-items: center; }
.toolbar-field { width: 150px; }
.toolbar-match { width: 100px; }
.toolbar-value { width: 200px; }
.wage-miss { color: #f56c6c; margin-right: 4px; }
.qty-abnormal-tag { margin-left: 6px; }
.dlg-body { margin: 0 0 12px; line-height: 1.6; color: #606266; }
</style>
<style>
.el-table .qty-abnormal-row > td.el-table__cell {
  background: #fef9e7 !important;
}
</style>
