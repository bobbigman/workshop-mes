<template>
  <div>
    <ValueTip :text="VALUE_TIPS.report" />
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
      <el-input v-model="q.productCode" placeholder="产品编号" clearable style="width:160px" />
      <el-input v-model="q.productName" placeholder="产品名称" clearable style="width:160px" />
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" @click="openCreate()">+ 报工</el-button>
      <el-button v-if="canProxy" type="primary" plain @click="openProxy()">代报工</el-button>
      <el-button v-if="isAdmin" type="warning" @click="openBatch()">一键补报未完工序</el-button>
    </div>
    <el-table :data="list">
      <el-table-column prop="orderNo" label="工单编号" />
      <el-table-column prop="productName" label="产品" />
      <el-table-column prop="operationName" label="工序" />
      <el-table-column prop="userName" label="报工人" />
      <el-table-column prop="goodQty" label="良品" width="80" />
      <el-table-column prop="defectQty" label="不良品" width="80" />
      <el-table-column prop="defectName" label="不良原因" />
      <el-table-column label="单价" width="120">
        <template #default="{ row }">
          <template v-if="row.unitPrice != null">¥{{ Number(row.unitPrice).toFixed(4) }}</template>
          <template v-else>
            <span class="wage-miss">未配工价</span>
            <el-button v-if="isAdmin" link type="primary" @click="$router.push('/price-rule')">去补价</el-button>
          </template>
        </template>
      </el-table-column>
      <el-table-column label="时长" width="110">
        <template #default="{ row }">{{ formatDuration(row.durationMinutes) }}</template>
      </el-table-column>
      <el-table-column label="复核" width="90">
        <template #default="{ row }">
          <el-tag v-if="row.reviewStatus === 0" type="warning" size="small">待复核</el-tag>
          <el-tag v-else-if="row.reviewStatus === 1" type="success" size="small">已通过</el-tag>
          <el-tag v-else-if="row.reviewStatus === 2" type="danger" size="small">已退回</el-tag>
          <span v-else>-</span>
        </template>
      </el-table-column>
      <el-table-column prop="reportTime" label="时间" width="170" />
      <el-table-column label="操作" width="80">
        <template #default="{ row }">
          <el-button link type="primary" :disabled="row.reviewStatus === 1" @click="openEdit(row)">修改</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 新增报工（电脑端） -->
    <el-dialog v-model="createDlg" title="新增报工" width="480px" @closed="resetCreate">
      <el-form :model="createForm" label-width="90px">
        <el-form-item label="工单号" required>
          <el-select
            v-model="createForm.orderNo"
            filterable
            clearable
            allow-create
            default-first-option
            placeholder="选择或输入工单号"
            style="width:100%"
            @change="onCreateOrderChange"
          >
            <el-option
              v-for="o in orderOptions"
              :key="o.value"
              :label="o.label"
              :value="o.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="orderDetail" label="产品">
          <span>{{ orderDetail.productName }}（计划 {{ orderDetail.qty }}）</span>
        </el-form-item>
        <el-form-item v-if="orderDetail" label="工序进度">
          <div class="summary">
            <span>计划 {{ summary.plan }}</span>
            <span>良品 {{ summary.done }}</span>
            <span>不良 {{ summary.defect }}</span>
            <span>剩余 {{ summary.remain }}</span>
          </div>
        </el-form-item>
        <el-form-item label="工序" required>
          <el-select
            v-model="createForm.operationId"
            style="width:100%"
            placeholder="请先加载工单"
            :disabled="!orderDetail"
            @change="onOpChange"
          >
            <el-option
              v-for="o in (orderDetail?.tasks || [])"
              :key="o.operationId"
              :label="`${o.seq} ${o.operationName}`"
              :value="o.operationId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="良品数"><el-input-number v-model="createForm.goodQty" :min="0" :max="remainNum" /></el-form-item>
        <el-form-item label="不良品数"><el-input-number v-model="createForm.defectQty" :min="0" /></el-form-item>
        <el-form-item label="不良原因">
          <el-select v-model="createForm.defectId" clearable style="width:100%" placeholder="选择原因" :disabled="!createForm.defectQty">
            <el-option v-for="d in defectOptions" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="报工时长">
          <div class="duration">
            <el-input-number v-model="createForm.hours" :min="0" :max="24" />
            <span>小时</span>
            <el-input-number v-model="createForm.minutes" :min="0" :max="59" />
            <span>分钟</span>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDlg=false">取消</el-button>
        <el-button type="primary" :loading="submitting" :disabled="locked" @click="onCreate(false)">提交</el-button>
        <el-button type="success" :loading="submitting" :disabled="locked" @click="onCreate(true)">继续报工</el-button>
      </template>
    </el-dialog>

    <!-- 修改报工 -->
    <el-dialog v-model="dlg" title="修改报工" width="440px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="工序">
          <el-input :model-value="form.operationName" disabled />
        </el-form-item>
        <el-form-item label="良品数"><el-input-number v-model="form.goodQty" :min="0" /></el-form-item>
        <el-form-item label="不良品数"><el-input-number v-model="form.defectQty" :min="0" /></el-form-item>
        <el-form-item label="不良原因">
          <el-select v-model="form.defectId" clearable style="width:100%" placeholder="选择原因">
            <el-option v-for="d in defectOptions" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="报工时长">
          <div class="duration">
            <el-input-number v-model="form.hours" :min="0" :max="24" />
            <span>小时</span>
            <el-input-number v-model="form.minutes" :min="0" :max="59" />
            <span>分钟</span>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg=false">取消</el-button>
        <el-button type="primary" @click="onSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 代报工（docs/94，管理员/班组长） -->
    <el-dialog v-model="proxyDlg" title="代报工" width="720px" @closed="resetProxy">
      <el-form label-width="90px">
        <el-form-item label="工单号" required>
          <el-select
            v-model="proxyForm.orderNo"
            filterable
            clearable
            allow-create
            default-first-option
            placeholder="选择或输入工单号"
            style="width:100%"
            @change="onProxyOrderChange"
          >
            <el-option
              v-for="o in orderOptions"
              :key="o.value"
              :label="o.label"
              :value="o.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="proxyDetail" label="产品">
          <span>{{ proxyDetail.productName }}（计划 {{ proxyDetail.qty }}）</span>
        </el-form-item>
        <el-form-item label="工序" required>
          <el-select
            v-model="proxyForm.operationId"
            style="width:100%"
            placeholder="请先加载工单"
            :disabled="!proxyDetail"
            @change="onProxyOpChange"
          >
            <el-option
              v-for="o in (proxyDetail?.tasks || [])"
              :key="o.operationId"
              :label="`${o.seq} ${o.operationName}`"
              :value="o.operationId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="被代报人">
          <div class="alloc-wrap">
            <div class="hint">记到工人名下；各人数量可不同。选项已限该工序报权部门{{ isLeader ? '（班组长再限本组）' : '' }}。</div>
            <div v-for="(row, idx) in proxyForm.rows" :key="idx" class="alloc-user">
              <el-select v-model="row.userId" filterable placeholder="选工人" style="width:180px">
                <el-option v-for="p in proxyCandidates" :key="p.id" :label="p.name" :value="p.id" />
              </el-select>
              <span>良品</span>
              <el-input-number v-model="row.goodQty" :min="0" />
              <span>不良</span>
              <el-input-number v-model="row.defectQty" :min="0" />
              <el-select
                v-model="row.defectId"
                clearable
                placeholder="不良原因"
                style="width:140px"
                :disabled="!(row.defectQty > 0)"
              >
                <el-option v-for="d in proxyDefects" :key="d.id" :label="d.name" :value="d.id" />
              </el-select>
              <el-button link type="danger" :disabled="proxyForm.rows.length <= 1" @click="proxyForm.rows.splice(idx, 1)">删</el-button>
            </div>
            <el-button link type="primary" @click="addProxyRow">+ 加人</el-button>
            <div class="hint">合计良品 {{ proxyGoodSum }}</div>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="proxyDlg=false">取消</el-button>
        <el-button type="primary" :loading="proxySubmitting" @click="onProxySubmit">确认代报</el-button>
      </template>
    </el-dialog>

    <!-- 一键补报未完工序（docs/17，仅管理员） -->
    <el-dialog v-model="batchDlg" title="一键补报未完工序" width="720px" @closed="resetBatch">
      <el-form label-width="100px">
        <el-form-item label="工单号" required>
          <el-select
            v-model="batchForm.orderNo"
            filterable
            clearable
            allow-create
            default-first-option
            placeholder="选择或输入工单号"
            style="width:100%"
            @change="onBatchOrderChange"
          >
            <el-option
              v-for="o in orderOptions"
              :key="o.value"
              :label="o.label"
              :value="o.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="batchDetail" label="产品">
          <span>{{ batchDetail.productName }}（计划 {{ batchDetail.qty }}）</span>
        </el-form-item>
        <el-form-item v-if="batchDetail" label="待报工序">
          <el-table :data="pendingTasks" size="small" border empty-text="所有工序已报满">
            <el-table-column prop="seq" label="序号" width="60" />
            <el-table-column prop="operationName" label="工序" />
            <el-table-column prop="planQty" label="计划" width="80" />
            <el-table-column prop="doneQty" label="已报良品" width="90" />
            <el-table-column label="剩余可报" width="90">
              <template #default="{ row }">{{ row.remain }}</template>
            </el-table-column>
          </el-table>
        </el-form-item>
        <el-form-item v-if="pendingTasks.length" label="统一良品数" required>
          <el-input-number v-model="batchForm.goodQty" :min="1" :max="minRemain" />
          <span class="hint">上限 = 各待报工序剩余最小值（{{ minRemain }}）</span>
        </el-form-item>
        <el-form-item v-if="pendingTasks.length" label="谁做的">
          <div class="alloc-wrap">
            <div class="hint">默认挂当前管理员；需要计件到人时再按工序拆分，每道合计必须等于统一良品数。</div>
            <el-checkbox v-model="batchForm.split">按工序拆分到人</el-checkbox>
            <div v-if="batchForm.split" class="alloc-list">
              <div v-for="row in batchForm.allocRows" :key="row.operationId" class="alloc-op">
                <div class="alloc-op-title">
                  {{ row.seq }} {{ row.operationName }}
                  <span :class="{ bad: rowSum(row) !== batchForm.goodQty }">
                    合计 {{ rowSum(row) }} / {{ batchForm.goodQty }}
                  </span>
                  <el-button link type="primary" @click="addAllocUser(row)">+ 加人</el-button>
                </div>
                <div v-for="(u, idx) in row.users" :key="idx" class="alloc-user">
                  <el-select v-model="u.userId" filterable placeholder="选人" style="width:180px">
                    <el-option v-for="p in people" :key="p.id" :label="`${p.name}（${p.account}）`" :value="p.id" />
                  </el-select>
                  <el-input-number v-model="u.goodQty" :min="1" />
                  <el-button link type="danger" :disabled="row.users.length <= 1" @click="row.users.splice(idx, 1)">删</el-button>
                </div>
              </div>
            </div>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="batchDlg=false">取消</el-button>
        <el-button
          type="primary"
          :loading="batchSubmitting"
          :disabled="!pendingTasks.length"
          @click="onBatchSubmit"
        >确认补报</el-button>
      </template>
    </el-dialog>
  </div>
</template>
<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getReportList, updateReport, submitReport, batchReport, getDefectsByOp, getOrderByNo, getOrderList, getReportCandidates } from '@/api/prod'
import { getUserList } from '@/api/sys'
import { ElMessage } from 'element-plus'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'

const route = useRoute()
const router = useRouter()
const isAdmin = Number(localStorage.getItem('role') || 0) === 1
const isLeader = Number(localStorage.getItem('role') || 0) === 3
const canProxy = isAdmin || isLeader
const list = ref([]), dlg = ref(false), createDlg = ref(false), defectOptions = ref([])
const submitting = ref(false)
const orderDetail = ref(null)
const orderOptions = ref([])
const q = ref({ orderNo: '', productCode: '', productName: '' })
const form = ref({
  id: null, operationId: null, operationName: '',
  goodQty: 0, defectQty: 0, defectId: null, hours: 0, minutes: 0
})
const createForm = ref({
  orderNo: '', operationId: null, goodQty: 0, defectQty: 0, defectId: null, hours: 0, minutes: 0
})
const createClientRequestId = ref('')

const proxyDlg = ref(false)
const proxySubmitting = ref(false)
const proxyDetail = ref(null)
const proxyCandidates = ref([])
const proxyDefects = ref([])
const proxyClientRequestId = ref('')
const proxyForm = ref({
  orderNo: '', operationId: null, rows: [{ userId: null, goodQty: 0, defectQty: 0, defectId: null }]
})
const proxyGoodSum = computed(() =>
  (proxyForm.value.rows || []).reduce((s, r) => s + (Number(r.goodQty) || 0), 0)
)

const batchDlg = ref(false)
const batchSubmitting = ref(false)
const batchDetail = ref(null)
const people = ref([])
const batchForm = ref({
  orderNo: '', goodQty: 1, split: false, allocRows: []
})

const summary = computed(() => {
  if (!orderDetail.value || !createForm.value.operationId) {
    return { plan: '—', done: '—', defect: '—', remain: '—' }
  }
  const t = (orderDetail.value.tasks || []).find(x => x.operationId === createForm.value.operationId)
  if (!t) return { plan: '—', done: '—', defect: '—', remain: '—' }
  return { plan: t.planQty, done: t.doneQty, defect: t.defectQty, remain: Math.max(0, t.planQty - t.doneQty) }
})

const remainNum = computed(() => {
  const r = summary.value.remain
  return typeof r === 'number' ? r : 0
})
const locked = computed(() => !!createForm.value.operationId && remainNum.value <= 0)

const pendingTasks = computed(() => {
  if (!batchDetail.value) return []
  return (batchDetail.value.tasks || [])
    .map(t => ({
      ...t,
      remain: Math.max(0, (t.planQty || 0) - (t.doneQty || 0))
    }))
    .filter(t => t.remain > 0)
})

const minRemain = computed(() => {
  if (!pendingTasks.value.length) return 0
  return Math.min(...pendingTasks.value.map(t => t.remain))
})

function rowSum(row) {
  return (row.users || []).reduce((s, u) => s + (u.goodQty || 0), 0)
}

function formatDuration(m) {
  const n = m || 0
  if (n <= 0) return '—'
  const h = Math.floor(n / 60)
  const min = n % 60
  if (h > 0 && min > 0) return `${h}小时${min}分钟`
  if (h > 0) return `${h}小时`
  return `${min}分钟`
}

onMounted(async () => {
  await Promise.all([load(), loadOrderOptions()])
  const order = route.query.order
  if (order) {
    openCreate(String(order))
    router.replace({ path: '/report', query: {} })
  }
})

async function loadOrderOptions() {
  const res = await getOrderList({ excludeFinished: true, page: 1, pageSize: 500 })
  const rows = res.data?.list || []
  orderOptions.value = rows.map(o => ({
    value: o.orderNo,
    label: `${o.orderNo}（${o.productName || ''}）`
  }))
}

async function load() {
  const res = await getReportList({ ...q.value, page: 1, pageSize: 100 })
  list.value = res.data.list || []
}

function onCreateOrderChange(val) {
  if (!val) {
    orderDetail.value = null
    return
  }
  loadOrder()
}

function onProxyOrderChange(val) {
  if (!val) {
    proxyDetail.value = null
    return
  }
  loadProxyOrder()
}

function onBatchOrderChange(val) {
  if (!val) {
    batchDetail.value = null
    return
  }
  loadBatchOrder()
}

function openCreate(orderNo) {
  resetCreate()
  if (orderNo) createForm.value.orderNo = orderNo
  createDlg.value = true
  loadOrderOptions()
  if (orderNo) loadOrder()
}

function openProxy() {
  resetProxy()
  proxyDlg.value = true
  loadOrderOptions()
}

function openBatch() {
  resetBatch()
  batchDlg.value = true
  loadOrderOptions()
}

function newClientRequestId() {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID()
  return 'r-' + Date.now() + '-' + Math.random().toString(36).slice(2, 10)
}

function resetCreate() {
  orderDetail.value = null
  defectOptions.value = []
  createClientRequestId.value = ''
  createForm.value = {
    orderNo: '', operationId: null, goodQty: 0, defectQty: 0, defectId: null, hours: 0, minutes: 0
  }
}

async function loadOrder() {
  const no = (createForm.value.orderNo || '').trim()
  if (!no) {
    ElMessage.error('请输入工单编号')
    return
  }
  const res = await getOrderByNo(no)
  const data = res.data
  if (data.status === 2 || data.status === 3) {
    ElMessage.error(data.status === 2 ? '工单已结束，不可报工' : '工单已取消，不可报工')
    orderDetail.value = null
    return
  }
  orderDetail.value = data
  createForm.value.operationId = null
  createForm.value.defectId = null
  defectOptions.value = []
  const tasks = data.tasks || []
  if (tasks.length === 1) {
    createForm.value.operationId = tasks[0].operationId
    await onOpChange()
  }
}

async function onOpChange() {
  createForm.value.defectId = null
  if (!createForm.value.operationId) {
    defectOptions.value = []
    return
  }
  const res = await getDefectsByOp(createForm.value.operationId)
  defectOptions.value = res.data || []
}

async function onCreate(keepOpen) {
  if (!orderDetail.value) {
    ElMessage.error('请先加载工单')
    return
  }
  if (!createForm.value.operationId) {
    ElMessage.error('请选择工序')
    return
  }
  if (locked.value) {
    ElMessage.error('该工序已报满，不可继续报工')
    return
  }
  if (createForm.value.defectQty > 0 && !createForm.value.defectId) {
    ElMessage.error('有不良品时必须选择不良品原因')
    return
  }
  const durationMinutes = (createForm.value.hours || 0) * 60 + (createForm.value.minutes || 0)
  if (durationMinutes > 1440) {
    ElMessage.error('单次报工时长不能超过24小时')
    return
  }
  submitting.value = true
  try {
    if (!createClientRequestId.value) createClientRequestId.value = newClientRequestId()
    const res = await submitReport({
      orderId: orderDetail.value.id,
      operationId: createForm.value.operationId,
      goodQty: createForm.value.goodQty || 0,
      defectQty: createForm.value.defectQty || 0,
      defectId: createForm.value.defectId,
      durationMinutes,
      clientRequestId: createClientRequestId.value
    })
    createClientRequestId.value = ''
    const item = res?.data?.reports?.[0]
    const unmatched = (res?.data?.reports || []).some(r => r.unitPrice == null)
    let tip = item
      ? `报工成功：良品${item.goodQty} 不良${item.defectQty}`
      : '报工成功'
    if (unmatched) tip += '（未配工价）'
    if (unmatched) ElMessage.warning(tip)
    else ElMessage.success(tip)
    await load()
    if (keepOpen) {
      createForm.value.goodQty = 0
      createForm.value.defectQty = 0
      createForm.value.defectId = null
      createForm.value.hours = 0
      createForm.value.minutes = 0
      await loadOrder()
    } else {
      createDlg.value = false
    }
  } finally {
    submitting.value = false
  }
}

function resetProxy() {
  proxyDetail.value = null
  proxyCandidates.value = []
  proxyDefects.value = []
  proxyClientRequestId.value = ''
  proxyForm.value = {
    orderNo: '', operationId: null, rows: [{ userId: null, goodQty: 0, defectQty: 0, defectId: null }]
  }
}

function addProxyRow() {
  proxyForm.value.rows.push({ userId: null, goodQty: 0, defectQty: 0, defectId: null })
}

async function loadProxyOrder() {
  const no = (proxyForm.value.orderNo || '').trim()
  if (!no) {
    ElMessage.error('请输入工单编号')
    return
  }
  const res = await getOrderByNo(no)
  const data = res.data
  if (data.status === 2 || data.status === 3) {
    ElMessage.error(data.status === 2 ? '工单已结束，不可报工' : '工单已取消，不可报工')
    proxyDetail.value = null
    return
  }
  proxyDetail.value = data
  proxyForm.value.operationId = null
  proxyCandidates.value = []
  proxyDefects.value = []
  const tasks = data.tasks || []
  if (tasks.length === 1) {
    proxyForm.value.operationId = tasks[0].operationId
    await onProxyOpChange()
  }
}

async function onProxyOpChange() {
  proxyCandidates.value = []
  proxyDefects.value = []
  if (!proxyForm.value.operationId) return
  const [c, d] = await Promise.all([
    getReportCandidates(proxyForm.value.operationId),
    getDefectsByOp(proxyForm.value.operationId)
  ])
  proxyCandidates.value = c.data || []
  proxyDefects.value = d.data || []
}

async function onProxySubmit() {
  if (!proxyDetail.value) {
    ElMessage.error('请先加载工单')
    return
  }
  if (!proxyForm.value.operationId) {
    ElMessage.error('请选择工序')
    return
  }
  const rows = proxyForm.value.rows || []
  if (!rows.length) {
    ElMessage.error('请至少添加一名被代报人')
    return
  }
  for (const r of rows) {
    if (!r.userId) {
      ElMessage.error('请选择被代报人')
      return
    }
    if ((r.defectQty || 0) > 0 && !r.defectId) {
      ElMessage.error('有不良品时必须选择不良品原因')
      return
    }
  }
  proxySubmitting.value = true
  try {
    if (!proxyClientRequestId.value) proxyClientRequestId.value = newClientRequestId()
    const res = await submitReport({
      orderId: proxyDetail.value.id,
      operationId: proxyForm.value.operationId,
      goodQty: 0,
      defectQty: 0,
      clientRequestId: proxyClientRequestId.value,
      assignees: rows.map(r => ({
        userId: r.userId,
        goodQty: r.goodQty || 0,
        defectQty: r.defectQty || 0,
        defectId: r.defectId,
        durationMinutes: 0
      }))
    })
    proxyClientRequestId.value = ''
    const n = res?.data?.reports?.length || rows.length
    const unmatched = (res?.data?.reports || []).some(r => r.unitPrice == null)
    const tip = unmatched ? `已为 ${n} 人代报（含未配工价）` : `已为 ${n} 人代报`
    if (unmatched) ElMessage.warning(tip)
    else ElMessage.success(tip)
    proxyDlg.value = false
    await load()
  } finally {
    proxySubmitting.value = false
  }
}

async function openEdit(row) {
  const defects = await getDefectsByOp(row.operationId)
  defectOptions.value = defects.data || []
  const dm = row.durationMinutes || 0
  form.value = {
    id: row.id,
    operationId: row.operationId,
    operationName: row.operationName,
    goodQty: row.goodQty,
    defectQty: row.defectQty,
    defectId: row.defectId,
    hours: Math.floor(dm / 60),
    minutes: dm % 60
  }
  dlg.value = true
}

async function onSave() {
  if (form.value.defectQty > 0 && !form.value.defectId) {
    ElMessage.error('有不良品时必须选择不良品原因')
    return
  }
  const durationMinutes = (form.value.hours || 0) * 60 + (form.value.minutes || 0)
  if (durationMinutes > 1440) {
    ElMessage.error('单次报工时长不能超过24小时')
    return
  }
  await updateReport(form.value.id, {
    orderId: 0,
    operationId: form.value.operationId,
    goodQty: form.value.goodQty,
    defectQty: form.value.defectQty,
    defectId: form.value.defectId,
    durationMinutes
  })
  ElMessage.success('已保存')
  dlg.value = false
  await load()
}

function resetBatch() {
  batchDetail.value = null
  batchForm.value = { orderNo: '', goodQty: 1, split: false, allocRows: [] }
}

async function ensurePeople() {
  if (people.value.length) return
  const res = await getUserList({ page: 1, pageSize: 200 })
  people.value = res.data?.list || []
}

async function loadBatchOrder() {
  const no = (batchForm.value.orderNo || '').trim()
  if (!no) {
    ElMessage.error('请输入工单编号')
    return
  }
  const res = await getOrderByNo(no)
  const data = res.data
  if (data.status === 2 || data.status === 3) {
    ElMessage.error(data.status === 2 ? '工单已结束，不可补报' : '工单已取消，不可补报')
    batchDetail.value = null
    return
  }
  batchDetail.value = data
  // 等 pendingTasks 算好后再设默认数量
  await Promise.resolve()
  const min = Math.min(...(data.tasks || [])
    .map(t => Math.max(0, (t.planQty || 0) - (t.doneQty || 0)))
    .filter(r => r > 0))
  batchForm.value.goodQty = Number.isFinite(min) && min > 0 ? min : 1
  rebuildAllocRows()
}

function rebuildAllocRows() {
  batchForm.value.allocRows = pendingTasks.value.map(t => ({
    operationId: t.operationId,
    seq: t.seq,
    operationName: t.operationName,
    users: [{ userId: null, goodQty: batchForm.value.goodQty || 1 }]
  }))
}

function addAllocUser(row) {
  row.users.push({ userId: null, goodQty: 1 })
}

watch(() => batchForm.value.split, async (v) => {
  if (v) {
    await ensurePeople()
    rebuildAllocRows()
  }
})

watch(() => batchForm.value.goodQty, (n) => {
  if (!batchForm.value.split) return
  for (const row of batchForm.value.allocRows) {
    if (row.users.length === 1) row.users[0].goodQty = n || 1
  }
})

async function onBatchSubmit() {
  if (!batchDetail.value) {
    ElMessage.error('请先加载工单')
    return
  }
  if (!pendingTasks.value.length) {
    ElMessage.error('所有工序已报满')
    return
  }
  const n = batchForm.value.goodQty || 0
  if (n <= 0) {
    ElMessage.error('补报良品数必须大于 0')
    return
  }
  if (n > minRemain.value) {
    ElMessage.error(`本次数量超过剩余可报最小值 ${minRemain.value}`)
    return
  }

  let allocations = null
  if (batchForm.value.split) {
    for (const row of batchForm.value.allocRows) {
      if (rowSum(row) !== n) {
        ElMessage.error(`工序【${row.operationName}】拆分合计必须等于 ${n}`)
        return
      }
      for (const u of row.users) {
        if (!u.userId) {
          ElMessage.error(`工序【${row.operationName}】请选择报工人`)
          return
        }
      }
    }
    allocations = batchForm.value.allocRows.map(row => ({
      operationId: row.operationId,
      users: row.users.map(u => ({ userId: u.userId, goodQty: u.goodQty }))
    }))
  }

  // batch_no 最长 32：UUID 去横线正好 32（docs/17）
  const batchNo = (crypto.randomUUID && crypto.randomUUID().replace(/-/g, ''))
    || `${Date.now()}${Math.random().toString(16).slice(2, 10)}`.slice(0, 32)

  batchSubmitting.value = true
  try {
    const res = await batchReport({
      orderId: batchDetail.value.id,
      goodQty: n,
      batchNo,
      allocations
    })
    const data = res.data || {}
    ElMessage.success(`已为 ${data.operationCount || 0} 道工序生成 ${data.reportCount || 0} 条报工`)
    batchDlg.value = false
    await load()
  } finally {
    batchSubmitting.value = false
  }
}
</script>
<style scoped>
.toolbar{display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap}
.duration{display:flex;gap:8px;align-items:center}
.summary{display:flex;gap:16px;color:#606266;font-size:13px}
.hint{margin-left:8px;color:#909399;font-size:12px}
.wage-miss{color:#f56c6c;margin-right:4px}
.alloc-wrap{width:100%}
.alloc-list{margin-top:8px;display:flex;flex-direction:column;gap:12px}
.alloc-op{border:1px solid #ebeef5;border-radius:4px;padding:8px}
.alloc-op-title{display:flex;gap:12px;align-items:center;margin-bottom:6px;font-weight:500}
.alloc-op-title .bad{color:#f56c6c}
.alloc-user{display:flex;gap:8px;align-items:center;margin-bottom:4px}
</style>
