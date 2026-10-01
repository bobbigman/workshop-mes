<template>
  <div>
    <ValueTip :text="VALUE_TIPS.order" />
    <div class="toolbar">
      <el-input v-model="keyword" class="toolbar-search" placeholder="工单号/产品" clearable @keyup.enter="onSearch" />
      <template v-if="customFields.length">
        <el-select
          v-model="filterFieldId"
          clearable
          placeholder="自定义字段"
          class="toolbar-field"
          @change="onFilterFieldChange"
        >
          <el-option v-for="f in customFields" :key="f.id" :label="f.fieldName" :value="f.id" />
        </el-select>
        <el-select
          v-if="filterField && filterField.fieldType === 'text'"
          v-model="filterMatch"
          class="toolbar-match"
        >
          <el-option value="exact" label="精确匹配" />
          <el-option value="contains" label="包含" />
        </el-select>
        <el-select
          v-if="filterField && filterField.fieldType === 'single'"
          v-model="filterValue"
          clearable
          filterable
          placeholder="选项值"
          class="toolbar-value"
          @keyup.enter="onSearch"
        >
          <el-option v-for="o in (filterField.options || [])" :key="o" :label="o" :value="o" />
        </el-select>
        <el-input
          v-else-if="filterField"
          v-model="filterValue"
          clearable
          :placeholder="filterField.fieldType === 'number' ? '数字（按原文精确）' : '筛选值'"
          class="toolbar-value"
          @keyup.enter="onSearch"
        />
      </template>
      <el-button @click="onSearch">查询</el-button>
      <el-button @click="onReset">重置</el-button>
      <el-button v-if="isAdmin" type="primary" @click="openCreate">+ 创建工单</el-button>
      <el-button v-if="isAdmin && canPrint" @click="printSettingDlg = true">打印设置</el-button>
    </div>

    <el-tabs v-model="statusTab" class="status-tabs" @tab-change="onStatusChange">
      <el-tab-pane :label="`全部 ${counts.all}`" name="" />
      <el-tab-pane :label="`未开始 ${counts.notStarted}`" name="0" />
      <el-tab-pane :label="`执行中 ${counts.doing}`" name="1" />
      <el-tab-pane :label="`已结束 ${counts.done}`" name="2" />
      <el-tab-pane :label="`已取消 ${counts.cancelled}`" name="3" />
    </el-tabs>

    <!-- 桌面：表格；手机：卡片，避免窄屏挤成碎字 -->
    <el-table class="pc-table" :data="list">
      <el-table-column prop="orderNo" label="工单编号" min-width="168" class-name="col-order-no" />
      <el-table-column prop="productName" label="产品" min-width="120" />
      <el-table-column prop="qty" label="数量" width="88" />
      <el-table-column label="进度" width="120">
        <template #default="{ row }">{{ row.doneQty ?? 0 }}/{{ row.planQty ?? row.qty }}</template>
      </el-table-column>
      <el-table-column label="剩余" width="80">
        <template #default="{ row }">{{ row.remainQty ?? 0 }}</template>
      </el-table-column>
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="交期" width="180">
        <template #default="{ row }">
          <span :class="dueStateClass(row)">{{ formatDue(row.dueDate) }}</span>
        </template>
      </el-table-column>
      <el-table-column
        v-for="f in listColumns"
        :key="'cf-' + f.id"
        :label="f.fieldName"
        min-width="120"
        show-overflow-tooltip
      >
        <template #default="{ row }">{{ extCell(row, f.id) }}</template>
      </el-table-column>
      <el-table-column prop="createdAt" label="创建时间" width="250" />
      <el-table-column label="操作" min-width="460" fixed="right">
        <template #default="{ row }">
          <div class="op-btns">
          <el-button link type="primary" @click="openDetail(row)">详情</el-button>
          <el-button v-if="isAdmin && canEdit(row)" link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button v-if="canReport(row)" link type="success" @click="goReport(row)">报工</el-button>
          <template v-if="isAdmin">
            <el-button v-for="a in actions(row)" :key="a.key" link :type="a.type" @click="onTransition(row, a.key)">{{ a.label }}</el-button>
          </template>
          <el-button v-if="isAdmin" link type="primary" @click="onCopy(row)">复制</el-button>
          <el-button v-if="canPrint" link type="primary" @click="onPrint(row)">打印</el-button>
          <el-button v-if="isAdmin" link type="danger" @click="onDelete(row)">删除</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>

    <div class="m-list">
      <div v-if="list.length === 0" class="m-empty">暂无工单</div>
      <div v-for="row in list" :key="row.id" class="m-card">
        <div class="m-top">
          <el-tag size="small" :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag>
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
          <el-button v-if="isAdmin && canEdit(row)" size="small" type="primary" plain @click="openEdit(row)">编辑</el-button>
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

    <!-- 创建/编辑 -->
    <el-dialog v-model="dlg" :title="editingId ? '编辑工单' : '创建工单'" width="620px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="工单号">
          <el-input v-model="form.orderNo" placeholder="留空自动生成" />
        </el-form-item>
        <el-form-item label="产品" required>
          <el-select v-model="form.productId" filterable style="width:100%" @change="onProductChange">
            <el-option v-for="p in productOptions" :key="p.id" :label="p.code + ' ' + p.name" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="数量" required>
          <el-input-number v-model="form.qty" :min="1" />
        </el-form-item>
        <el-form-item label="计划交期">
          <el-date-picker v-model="form.dueDate" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" placeholder="留空则未设交期" style="width:100%" />
        </el-form-item>
        <el-form-item v-if="form.tasks.length" label="工序计划">
          <div v-for="t in form.tasks" :key="t.operationId" class="plan-row">
            <span class="plan-name">{{ t.operationName }}</span>
            <el-input-number v-model="t.planQty" :min="t.doneQty" size="small" />
          </div>
          <div class="plan-hint">计划数不可小于该工序已报良品数</div>
        </el-form-item>
        <el-form-item v-for="f in customFields" :key="f.id" :label="f.fieldName">
          <el-select v-if="f.fieldType === 'single'" v-model="form.ext[f.id]" clearable style="width:100%">
            <el-option v-for="o in f.options" :key="o" :label="o" :value="o" />
          </el-select>
          <el-input v-else v-model="form.ext[f.id]" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg = false">取消</el-button>
        <el-button type="primary" @click="onSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 详情 -->
    <el-dialog v-model="detailDlg" title="工单详情" width="620px">
      <el-descriptions :column="2">
        <el-descriptions-item label="工单号">{{ detail.orderNo }}</el-descriptions-item>
        <el-descriptions-item label="产品">{{ detail.productName }}</el-descriptions-item>
        <el-descriptions-item label="数量">{{ detail.qty }}</el-descriptions-item>
        <el-descriptions-item label="状态">{{ statusLabel(detail.status) }}</el-descriptions-item>
        <el-descriptions-item label="计划交期">
          <span :class="dueStateClass(detail)">{{ formatDue(detail.dueDate) }}</span>
        </el-descriptions-item>
      </el-descriptions>
      <el-table :data="detail.tasks" class="detail-tasks">
        <el-table-column prop="seq" label="序" width="50" />
        <el-table-column prop="operationName" label="工序" />
        <el-table-column prop="planQty" label="计划" width="80" />
        <el-table-column prop="doneQty" label="已报良品" width="100" />
        <el-table-column v-if="canAssign" label="执行人" min-width="140">
          <template #default="{ row }">
            <el-select
              v-if="row.id"
              :model-value="row.assigneeUserId ?? ''"
              clearable
              filterable
              placeholder="未派工"
              size="small"
              style="width:100%"
              @change="(v) => onAssignChange(row, v)"
            >
              <el-option v-for="u in workerOptions" :key="u.id" :label="u.name" :value="u.id" />
            </el-select>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column label="进度" min-width="120">
          <template #default="{ row }">
            <el-progress :percentage="row.planQty ? Math.min(100, Math.round(row.doneQty / row.planQty * 100)) : 0" />
          </template>
        </el-table-column>
      </el-table>
      <div class="kf-block">
        <div class="kf-title">图纸 / 作业指导书</div>
        <el-table v-if="guides.length" :data="guides" size="small">
          <el-table-column prop="fileName" label="文件名" min-width="160" show-overflow-tooltip />
          <el-table-column prop="fileType" label="类型" width="70" />
          <el-table-column prop="version" label="版本" width="80">
            <template #default="{ row }">{{ row.version || '—' }}</template>
          </el-table-column>
          <el-table-column label="来源" width="100">
            <template #default="{ row }">{{ row.refType === 'operation' ? (row.refName || '工序') : '产品' }}</template>
          </el-table-column>
          <el-table-column label="操作" width="80">
            <template #default="{ row }">
              <el-button link type="primary" @click="openGuide(row)">预览</el-button>
            </template>
          </el-table-column>
        </el-table>
        <div v-else class="kf-empty">暂无</div>
      </div>
      <el-image-viewer
        v-if="imgPreviewVisible"
        :url-list="imgPreviewUrls"
        @close="closeImgPreview"
      />
    </el-dialog>

    <el-dialog v-model="docPreviewVisible" :title="docPreviewFile?.fileName || '预览'"
      width="90%" top="4vh" destroy-on-close append-to-body @closed="docPreviewFile = null">
      <div style="height: 72vh">
        <DocPreview v-if="docPreviewFile" :blob="docPreviewFile.blob"
          :file-type="docPreviewFile.fileType" :file-name="docPreviewFile.fileName" />
      </div>
    </el-dialog>

    <!-- 打印设置 / 流转卡 -->
    <PrintSettingDialog v-model="printSettingDlg" />
    <PrintLabelDialog v-model="printLabelDlg" :order-id="printOrderId" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { getOrderList, getOrder, getOrderStatusCounts, createOrder, updateOrder, transitionOrder, copyOrder, deleteOrder, getCustomFields } from '@/api/prod'
import { getProductList } from '@/api/base'
import { assign } from '@/api/assign'
import { getUserList } from '@/api/sys'
import { getKnowledgeFileByOrder, getKnowledgeFileRaw } from '@/api/knowledge'
import { ElMessage, ElMessageBox } from 'element-plus'
import PrintSettingDialog from './PrintSettingDialog.vue'
import PrintLabelDialog from './PrintLabelDialog.vue'
import ValueTip from '@/components/ValueTip.vue'
import DocPreview from '@/components/DocPreview.vue'
import { getKnowledgeFileType } from '@/utils/knowledgeFile'
import { VALUE_TIPS } from '@/constants/valueTips'
import { canUse, FEATURE, currentTier } from '@/utils/licenseTier'

const router = useRouter()
const route = useRoute()
const role = Number(localStorage.getItem('role') || 0)
const isAdmin = role === 1
const canAssign = role === 1 || role === 3
const canPrint = canUse(currentTier(), FEATURE.PrintLabel)
const list = ref([]), keyword = ref(''), statusTab = ref('')
const counts = ref({ all: 0, notStarted: 0, doing: 0, done: 0, cancelled: 0 })
const dlg = ref(false), editingId = ref(null), detailDlg = ref(false)
const printSettingDlg = ref(false), printLabelDlg = ref(false), printOrderId = ref(null)
const form = ref({ orderNo: '', productId: null, qty: 1, dueDate: null, ext: {}, tasks: [] })
const detail = ref({ orderNo: '', productName: '', qty: 0, status: 0, tasks: [] })
const guides = ref([])
const imgPreviewVisible = ref(false)
const imgPreviewUrls = ref([])
const docPreviewVisible = ref(false)
const docPreviewFile = ref(null)
const productOptions = ref([]), customFields = ref([]), workerOptions = ref([])
const filterFieldId = ref(null), filterMatch = ref('exact'), filterValue = ref('')

const listColumns = computed(() =>
  (customFields.value || [])
    .filter(f => f.showInOrderList)
    .slice()
    .sort((a, b) => a.id - b.id)
)

const filterField = computed(() =>
  (customFields.value || []).find(f => f.id === filterFieldId.value) || null
)

const STATUS = { 0: '未开始', 1: '执行中', 2: '已结束', 3: '已取消' }
function statusLabel(s) { return STATUS[s] ?? s }
function statusTag(s) { return s === 0 ? 'info' : s === 1 ? 'warning' : s === 2 ? 'success' : 'danger' }
function formatDue(t) { return t ? String(t).replace('T', ' ').slice(0, 16) : '—' }
function dueStateClass(row) { return row.dueState === 'overdue' ? 'due-overdue' : row.dueState === 'warning' ? 'due-warning' : '' }
function canEdit(row) { return row.status === 0 || row.status === 1 }
function canReport(row) { return row.status === 0 || row.status === 1 }
function goReport(row) { router.push({ path: '/report', query: { order: row.orderNo } }) }
function extCell(row, fieldId) {
  const ext = row.ext || {}
  const v = ext[fieldId] ?? ext[String(fieldId)]
  return v == null || v === '' ? '—' : v
}

function actions(row) {
  const a = []
  if (row.status === 0) a.push({ key: 'start', label: '开始', type: 'success' })
  if (row.status === 1) a.push({ key: 'finish', label: '结束', type: 'success' })
  if (row.status === 1) a.push({ key: 'withdraw', label: '撤回', type: 'warning' })
  if (row.status === 2) a.push({ key: 'withdraw', label: '撤回', type: 'warning' })
  if (row.status === 0 || row.status === 1) a.push({ key: 'cancel', label: '取消', type: 'danger' })
  if (row.status === 3) a.push({ key: 'restore', label: '恢复', type: 'primary' })
  return a
}

onMounted(async () => {
  await loadFields()
  await load()
  if (canAssign) await loadWorkers()
  const openId = route.query.openId
  if (openId) {
    try {
      await openDetail({ id: Number(openId) })
    } catch (_) {
      ElMessage.error('无法打开指定工单，请从列表重新进入')
    }
  }
  if (!isAdmin) return
  const products = await getProductList({ page: 1, pageSize: 200 })
  productOptions.value = products.data.list || []
})

async function loadWorkers() {
  const res = await getUserList({ page: 1, pageSize: 200 })
  workerOptions.value = (res.data.list || []).filter(u => u.role !== 1)
}

async function loadFields() {
  const fields = await getCustomFields('work_order')
  customFields.value = fields.data || []
  // 已删除的筛选字段：清理条件，避免反复提交失效 ID
  if (filterFieldId.value && !customFields.value.some(f => f.id === filterFieldId.value)) {
    filterFieldId.value = null
    filterValue.value = ''
    filterMatch.value = 'exact'
  }
}

function onFilterFieldChange() {
  filterValue.value = ''
  filterMatch.value = 'exact'
}

async function loadCounts() {
  const res = await getOrderStatusCounts()
  const d = res.data || {}
  counts.value = {
    all: d.all || 0,
    notStarted: d.notStarted || 0,
    doing: d.doing || 0,
    done: d.done || 0,
    cancelled: d.cancelled || 0
  }
}

async function load() {
  await loadFields()
  await loadCounts()
  const params = {
    keyword: keyword.value,
    page: 1,
    pageSize: 100
  }
  if (statusTab.value === '') {
    // 「全部」与数量一致：不含已取消
    params.excludeCancelled = true
  } else {
    params.status = statusTab.value
  }
  if (filterFieldId.value) {
    params.customFieldId = filterFieldId.value
    const f = filterField.value
    if (f && (f.fieldType === 'single' || f.fieldType === 'number')) {
      params.customFieldMatch = 'exact'
    } else {
      params.customFieldMatch = filterMatch.value || 'exact'
    }
    if (filterValue.value != null && String(filterValue.value).trim() !== '') {
      params.customFieldValue = String(filterValue.value).trim()
    }
  }
  const res = await getOrderList(params)
  list.value = res.data.list || []
}

function onSearch() { load() }
function onStatusChange() { load() }

function onReset() {
  keyword.value = ''
  filterFieldId.value = null
  filterMatch.value = 'exact'
  filterValue.value = ''
  statusTab.value = ''
  load()
}

function openCreate() {
  editingId.value = null
  form.value = { orderNo: '', productId: null, qty: 1, dueDate: null, ext: {}, tasks: [] }
  dlg.value = true
}

async function openEdit(row) {
  const res = await getOrder(row.id)
  const d = res.data
  editingId.value = row.id
  form.value = {
    orderNo: d.orderNo,
    productId: d.productId,
    qty: d.qty,
    dueDate: d.dueDate || null,
    ext: { ...d.ext },
    tasks: (d.tasks || []).map(t => ({ operationId: t.operationId, operationName: t.operationName, planQty: t.planQty, doneQty: t.doneQty }))
  }
  dlg.value = true
}

function onProductChange() {
  // 换产品后计划数由后端按新路线重建，前端清空旧计划
  if (editingId.value) form.value.tasks = []
}

async function onSave() {
  if (!form.value.productId || form.value.qty <= 0) {
    ElMessage.error('请选择产品并填写数量')
    return
  }
  if (editingId.value) {
    const payload = {
      orderNo: form.value.orderNo || undefined,
      productId: form.value.productId,
      qty: form.value.qty,
      dueDate: form.value.dueDate || null,
      ext: form.value.ext,
      planQty: form.value.tasks.length ? form.value.tasks.map(t => ({ operationId: t.operationId, planQty: t.planQty })) : undefined
    }
    await updateOrder(editingId.value, payload)
    ElMessage.success('已保存')
  } else {
    await createOrder({ orderNo: form.value.orderNo || undefined, productId: form.value.productId, qty: form.value.qty, dueDate: form.value.dueDate || null, ext: form.value.ext })
    ElMessage.success('创建成功')
  }
  dlg.value = false
  await load()
}

async function onTransition(row, action) {
  const labelMap = { start: '开始', finish: '结束', withdraw: '撤回', cancel: '取消', restore: '恢复' }
  await ElMessageBox.confirm(`确认${labelMap[action]}工单 ${row.orderNo}？`, '提示', { type: 'warning' })
  await transitionOrder(row.id, action)
  ElMessage.success('操作成功')
  await load()
}

async function onCopy(row) {
  await ElMessageBox.confirm(`确认复制工单 ${row.orderNo}（生成一张同产品同数量的未开始工单）？`, '提示', { type: 'info' })
  await copyOrder(row.id)
  ElMessage.success('复制成功')
  await load()
}

async function openDetail(row) {
  const res = await getOrder(row.id)
  detail.value = res.data
  guides.value = []
  detailDlg.value = true
  try {
    const g = await getKnowledgeFileByOrder(row.id)
    guides.value = g.data || []
  } catch {
    guides.value = []
    ElMessage.error('加载图纸 / 作业指导书失败')
  }
}

async function openGuide(file) {
  try {
    const res = await getKnowledgeFileRaw(file.id)
    const blob = res.data
    const fileType = getKnowledgeFileType(file)
    if (['jpg', 'jpeg', 'png', 'webp'].includes(fileType)) {
      const url = URL.createObjectURL(blob)
      imgPreviewUrls.value = [url]
      imgPreviewVisible.value = true
    } else if (fileType === 'pdf' || fileType === 'docx') {
      docPreviewFile.value = { blob, fileType, fileName: file.fileName }
      docPreviewVisible.value = true
    } else {
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = file.fileName || 'file'
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    }
  } catch {
    ElMessage.error('预览失败，请稍后重试')
  }
}

function closeImgPreview() {
  imgPreviewVisible.value = false
  for (const u of imgPreviewUrls.value) URL.revokeObjectURL(u)
  imgPreviewUrls.value = []
}

async function onAssignChange(taskRow, v) {
  const userId = v === '' || v == null ? null : v
  await assign(taskRow.id, userId)
  ElMessage.success(userId ? '已派工' : '已取消派工')
  const res = await getOrder(detail.value.id)
  detail.value = res.data
}

function onPrint(row) {
  printOrderId.value = row.id
  printLabelDlg.value = true
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除工单 ${row.orderNo}？将同时删除其报工记录`, '提示', { type: 'warning' })
  await deleteOrder(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<style scoped>
.toolbar { display: flex; align-items: center; gap: 12px; margin-bottom: var(--app-section-gap); flex-wrap: wrap; }
.toolbar-search { width: 220px; }
.toolbar-field { width: 160px; }
.toolbar-match { width: 120px; }
.toolbar-value { width: 180px; }
.status-tabs :deep(.el-tabs__item) { font-size: 14px; }
.status-tabs :deep(.el-tabs__item.is-active) { font-weight: 600; }
.status-tabs :deep(.el-tabs__header) { margin-bottom: var(--app-section-gap); }
.status-tabs :deep(.el-tabs__content) { display: none; }
:deep(td.col-order-no) { font-weight: 600; }
:deep(td.col-order-no .cell) { white-space: nowrap; }
.op-btns { display: flex; flex-wrap: nowrap; align-items: center; gap: 2px 10px; }
.op-btns :deep(.el-button) { margin-left: 0; }
.due-warning { color: var(--app-warning); }
.due-overdue { color: var(--app-danger); font-weight: 600; }
.plan-row { display: flex; gap: 8px; align-items: center; margin-bottom: 6px; }
.plan-name { width: 120px; }
.plan-hint { color: var(--app-text-3); font-size: 12px; }
.detail-tasks { margin-top: var(--app-section-gap); }
.kf-block { margin-top: var(--app-section-gap); }
.kf-title { font-weight: 600; margin-bottom: 8px; color: #303133; }
.kf-empty { color: var(--app-text-3, #909399); font-size: 13px; padding: 4px 0; }

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
