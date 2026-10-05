<template>
  <div>
    <ValueTip :text="VALUE_TIPS.salary" />
    <div class="toolbar">
      <el-select v-model="q.periodType" style="width:100px">
        <el-option :value="1" label="按月" />
        <el-option :value="2" label="按周" />
      </el-select>
      <el-input v-model="q.periodValue" :placeholder="q.periodType === 1 ? 'yyyy-MM' : 'yyyy-Www'" style="width:130px" />
      <el-select v-model="q.status" clearable placeholder="状态" style="width:110px">
        <el-option :value="0" label="草稿" />
        <el-option :value="1" label="已确认" />
      </el-select>
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" @click="openGen">生成工资单</el-button>
      <el-button
        type="warning"
        :disabled="isNarrow || !selectedIds.length"
        :loading="batching"
        :title="isNarrow ? '电脑端操作' : ''"
        @click="onBatchRecalc"
      >批量重算</el-button>
      <el-button @click="onExport">导出 CSV</el-button>
    </div>

    <!-- 窄屏：汇总卡片（docs/141） -->
    <div v-if="isNarrow" class="salary-cards">
      <div v-if="list.length === 0" class="cards-empty">暂无工资单</div>
      <div v-for="row in list" :key="row.id" class="salary-card">
        <div class="card-row1">
          <span class="card-name">{{ row.userName }}</span>
          <el-tag v-if="row.status === 0" type="info" size="small">草稿</el-tag>
          <el-tag v-else type="success" size="small">已确认</el-tag>
        </div>
        <div class="card-row2">
          <span>{{ row.periodValue }}</span>
          <span class="card-time">{{ row.createdAt }}</span>
        </div>
        <div class="card-amount">¥{{ fmtMoney(row.totalAmount) }}</div>
        <div class="card-actions">
          <el-button link type="primary" @click="openDetail(row)">明细</el-button>
          <el-button v-if="row.status === 0" link type="warning" @click="onRecalc(row)">重算</el-button>
          <el-button v-if="row.status === 0" link type="success" @click="onConfirm(row)">确认</el-button>
          <el-button v-if="row.status === 0" link type="danger" @click="onDelete(row)">删除</el-button>
          <el-button v-if="row.status === 1" link type="danger" @click="onRevoke(row)">撤回重算</el-button>
        </div>
      </div>
    </div>

    <el-table v-else :data="list" @selection-change="onSelectionChange">
      <el-table-column type="selection" width="48" fixed />
      <el-table-column prop="userName" label="人员" width="90" fixed />
      <el-table-column prop="periodValue" label="周期" width="100" />
      <el-table-column prop="totalAmount" label="应发合计" width="100" />
      <el-table-column label="状态" width="80">
        <template #default="{ row }">
          <el-tag v-if="row.status === 0" type="info">草稿</el-tag>
          <el-tag v-else type="success">已确认</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="createdAt" label="生成时间" width="160" />
      <el-table-column label="操作" width="320" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openDetail(row)">明细</el-button>
          <el-button v-if="row.status === 0" link type="warning" @click="onRecalc(row)">重算</el-button>
          <el-button v-if="row.status === 0" link type="success" @click="onConfirm(row)">确认</el-button>
          <el-button v-if="row.status === 0" link type="danger" @click="onDelete(row)">删除</el-button>
          <el-button v-if="row.status === 1" link type="danger" @click="onRevoke(row)">撤回重算</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- docs/139：报表层明细直显；docs/141：窄屏隐藏，不抢首屏 -->
    <div v-if="!isNarrow" class="items-section">
      <div class="items-title">工资明细（报表层）</div>
      <div class="toolbar">
        <el-select v-model="itemQ.operationId" clearable filterable placeholder="全部工序" style="width:160px">
          <el-option v-for="op in operations" :key="op.id" :label="op.name" :value="op.id" />
        </el-select>
        <el-date-picker
          v-model="itemRange"
          type="daterange"
          value-format="YYYY-MM-DD"
          start-placeholder="报工起"
          end-placeholder="报工止"
          style="width:260px"
        />
        <el-button type="primary" @click="loadItems">查明细</el-button>
        <el-button @click="onExportItems">导出明细 CSV</el-button>
      </div>
      <el-table :data="itemRows" size="small" max-height="420">
        <el-table-column prop="userName" label="人员" width="90" fixed />
        <el-table-column prop="orderNo" label="工单" width="110" />
        <el-table-column prop="operationName" label="工序" width="90" />
        <el-table-column prop="color" label="颜色" width="80" />
        <el-table-column prop="spec" label="规格尺码" width="90" />
        <el-table-column prop="goodQty" label="良品" width="60" />
        <el-table-column prop="defectQty" label="不良" width="60" />
        <el-table-column prop="unitPrice" label="单价" width="70" />
        <el-table-column prop="deductAmount" label="不良扣款" width="80" />
        <el-table-column prop="amount" label="小计" width="70" />
        <el-table-column prop="netAmount" label="应发" width="70" />
        <el-table-column prop="reportTime" label="报工时间" width="160" />
      </el-table>
      <div v-if="itemTotal > itemRows.length" class="items-foot">共 {{ itemTotal }} 条，当前显示 {{ itemRows.length }} 条</div>
    </div>

    <el-dialog v-model="genDlg" title="生成工资单" width="420px">
      <el-form label-width="90px">
        <el-form-item label="周期类型">
          <el-select v-model="gen.periodType" style="width:100%">
            <el-option :value="1" label="按月" />
            <el-option :value="2" label="按周" />
          </el-select>
        </el-form-item>
        <el-form-item label="周期值" required>
          <el-input v-model="gen.periodValue" :placeholder="gen.periodType === 1 ? '如 2026-09' : '如 2026-W37'" />
        </el-form-item>
        <el-form-item label="人员">
          <el-select v-model="gen.userId" clearable filterable style="width:100%" placeholder="空=全厂有报工人">
            <el-option v-for="u in users" :key="u.id" :label="u.name" :value="u.id" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="genDlg = false">取消</el-button>
        <el-button type="primary" @click="onGenerate">生成</el-button>
      </template>
    </el-dialog>

    <el-drawer v-model="detailDlg" title="工资明细" size="720px">
      <div v-if="detail" class="detail-head">
        <span>{{ detail.userName }}</span>
        <span>{{ detail.periodValue }}</span>
      </div>

      <div v-if="detail" class="summary-grid">
        <div class="summary-item">
          <div class="summary-label">计件合计</div>
          <div class="summary-value">{{ fmtMoney(pieceworkAmount) }}</div>
        </div>
        <div class="summary-item summary-total">
          <div class="summary-label">应发合计</div>
          <div class="summary-value">{{ fmtMoney(previewTotal) }}</div>
        </div>
      </div>

      <el-table :data="detail?.items || []" size="small">
        <el-table-column prop="orderNo" label="工单" width="110" />
        <el-table-column prop="operationName" label="工序" width="90" />
        <el-table-column prop="color" label="颜色" width="80" />
        <el-table-column prop="spec" label="规格尺码" width="90" />
        <el-table-column prop="goodQty" label="良品" width="60" />
        <el-table-column prop="defectQty" label="不良" width="60" />
        <el-table-column prop="unitPrice" label="单价" width="70" />
        <el-table-column prop="amount" label="小计" width="70" />
        <el-table-column prop="deductAmount" label="扣款" width="70" />
        <el-table-column prop="netAmount" label="应发" width="70" />
        <el-table-column label="匹配" width="70">
          <template #default="{ row }">
            <el-tag v-if="row.matched" type="success" size="small">是</el-tag>
            <el-tag v-else type="danger" size="small">未匹配</el-tag>
          </template>
        </el-table-column>
      </el-table>
    </el-drawer>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import {
  generateSalary, getSalaryList, getSalary,
  confirmSalary, recalcSalary, revokeSalary, batchRecalcSalary, deleteSalary, exportSalary,
  getSalaryItems, exportSalaryItems
} from '@/api/salary'
import { getUserList } from '@/api/sys'
import { getOperationList } from '@/api/base'
import { ElMessage, ElMessageBox } from 'element-plus'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'

const now = new Date()
const defaultPeriod = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
const q = ref({ periodType: 1, periodValue: defaultPeriod, status: null, page: 1, pageSize: 50 })
const list = ref([])
const users = ref([])
const operations = ref([])
const selectedIds = ref([])
const batching = ref(false)
const genDlg = ref(false)
const gen = ref({ periodType: 1, periodValue: defaultPeriod, userId: null })
const detailDlg = ref(false)
const detail = ref(null)

const itemQ = ref({ operationId: null })
const itemRange = ref(null)
const itemRows = ref([])
const itemTotal = ref(0)

/** docs/141：与 layout 同口径 max-width 768 */
const isNarrow = ref(false)
let narrowMql

function checkNarrow() {
  isNarrow.value = window.matchMedia('(max-width: 768px)').matches
}

const pieceworkAmount = computed(() => Number(detail.value?.pieceworkAmount ?? 0))
const previewTotal = computed(() => Number(detail.value?.totalAmount ?? pieceworkAmount.value))

function fmtMoney(v) {
  const n = Number(v)
  if (Number.isNaN(n)) return '0.00'
  return n.toFixed(2)
}

function buildItemParams() {
  const params = {
    periodType: q.value.periodType,
    periodValue: q.value.periodValue,
    page: 1,
    pageSize: 500
  }
  if (itemQ.value.operationId) params.operationId = itemQ.value.operationId
  if (itemRange.value?.length === 2) {
    params.from = `${itemRange.value[0]}T00:00:00`
    // 止日按「不含次日 0 点」：传次日 00:00
    const end = new Date(itemRange.value[1] + 'T00:00:00')
    end.setDate(end.getDate() + 1)
    const y = end.getFullYear()
    const m = String(end.getMonth() + 1).padStart(2, '0')
    const d = String(end.getDate()).padStart(2, '0')
    params.to = `${y}-${m}-${d}T00:00:00`
  }
  return params
}

onMounted(async () => {
  checkNarrow()
  narrowMql = window.matchMedia('(max-width: 768px)')
  narrowMql.addEventListener('change', checkNarrow)
  const [us, ops] = await Promise.all([
    getUserList({ page: 1, pageSize: 500 }),
    getOperationList({ page: 1, pageSize: 500 })
  ])
  users.value = us.data?.list || []
  operations.value = ops.data?.list || []
  await load()
})

onUnmounted(() => {
  narrowMql?.removeEventListener('change', checkNarrow)
})

async function load() {
  const res = await getSalaryList(q.value)
  list.value = res.data?.list || []
  selectedIds.value = []
  await loadItems()
}

async function loadItems() {
  if (!q.value.periodValue?.trim()) {
    itemRows.value = []
    itemTotal.value = 0
    return
  }
  const res = await getSalaryItems(buildItemParams())
  itemRows.value = res.data?.list || []
  itemTotal.value = res.data?.total ?? itemRows.value.length
}

function onSelectionChange(rows) {
  selectedIds.value = (rows || []).map(r => r.id)
}

function openGen() {
  gen.value = { periodType: q.value.periodType, periodValue: q.value.periodValue, userId: null }
  genDlg.value = true
}

async function onGenerate() {
  if (!gen.value.periodValue?.trim()) {
    ElMessage.error('请填写周期值')
    return
  }
  const res = await generateSalary(gen.value)
  const created = res.data?.created ?? 0
  const unmatched = res.data?.unmatchedCount ?? 0
  if (unmatched > 0) {
    ElMessage.warning(`已生成 ${created} 张，但有 ${unmatched} 条报工未匹配工价、金额为0，请先到工价表补价，别把缺价当零工资发`)
  } else {
    ElMessage.success(`已生成 ${created} 张工资单`)
  }
  genDlg.value = false
  q.value.periodType = gen.value.periodType
  q.value.periodValue = gen.value.periodValue
  load()
}

async function openDetail(row) {
  const res = await getSalary(row.id)
  detail.value = res.data
  detailDlg.value = true
}

async function onConfirm(row) {
  await ElMessageBox.confirm('确认后报工将冻结不可再改，继续？', '确认工资单')
  await confirmSalary(row.id)
  ElMessage.success('已确认')
  load()
}

async function onDelete(row) {
  await ElMessageBox.confirm('删除该草稿工资单？', '确认')
  await deleteSalary(row.id)
  ElMessage.success('已删除')
  load()
}

async function onRecalc(row) {
  await ElMessageBox.confirm('按当前工价重算报工明细，继续？', '重算')
  const res = await recalcSalary(row.id)
  const unmatched = res?.data?.unmatchedCount ?? 0
  if (unmatched > 0) {
    ElMessage.warning(`已重算，仍有 ${unmatched} 条未匹配工价，请先补齐工价`)
  } else {
    ElMessage.success('已按当前工价重算')
  }
  load()
}

/** 已确认：软撤回回草稿 + 原地重算（与批量同语义，docs/136） */
async function onRevoke(row) {
  await ElMessageBox.confirm('撤回后回草稿并按当前工价重算，报工解冻，工资单保留，继续？', '撤回重算')
  await revokeSalary(row.id)
  const res = await recalcSalary(row.id)
  const unmatched = res?.data?.unmatchedCount ?? 0
  if (unmatched > 0) {
    ElMessage.warning(`已撤回并重算，仍有 ${unmatched} 条未匹配工价，请先补齐工价`)
  } else {
    ElMessage.success('已撤回并重算')
  }
  load()
}

async function onBatchRecalc() {
  if (!selectedIds.value.length) {
    ElMessage.warning('请先勾选工资单')
    return
  }
  await ElMessageBox.confirm(
    `将对选中的 ${selectedIds.value.length} 张工资单批量重算：草稿直接重算，已确认先撤回再重算。继续？`,
    '批量重算'
  )
  batching.value = true
  try {
    const res = await batchRecalcSalary(selectedIds.value)
    const rows = res.data?.list || []
    const ok = res.data?.ok ?? rows.filter(r => r.success).length
    const fail = res.data?.fail ?? rows.filter(r => !r.success).length
    if (fail === 0) {
      ElMessage.success(`批量重算完成：成功 ${ok} 张`)
    } else {
      const fails = rows.filter(r => !r.success)
        .map(r => `#${r.id} ${r.message || '失败'}`)
        .slice(0, 5)
        .join('；')
      ElMessage.warning(`成功 ${ok} / 失败 ${fail}。${fails}${fail > 5 ? '…' : ''}`)
    }
    load()
  } finally {
    batching.value = false
  }
}

async function onExport() {
  if (!q.value.periodValue) {
    ElMessage.error('请先填写周期值')
    return
  }
  const res = await exportSalary({ periodType: q.value.periodType, periodValue: q.value.periodValue })
  const blob = res.data instanceof Blob ? res.data : new Blob([res.data])
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `salary_${q.value.periodValue}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

async function onExportItems() {
  if (!q.value.periodValue?.trim()) {
    ElMessage.error('请先填写周期值')
    return
  }
  const res = await exportSalaryItems(buildItemParams())
  const blob = res.data instanceof Blob ? res.data : new Blob([res.data])
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `salary_items_${q.value.periodValue}.csv`
  a.click()
  URL.revokeObjectURL(url)
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; align-items: center; }
.detail-head { display: flex; gap: 16px; margin-bottom: 12px; font-weight: 600; }
.summary-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  margin-bottom: 16px;
}
.summary-item {
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  padding: 10px 12px;
}
.summary-total { grid-column: 1 / -1; background: var(--el-fill-color-light); }
.summary-label { font-size: 12px; color: var(--el-text-color-secondary); margin-bottom: 4px; }
.summary-value { font-size: 18px; font-weight: 600; }
.summary-total .summary-value { font-size: 22px; color: var(--el-color-primary); }
.items-section { margin-top: 24px; }
.items-title { font-weight: 600; margin-bottom: 8px; }
.items-foot { margin-top: 8px; font-size: 12px; color: var(--el-text-color-secondary); }

.salary-cards { display: flex; flex-direction: column; gap: 10px; }
.cards-empty { text-align: center; color: var(--el-text-color-secondary); padding: 28px 0; font-size: 14px; }
.salary-card {
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
  padding: 12px 14px;
}
.card-row1 { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }
.card-name { font-size: 16px; font-weight: 700; color: var(--el-text-color-primary); }
.card-row2 {
  display: flex; justify-content: space-between; gap: 8px;
  font-size: 13px; color: var(--el-text-color-regular); margin-bottom: 8px;
}
.card-time { font-size: 12px; color: var(--el-text-color-secondary); }
.card-amount {
  font-size: 22px; font-weight: 700; color: var(--el-color-primary); margin-bottom: 8px;
}
.card-actions { display: flex; flex-wrap: wrap; gap: 2px; margin: 0 -4px; }
</style>
