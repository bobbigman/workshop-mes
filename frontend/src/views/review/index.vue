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
    <el-table :data="list" @selection-change="onSel">
      <el-table-column type="selection" width="48" :selectable="row => row.reviewStatus === 0" />
      <el-table-column prop="orderNo" label="工单" width="140" />
      <el-table-column prop="productName" label="产品" />
      <el-table-column prop="operationName" label="工序" width="100" />
      <el-table-column prop="userName" label="工人" width="90" />
      <el-table-column prop="goodQty" label="良品" width="70" />
      <el-table-column prop="defectQty" label="不良" width="70" />
      <el-table-column label="单价" width="130">
        <template #default="{ row }">
          <template v-if="row.unitPrice != null">¥{{ Number(row.unitPrice).toFixed(4) }}</template>
          <template v-else>
            <span class="wage-miss">未配工价</span>
            <el-button v-if="isAdmin" link type="primary" @click="$router.push('/price-rule')">去补价</el-button>
          </template>
        </template>
      </el-table-column>
      <el-table-column label="时长" width="90">
        <template #default="{ row }">{{ formatDuration(row.durationMinutes) }}</template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag v-if="row.reviewStatus === 0" type="warning">待复核</el-tag>
          <el-tag v-else-if="row.reviewStatus === 1" type="success">已通过</el-tag>
          <el-tag v-else type="danger">已退回</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="reportTime" label="报工时间" width="170" />
      <el-table-column label="操作" width="160" fixed="right">
        <template #default="{ row }">
          <el-button v-if="row.reviewStatus === 0" link type="success" @click="onApprove(row)">通过</el-button>
          <el-button v-if="row.reviewStatus !== 2" link type="danger" @click="openReject(row)">退回</el-button>
        </template>
      </el-table-column>
    </el-table>

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

function formatDuration(m) {
  if (!m) return '-'
  const h = Math.floor(m / 60), min = m % 60
  if (h && min) return `${h}小时${min}分`
  if (h) return `${h}小时`
  return `${min}分`
}

async function onApprove(row) {
  await approveReview(row.id)
  ElMessage.success('已通过')
  await Promise.all([load(), loadOrderOptions()])
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
  const ids = selected.value.map(r => r.id)
  if (!ids.length) return
  await ElMessageBox.confirm(`确认通过选中的 ${ids.length} 条？`, '批量通过')
  const res = await batchApproveReview({ ids })
  ElMessage.success(`已通过 ${res.data?.approved ?? ids.length} 条`)
  await Promise.all([load(), loadOrderOptions()])
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; align-items: center; }
.wage-miss { color: #f56c6c; margin-right: 4px; }
</style>
