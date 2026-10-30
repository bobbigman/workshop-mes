<template>
  <div>
    <div class="toolbar">
      <el-select
        v-model="q.orderNo"
        filterable
        clearable
        allow-create
        default-first-option
        placeholder="工单编号"
        style="width:220px"
        @change="load"
        @clear="load"
      >
        <el-option
          v-for="o in orderOptions"
          :key="o.value"
          :label="o.label"
          :value="o.value"
        />
      </el-select>
      <el-input v-model="q.userName" placeholder="工人姓名" clearable style="width:140px" />
      <el-select v-model="q.reviewStatus" style="width:120px">
        <el-option :value="0" label="待复核" />
        <el-option :value="1" label="已通过" />
        <el-option :value="2" label="已退回" />
      </el-select>
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" :disabled="!selected.length" @click="onBatch">批量通过</el-button>
    </div>
    <mes-table table-key="views/review/index:1" :data="list" :row-class-name="rowClassName" @selection-change="onSel">
      <el-table-column type="selection" width="48" :selectable="row => row.reviewStatus === 0" />
      <el-table-column prop="orderNo" label="工单" width="140" />
      <el-table-column prop="productName" label="产品" />
      <el-table-column prop="operationName" label="工序" width="100" />
      <el-table-column prop="userName" label="工人" width="90" />
      <el-table-column prop="goodQty" label="良品" width="70" />
      <el-table-column prop="defectQty" label="不良" width="70" />
      <el-table-column label="上工序合格库存（可报上限）" width="160">
        <template #default="{ row }">
          {{ row.upstreamLimit == null ? '-' : row.upstreamLimit }}
        </template>
      </el-table-column>
      <el-table-column label="单价" width="130">
        <template #default="{ row }">
          <template v-if="row.unitPrice != null">¥{{ Number(row.unitPrice).toFixed(4) }}</template>
          <template v-else>
            <span class="wage-miss">未配工价</span>
            <el-button v-if="isAdmin" link type="primary" @click="$router.push('/operation-price-list')">去补价</el-button>
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
            content="本工序报工良品超过上工序已审核合格产出，正常生产逻辑不合理，若为返工、补料可手动特批通过"
            placement="top"
          >
            <el-tag type="warning" effect="dark" class="qty-abnormal-tag">数量异常</el-tag>
          </el-tooltip>
        </template>
      </el-table-column>
      <el-table-column prop="reportTime" label="报工时间" width="170" />
      <el-table-column label="操作" width="160" fixed="right">
        <template #default="{ row }">
          <el-button v-if="row.reviewStatus === 0" link type="success" @click="onApprove(row)">通过</el-button>
          <el-button v-if="row.reviewStatus !== 2" link type="danger" @click="openReject(row)">退回</el-button>
        </template>
      </el-table-column>
    </mes-table>

    <el-dialog v-model="rejectDlg" title="退回报工" width="420px">
      <el-form label-width="80px">
        <el-form-item label="原因" required>
          <el-input v-model="rejectReason" type="textarea" :rows="3" maxlength="256" show-word-limit placeholder="请填写退回原因" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="rejectDlg = false">取消</el-button>
        <el-button type="danger" @click="onReject">确定退回</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="specialDlg" title="工序数量异常确认放行" width="480px">
      <p class="dlg-body">
        当前工序报工良品 {{ specialRow?.goodQty ?? 0 }} 件，上工序累计合格仅 {{ specialRow?.upstreamLimit ?? 0 }} 件，超出正常生产数量。请确认是否为返工、补料、特采等特殊场景，确认后才可完成复核通过。
      </p>
      <el-form label-width="100px">
        <el-form-item label="特殊情况备注">
          <el-input
            v-model="specialRemark"
            type="textarea"
            :rows="2"
            maxlength="200"
            show-word-limit
            placeholder="选填，备注不落库"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="specialDlg = false">取消</el-button>
        <el-button type="warning" :loading="specialLoading" @click="confirmSpecialApprove">确认特批通过</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="batchDlg" title="存在工序数量异常单据" width="480px">
      <p class="dlg-body">
        本次勾选共 {{ batchTotal }} 条单据，其中 {{ batchAbnormalCount }} 条报工数量超出上工序合格库存，系统将自动跳过异常单据，仅批量通过合规单据。
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
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { getPendingReviews, getReviewOrderOptions, approveReview, rejectReview, batchApproveReview } from '@/api/review'
import { ElMessage, ElMessageBox } from 'element-plus'

const route = useRoute()
const isAdmin = Number(localStorage.getItem('role') || 0) === 1
const list = ref([])
const selected = ref([])
const orderOptions = ref([])
const q = ref({ orderNo: '', userName: '', reviewStatus: 0, page: 1, pageSize: 50 })
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
  if (route.query.orderNo) q.value.orderNo = String(route.query.orderNo)
  if (route.query.reviewStatus != null && route.query.reviewStatus !== '')
    q.value.reviewStatus = Number(route.query.reviewStatus)
  await Promise.all([loadOrderOptions(), load()])
})

async function loadOrderOptions() {
  const res = await getReviewOrderOptions()
  const rows = res.data || []
  orderOptions.value = rows.map(o => ({
    value: o.orderNo,
    label: `${o.orderNo}（${o.productName || ''}）`
  }))
}

async function load() {
  const res = await getPendingReviews(q.value)
  list.value = res.data?.list || []
}

function onSel(rows) { selected.value = rows }

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
    ElMessage.error('请填写退回原因')
    return
  }
  await rejectReview(rejectId.value, { reason: rejectReason.value.trim() })
  ElMessage.success('已退回')
  rejectDlg.value = false
  await Promise.all([load(), loadOrderOptions()])
}

async function onBatch() {
  const rows = selected.value.slice()
  if (!rows.length) return
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
    `${r.orderNo} / ${r.operationName} / ${r.userName}：良品${r.goodQty}，上限${r.upstreamLimit ?? '-'}，同工序占用${r.sameOpOccupiedQty ?? '-'}`
  )
  ElMessageBox.alert(lines.join('<br/>') || '无', '异常明细', { dangerouslyUseHTMLString: true })
}

async function confirmBatchOkOnly() {
  const ids = batchOkIds.value.slice()
  if (!ids.length) {
    ElMessage.warning('没有可批量通过的合规单据')
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
.wage-miss { color: #f56c6c; margin-right: 4px; }
.qty-abnormal-tag { margin-left: 6px; }
.dlg-body { margin: 0 0 12px; line-height: 1.6; color: #606266; }
</style>
<style>
.el-table .qty-abnormal-row > td.el-table__cell {
  background: #fef9e7 !important;
}
</style>
