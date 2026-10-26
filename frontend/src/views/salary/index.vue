<template>
  <div>
    <ValueTip :text="VALUE_TIPS.salary" />
    <el-tabs v-model="mainTab" @tab-change="onMainTabChange">
      <el-tab-pane label="工资单" name="statements" />
      <el-tab-pane label="按工单" name="workOrder" />
    </el-tabs>

    <template v-if="mainTab === 'statements'">
    <div class="toolbar">
      <el-select v-model="q.periodType" style="width:100px" @change="onPeriodChange">
        <el-option :value="1" label="按月" />
        <el-option :value="2" label="按周" />
      </el-select>
      <el-input v-model="q.periodValue" :placeholder="q.periodType === 1 ? 'yyyy-MM' : 'yyyy-Www'" style="width:130px" @change="onPeriodChange" />
      <el-select v-model="q.status" clearable placeholder="状态" style="width:110px">
        <el-option :value="0" label="草稿" />
        <el-option :value="1" label="已确认" />
      </el-select>
      <el-input
        v-model="keyword"
        clearable
        :prefix-icon="Search"
        placeholder="姓名/账号"
        style="width:180px"
        @input="onKeywordInput"
        @clear="onKeywordClear"
      />
      <el-select v-model="q.userType" clearable placeholder="人员类型" style="width:140px" @change="onQuery">
        <el-option v-for="o in USER_TYPE_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
      </el-select>
      <el-button type="primary" @click="onQuery">查询</el-button>
      <el-button type="success" @click="openGen">生成工资单</el-button>
      <el-button
        type="warning"
        :disabled="isNarrow || !selectedIds.length"
        :loading="batching"
        :title="isNarrow ? '电脑端操作' : ''"
        @click="onBatchRecalc"
      >批量重算</el-button>
      <el-button @click="onExport">导出 CSV</el-button>
      <el-button
        v-if="!isNarrow"
        :type="itemsExpanded ? 'default' : 'primary'"
        plain
        @click="toggleItemsSection"
      >{{ itemsExpanded ? '收起工资明细' : '展开工资明细（报表层）' }}</el-button>
    </div>

    <!-- 窄屏：汇总卡片（docs/141） -->
    <div v-if="isNarrow" class="salary-cards">
      <div v-if="list.length === 0" class="cards-empty">暂无工资单</div>
      <div v-for="row in list" :key="row.id" class="salary-card">
        <div class="card-row1">
          <span class="card-name">{{ row.userName }}</span>
          <el-tag :type="userTypeTagType(row.userType)" size="small">{{ userTypeLabel(row.userType) }}</el-tag>
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

    <template v-else>
      <el-table
        :data="list"
        :height="tableHeight"
        @selection-change="onSelectionChange"
        @row-dblclick="openDetail"
      >
        <el-table-column type="selection" width="48" fixed />
        <el-table-column prop="userName" label="人员" width="90" fixed />
        <el-table-column label="人员类型" width="110">
          <template #default="{ row }">
            <el-tag :type="userTypeTagType(row.userType)" size="small">{{ userTypeLabel(row.userType) }}</el-tag>
          </template>
        </el-table-column>
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
      <div class="pager">
        <el-pagination
          v-model:current-page="q.page"
          v-model:page-size="q.pageSize"
          :total="total"
          :page-sizes="[10, 20, 50]"
          layout="total, sizes, prev, pager, next, jumper"
          @current-change="loadList"
          @size-change="onPageSizeChange"
        />
      </div>
    </template>

    <!-- 默认不显示；工具栏「展开」后才占下半屏。单人明细：点「明细」或双击行 → 抽屉 -->
    <div v-if="!isNarrow && itemsExpanded" class="items-section">
      <div class="items-title">工资明细（报表层）</div>
      <div class="toolbar">
        <el-select v-model="itemQ.operationId" clearable filterable placeholder="全部工序" style="width:160px">
          <el-option v-for="op in operations" :key="op.id" :label="op.name" :value="op.id" />
        </el-select>
        <el-select v-model="itemQ.userType" clearable placeholder="人员类型" style="width:140px">
          <el-option v-for="o in USER_TYPE_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
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
      <el-table :data="itemRows" size="small" height="calc(50vh - 120px)">
        <el-table-column prop="userName" label="人员" width="90" fixed />
        <el-table-column label="人员类型" width="110">
          <template #default="{ row }">
            <el-tag :type="userTypeTagType(row.userType)" size="small">{{ userTypeLabel(row.userType) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="orderNo" label="工单" width="110" />
        <el-table-column prop="operationName" label="工序" width="90" />
        <el-table-column prop="color" label="颜色" width="80" />
        <el-table-column prop="spec" label="规格尺码" width="90" />
        <el-table-column prop="goodQty" label="良品" width="60" />
        <el-table-column prop="defectQty" label="不良" width="60" />
        <el-table-column label="时长" width="85"><template #default="{ row }">{{ row.priceType === 2 ? (row.durationMinutes / 60).toFixed(1) + 'h' : '' }}</template></el-table-column>
        <el-table-column label="单价" width="115"><template #default="{ row }">{{ priceLabel(row) }}</template></el-table-column>
        <el-table-column prop="wageCoeff" label="系数" width="70" />
        <el-table-column prop="deductAmount" label="不良扣款" width="80" />
        <el-table-column prop="amount" label="小计" width="70" />
        <el-table-column prop="netAmount" label="应发" width="70" />
        <el-table-column prop="reportTime" label="报工时间" width="160" />
      </el-table>
      <div v-if="itemTotal > itemRows.length" class="items-foot">共 {{ itemTotal }} 条，当前显示 {{ itemRows.length }} 条</div>
    </div>
    </template>

    <!-- docs/420：按工单查累计 + 已完工应付台账 -->
    <template v-else>
      <div class="wo-block">
        <div class="wo-title">按工单查累计工资</div>
        <div class="toolbar">
          <el-select
            v-model="woOrderNo"
            filterable
            clearable
            placeholder="选择工单（可输入筛选）"
            style="width:360px"
            :loading="woOptionsLoading"
            @change="onWoOrderChange"
          >
            <el-option
              v-for="o in woOptions"
              :key="o.id"
              :label="woOptionLabel(o)"
              :value="o.orderNo"
            />
          </el-select>
          <el-button type="primary" :loading="woLoading" @click="onQueryWorkOrder">查询</el-button>
        </div>
        <template v-if="woSummary">
          <div class="wo-meta">
            <span>工单 {{ woSummary.orderNo }}</span>
            <span>{{ woSummary.productCode }} {{ woSummary.productName }}</span>
            <span>下单 {{ woSummary.qty }}</span>
            <span>已复核良品 {{ woSummary.reviewedGoodQty }}</span>
            <span class="wo-time">查询 {{ formatQueryTime(woSummary.queriedAt) }}</span>
          </div>
          <el-alert
            v-if="woSummary.unpricedCount > 0"
            type="warning"
            :closable="false"
            show-icon
            class="wo-alert"
            :title="`部分报工未计价（${woSummary.unpricedCount} 笔按 0 计入）`"
          />
          <el-alert
            v-if="Number(woSummary.unknownPriceTypeAmount) > 0"
            type="info"
            :closable="false"
            show-icon
            class="wo-alert"
            :title="`未知计价方式金额 ¥${fmtMoney(woSummary.unknownPriceTypeAmount)}（已计入小计，未进计件/计时/固定列）`"
          />
          <div class="summary-grid wo-summary-grid">
            <div class="summary-item summary-total">
              <div class="summary-label">应付合计</div>
              <div class="summary-value">¥{{ fmtMoney(woSummary.payableTotal) }}</div>
            </div>
            <div class="summary-item">
              <div class="summary-label">未复核金额</div>
              <div class="summary-value">¥{{ fmtMoney(woSummary.unreviewedAmount) }}</div>
            </div>
          </div>
          <div class="wo-sub">按工序</div>
          <el-table :data="woSummary.byOperation || []" size="small" empty-text="暂无已复核工资">
            <el-table-column prop="name" label="工序" min-width="100" />
            <el-table-column label="计件" width="90"><template #default="{ row }">{{ fmtMoney(row.pieceAmount) }}</template></el-table-column>
            <el-table-column label="计时" width="90"><template #default="{ row }">{{ fmtMoney(row.timeAmount) }}</template></el-table-column>
            <el-table-column label="固定" width="90"><template #default="{ row }">{{ fmtMoney(row.fixedAmount) }}</template></el-table-column>
            <el-table-column label="小计" width="100"><template #default="{ row }">{{ fmtMoney(row.subtotal) }}</template></el-table-column>
          </el-table>
          <div class="wo-sub">按人员</div>
          <el-table :data="woSummary.byUser || []" size="small" empty-text="暂无已复核工资">
            <el-table-column prop="name" label="人员" min-width="100" />
            <el-table-column label="计件" width="90"><template #default="{ row }">{{ fmtMoney(row.pieceAmount) }}</template></el-table-column>
            <el-table-column label="计时" width="90"><template #default="{ row }">{{ fmtMoney(row.timeAmount) }}</template></el-table-column>
            <el-table-column label="固定" width="90"><template #default="{ row }">{{ fmtMoney(row.fixedAmount) }}</template></el-table-column>
            <el-table-column label="小计" width="100"><template #default="{ row }">{{ fmtMoney(row.subtotal) }}</template></el-table-column>
          </el-table>
        </template>
      </div>

      <div class="wo-block">
        <div class="wo-title-row">
          <div class="wo-title">已完工工单应付台账</div>
          <el-button :loading="ledgerLoading" @click="loadCompletedLedger">刷新</el-button>
        </div>
        <el-alert
          v-if="ledger && ledger.unpricedCount > 0"
          type="warning"
          :closable="false"
          show-icon
          class="wo-alert"
          :title="`台账内有 ${ledger.unpricedCount} 笔报工未计价`"
        />
        <el-table
          :data="ledger?.list || []"
          size="small"
          empty-text="暂无已完工工单"
          max-height="420"
        >
          <el-table-column prop="orderNo" label="工单号" width="130" />
          <el-table-column label="产品" min-width="160">
            <template #default="{ row }">{{ row.productCode }} {{ row.productName }}</template>
          </el-table-column>
          <el-table-column prop="qty" label="数量" width="80" />
          <el-table-column label="应付合计" width="110">
            <template #default="{ row }">{{ fmtMoney(row.payableTotal) }}</template>
          </el-table-column>
          <el-table-column label="未复核金额" width="110">
            <template #default="{ row }">{{ fmtMoney(row.unreviewedAmount) }}</template>
          </el-table-column>
        </el-table>
        <div v-if="ledger" class="wo-ledger-foot">
          <span>应付总合计 <b>¥{{ fmtMoney(ledger.payableTotal) }}</b></span>
          <span>未复核合计 ¥{{ fmtMoney(ledger.unreviewedAmount) }}</span>
          <span class="wo-time">查询 {{ formatQueryTime(ledger.queriedAt) }}</span>
        </div>
      </div>
    </template>

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
          <div class="summary-value">{{ fmtMoney(categoryAmount(1)) }}</div>
        </div>
        <div class="summary-item">
          <div class="summary-label">计时合计（元）</div>
          <div class="summary-value">{{ fmtMoney(categoryAmount(2)) }}</div>
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
        <el-table-column label="时长" width="85"><template #default="{ row }">{{ row.priceType === 2 ? (row.durationMinutes / 60).toFixed(1) + 'h' : '' }}</template></el-table-column>
        <el-table-column label="单价" width="115"><template #default="{ row }">{{ priceLabel(row) }}</template></el-table-column>
        <el-table-column prop="wageCoeff" label="系数" width="70" />
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
function categoryAmount(type) { return (detail.value?.items || []).filter(x => x.priceType === type).reduce((sum, x) => sum + Number(x.amount || 0), 0) }
function priceLabel(row) { const n = Number(row.unitPrice || 0); return row.priceType === 1 ? `${n}元/件` : row.priceType === 2 ? `${n}元/时` : row.priceType === 3 ? `固定${n}元` : String(n) }

import { ref, computed, onMounted, onUnmounted } from 'vue'
import {
  generateSalary, getSalaryList, getSalary,
  confirmSalary, recalcSalary, revokeSalary, batchRecalcSalary, deleteSalary, exportSalary,
  getSalaryItems, exportSalaryItems,
  getSalaryWorkOrder, getSalaryCompletedWorkOrders
} from '@/api/salary'
import { getOrderList } from '@/api/prod'
import { getUserList } from '@/api/sys'
import { getOperationList } from '@/api/base'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Search } from '@element-plus/icons-vue'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'
import { USER_TYPE_OPTIONS, userTypeLabel, userTypeTagType } from '@/constants/userType'

const now = new Date()
const defaultPeriod = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
const mainTab = ref('statements')
const q = ref({ periodType: 1, periodValue: defaultPeriod, status: null, userType: null, page: 1, pageSize: 20 })
const keyword = ref('')
const list = ref([])
const total = ref(0)
const users = ref([])
const operations = ref([])
const selectedIds = ref([])
const batching = ref(false)
const genDlg = ref(false)
const gen = ref({ periodType: 1, periodValue: defaultPeriod, userId: null })
const detailDlg = ref(false)
const detail = ref(null)

const woOrderNo = ref('')
const woOptions = ref([])
const woOptionsLoading = ref(false)
const woLoading = ref(false)
const woSummary = ref(null)
const ledgerLoading = ref(false)
const ledger = ref(null)
let ledgerLoaded = false
let woOptionsLoaded = false
const WO_STATUS = { 0: '未开始', 1: '执行中', 2: '已结束' }
function woOptionLabel(o) {
  const st = WO_STATUS[o.status] ?? o.status
  const product = [o.productCode, o.productName].filter(Boolean).join(' ')
  return product ? `${o.orderNo} · ${product}（${st}）` : `${o.orderNo}（${st}）`
}

const itemQ = ref({ operationId: null, userType: null })
const itemRange = ref(null)
const itemRows = ref([])
const itemTotal = ref(0)
/** 下方报表层明细默认收起，点按钮才展开 */
const itemsExpanded = ref(false)

/** docs/141：与 layout 同口径 max-width 768 */
const isNarrow = ref(false)
let narrowMql
let keywordTimer = null

/** 收起时汇总表铺满；展开后只占上半屏 */
const tableHeight = computed(() =>
  itemsExpanded.value ? 'calc(50vh - 40px)' : 'calc(100vh - 220px)'
)

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

function formatQueryTime(v) {
  if (!v) return ''
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v)
  const p = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

async function onMainTabChange(name) {
  if (name !== 'workOrder') return
  await Promise.all([
    woOptionsLoaded ? Promise.resolve() : loadWoOptions(),
    ledgerLoaded ? Promise.resolve() : loadCompletedLedger()
  ])
}

function onWoOrderChange(val) {
  if (!val) {
    woSummary.value = null
    return
  }
  onQueryWorkOrder()
}

async function loadWoOptions() {
  woOptionsLoading.value = true
  try {
    const res = await getOrderList({ page: 1, pageSize: 500, excludeCancelled: true })
    woOptions.value = res.data?.list || []
    woOptionsLoaded = true
  } finally {
    woOptionsLoading.value = false
  }
}

async function onQueryWorkOrder() {
  const orderNo = woOrderNo.value?.trim()
  if (!orderNo) {
    ElMessage.error('请选择工单')
    return
  }
  woLoading.value = true
  try {
    const res = await getSalaryWorkOrder({ orderNo })
    woSummary.value = res.data || null
  } catch {
    woSummary.value = null
  } finally {
    woLoading.value = false
  }
}

async function loadCompletedLedger() {
  ledgerLoading.value = true
  try {
    const res = await getSalaryCompletedWorkOrders()
    ledger.value = res.data || null
    ledgerLoaded = true
  } finally {
    ledgerLoading.value = false
  }
}

function buildListParams() {
  const params = {
    periodType: q.value.periodType,
    periodValue: q.value.periodValue,
    status: q.value.status,
    page: q.value.page,
    pageSize: q.value.pageSize
  }
  const kw = keyword.value?.trim()
  if (kw) params.keyword = kw
  if (q.value.userType != null) params.userType = q.value.userType
  return params
}

function buildItemParams() {
  const params = {
    periodType: q.value.periodType,
    periodValue: q.value.periodValue,
    page: 1,
    pageSize: 500
  }
  if (itemQ.value.operationId) params.operationId = itemQ.value.operationId
  if (itemQ.value.userType != null) params.userType = itemQ.value.userType
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
  if (keywordTimer) clearTimeout(keywordTimer)
})

async function loadList() {
  const res = await getSalaryList(buildListParams())
  list.value = res.data?.list || []
  total.value = res.data?.total ?? list.value.length
  selectedIds.value = []
}

async function load() {
  await loadList()
  if (itemsExpanded.value) await loadItems()
}

async function toggleItemsSection() {
  itemsExpanded.value = !itemsExpanded.value
  if (itemsExpanded.value) await loadItems()
}

function onQuery() {
  q.value.page = 1
  load()
}

/** 切换周期：清 keyword、回第 1 页 */
function onPeriodChange() {
  keyword.value = ''
  q.value.page = 1
  load()
}

function onKeywordInput() {
  if (keywordTimer) clearTimeout(keywordTimer)
  keywordTimer = setTimeout(() => {
    q.value.page = 1
    loadList()
  }, 300)
}

function onKeywordClear() {
  if (keywordTimer) clearTimeout(keywordTimer)
  q.value.page = 1
  loadList()
}

function onPageSizeChange() {
  q.value.page = 1
  loadList()
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
  await ElMessageBox.confirm('按报工时工价快照重算报工明细，继续？', '重算')
  const res = await recalcSalary(row.id)
  const unmatched = res?.data?.unmatchedCount ?? 0
  if (unmatched > 0) {
    ElMessage.warning(`已重算，仍有 ${unmatched} 条未匹配工价，请先补齐工价`)
  } else {
    ElMessage.success('已按报工时工价快照重算')
  }
  load()
}

/** 已确认：软撤回回草稿 + 原地重算（与批量同语义，docs/136） */
async function onRevoke(row) {
  await ElMessageBox.confirm('撤回后回草稿并按报工时工价快照重算，报工解冻，工资单保留，继续？', '撤回重算')
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
  const exportParams = { periodType: q.value.periodType, periodValue: q.value.periodValue }
  if (q.value.userType != null) exportParams.userType = q.value.userType
  const res = await exportSalary(exportParams)
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
.pager { margin-top: 12px; display: flex; justify-content: flex-end; flex-wrap: wrap; }
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
.items-section { margin-top: 16px; }
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

.wo-block { margin-bottom: 28px; }
.wo-title { font-weight: 600; font-size: 15px; margin-bottom: 10px; }
.wo-title-row { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 10px; }
.wo-title-row .wo-title { margin-bottom: 0; }
.wo-meta {
  display: flex; flex-wrap: wrap; gap: 12px 16px;
  font-size: 13px; color: var(--el-text-color-regular); margin-bottom: 10px;
}
.wo-time { color: var(--el-text-color-secondary); font-size: 12px; }
.wo-alert { margin-bottom: 10px; }
.wo-summary-grid { grid-template-columns: 1fr 1fr; max-width: 480px; }
.wo-sub { font-weight: 600; margin: 14px 0 8px; font-size: 13px; }
.wo-ledger-foot {
  margin-top: 12px; display: flex; flex-wrap: wrap; gap: 16px;
  align-items: center; font-size: 14px;
}
.wo-ledger-foot b { color: var(--el-color-primary); font-size: 18px; }
</style>
