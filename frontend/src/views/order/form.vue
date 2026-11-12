<template>
  <div v-loading="booting" class="order-bill">
    <div class="order-edit-header sticky-toolbar">
      <div class="order-edit-toolbar" role="toolbar" :aria-label="pageTitle">
        <template v-if="browseOnly">
          <el-button
            v-if="isAdmin && canEdit({ status: editingStatus }) && canEditCreator"
            type="primary"
            @click="switchToEdit"
          >编辑</el-button>
        </template>
        <template v-else-if="!editingId || editingStatus === 4 || editingStatus === 5">
          <el-button :loading="saving" :disabled="requoting" @click="onSave('draft')">暂存</el-button>
          <el-button type="primary" :loading="saving" :disabled="requoting" @click="onSave('pending')">保存</el-button>
          <el-button v-if="editingId" :loading="saving" :disabled="requoting" @click="onSave('approve')">审核</el-button>
          <el-button
            :loading="requoting"
            :disabled="saving || !form.productId"
            @click="onRequote"
          >重新取价</el-button>
        </template>
        <el-button :disabled="saving || requoting" @click="closeEdit">退出</el-button>
      </div>
      <span class="order-edit-title">{{ pageTitle }}</span>
    </div>
    <el-alert
      v-if="browseOnly"
      class="browse-notice"
      :title="browseNoticeTitle"
      type="info"
      show-icon
      :closable="false"
      style="margin-bottom:12px"
    />
    <el-alert v-if="copyFromId" title="请核对数量并填写本次交期。工序计划会按当前产品工艺重新生成，之前的报工记录和派工信息不会带过来。" type="info" :closable="false" style="margin-bottom:12px" />
    <el-form class="order-edit-form" :disabled="saving || browseOnly" :model="form" label-width="90px">
      <div class="order-form-grid">
        <el-form-item label="工单号">
          <el-input v-model="form.orderNo" placeholder="留空则系统自动生成工单号" />
        </el-form-item>
        <el-form-item v-if="editingId" label="状态">
          <span>{{ statusLabel(editingStatus) }}</span>
          <el-tag v-if="detail.reopened && editingStatus === 1" size="small" type="info" style="margin-left:6px">已重新打开</el-tag>
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
          <el-date-picker v-model="form.dueDate" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" placeholder="选择交期，可留空" style="width:100%" />
        </el-form-item>
        <el-form-item v-for="f in customFields" :key="f.id" :label="f.fieldName">
          <el-select v-if="f.fieldType === 'single'" v-model="form.ext[f.id]" clearable style="width:100%">
            <el-option v-for="o in f.options" :key="o" :label="o" :value="o" />
          </el-select>
          <el-input v-else v-model="form.ext[f.id]" />
        </el-form-item>
      </div>
      <el-form-item v-if="form.tasks.length" label="工序计划" class="order-form-full">
        <div v-for="t in form.tasks" :key="t.operationId" class="plan-row">
          <span class="plan-name">{{ t.operationName }}</span>
          <el-input-number v-model="t.planQty" :min="t.doneQty" size="small" />
        </div>
        <div class="plan-hint">计划数量不能小于该工序已经报工的良品数；如需调低计划数，先到「报工」核实已报数量</div>
      </el-form-item>
      <el-collapse v-model="openSections" class="order-section-collapse">
        <el-collapse-item title="本单工序工价" name="pricing">
          <WorkOrderProcessing
            v-if="ready"
            ref="processingEditor"
            hide-title
            :product-id="form.productId"
            :tasks="processingTasks"
            :copy="!!copyFromId"
            :disabled="saving || browseOnly"
            :approve-mode="!editingId || editingStatus === 4 || editingStatus === 5"
          />
        </el-collapse-item>
      </el-collapse>
    </el-form>
    <template v-if="editingId && detail?.tasks">
      <el-collapse v-model="openSections" class="order-section-collapse order-assign-anchor">
        <el-collapse-item :title="canAssign ? '工序进度与派工' : '工序进度'" name="assign">
          <mes-table table-key="views/order/form:1" :data="detail.tasks" class="detail-tasks">
            <el-table-column prop="seq" label="序" width="50" />
            <el-table-column prop="operationName" label="工序" />
            <el-table-column prop="planQty" label="计划" width="80" />
            <el-table-column prop="doneQty" label="已报良品" width="100" />
            <el-table-column v-if="canAssign" label="派工" min-width="260">
              <template #default="{ row }">
                <div v-if="row.id" class="assign-cell">
                  <div v-if="suggestMap[row.id]?.length" class="assign-suggest">
                    <div class="assign-suggest-title">推荐派给谁</div>
                    <div class="assign-suggest-list">
                      <button
                        v-for="(s, i) in suggestMap[row.id]"
                        :key="s.userId"
                        type="button"
                        class="assign-suggest-item"
                        :class="{ top: i === 0 }"
                        @click="onPickSuggest(row, s.userId)"
                      >
                        <span class="name">{{ s.name }}<template v-if="isSuggestedPicked(row, s.userId)"> · 已选</template></span>
                        <span class="meta">近30天 {{ s.reportCount }} 次 · 良品 {{ s.goodQty }}</span>
                      </button>
                    </div>
                  </div>
                  <el-select
                    :model-value="row.assigneeDepartmentId || null"
                    clearable
                    filterable
                    placeholder="选择负责该工序的班组（可不填）"
                    size="small"
                    style="width:100%"
                    @visible-change="(open) => open && loadDeptOptions(row)"
                    @change="(v) => onAssignDeptChange(row, v)"
                  >
                    <el-option
                      v-if="row.assigneeDepartmentId"
                      label="不派班组（取消）"
                      :value="0"
                    />
                    <el-option
                      v-for="d in (deptOptionsMap[row.id] || (row.assigneeDepartmentId ? [{ id: row.assigneeDepartmentId, name: row.assigneeDepartmentName || '班组' }] : []))"
                      :key="d.id"
                      :label="d.name"
                      :value="d.id"
                    />
                  </el-select>
                  <el-select
                    :model-value="(row.assignees || []).map(a => a.userId)"
                    multiple
                    clearable
                    filterable
                    collapse-tags
                    collapse-tags-tooltip
                    :disabled="!!row.assigneeDepartmentId"
                    placeholder="选择执行人（可多选，不填则由部门内任何人报工）"
                    size="small"
                    style="width:100%;margin-top:4px"
                    @change="(v) => onAssignChange(row, v)"
                  >
                    <el-option v-for="u in workerOptions" :key="u.id" :label="u.name" :value="u.id" />
                  </el-select>
                  <el-button
                    v-if="row.assigneeDepartmentId || (row.assignees || []).length"
                    link
                    type="primary"
                    size="small"
                    class="assign-clear-btn"
                    @click="onClearAssign(row)"
                  >取消派工</el-button>
                </div>
                <span v-else>—</span>
              </template>
            </el-table-column>
            <el-table-column label="进度" min-width="120">
              <template #default="{ row }">
                <el-progress :percentage="row.planQty ? Math.min(100, Math.round(row.doneQty / row.planQty * 100)) : 0" />
              </template>
            </el-table-column>
          </mes-table>
        </el-collapse-item>
      </el-collapse>
      <div class="kf-block">
        <div class="kf-title">图纸 / 作业指导书</div>
        <mes-table table-key="views/order/form:2" v-if="guides.length" :data="guides" size="small">
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
        </mes-table>
        <div v-else class="kf-empty">暂无</div>
      </div>
      <div class="order-sys-meta">
        创建人：{{ detail.createdByName || '—' }}
        · 创建时间：{{ formatDue(detail.createdAt) }}
        · 审核人：{{ detail.auditedByName || '—' }}
        · 审核时间：{{ detail.auditedAt ? formatDue(detail.auditedAt) : '—' }}
        · 修改人：{{ detail.updatedByName || '—' }}
        · 修改时间：{{ detail.updatedAt ? formatDue(detail.updatedAt) : '—' }}
      </div>
    </template>
    <el-image-viewer
      v-if="imgPreviewVisible"
      :url-list="imgPreviewUrls"
      @close="closeImgPreview"
    />
    <el-dialog v-model="docPreviewVisible" :title="docPreviewFile?.fileName || '预览'"
      width="90%" top="4vh" destroy-on-close append-to-body @closed="docPreviewFile = null">
      <div style="height: 72vh">
        <DocPreview v-if="docPreviewFile" :blob="docPreviewFile.blob"
          :file-type="docPreviewFile.fileType" :file-name="docPreviewFile.fileName" />
      </div>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, watch, nextTick } from 'vue'
import { useRouter, useRoute, onBeforeRouteLeave } from 'vue-router'
import WorkOrderProcessing from '@/components/WorkOrderProcessing.vue'
import { getOrder, createOrder, updateOrder, getCustomFields, getDocumentPermission } from '@/api/prod'
import { getProductList } from '@/api/base'
import { assign, assignWorkers, assignDepartments, assignSuggest } from '@/api/assign'
import { getKnowledgeFileByOrder, getKnowledgeFileRaw } from '@/api/knowledge'
import { ElMessage } from 'element-plus'
import DocPreview from '@/components/DocPreview.vue'
import { getKnowledgeFileType } from '@/utils/knowledgeFile'
import { setPcTabTitle, closePcTab } from '@/composables/usePcTabs'
import { canEditByCreator } from '@/utils/documentPermission'

const router = useRouter()
const route = useRoute()
const processingEditor = ref(null), processingTasks = ref([])
const role = Number(localStorage.getItem('role') || 0)
const isAdmin = role === 1
const canAssign = role === 1 || role === 3
const onlyCreatorEditWorkOrder = ref(false)
const editingStatus = ref(null)
const editingId = ref(null)
const openSections = ref(['pricing'])
const browseOnly = ref(false)
const copyFromId = ref(null), saving = ref(false), requoting = ref(false)
const ready = ref(false)
const booting = ref(true)
const skipLeaveGuard = ref(false)

const STATUS = { 0: '已审核', 1: '报工中', 2: '已结束', 3: '已作废', 4: '草稿', 5: '已保存', 6: '已挂起' }
function statusLabel(s) { return STATUS[s] ?? s }
function formatDue(t) { return t ? String(t).replace('T', ' ').slice(0, 16) : '—' }
function canEdit(row) {
  const s = Number(row?.status)
  return s === 4 || s === 5
}
const canEditCreator = computed(() =>
  canEditByCreator(onlyCreatorEditWorkOrder.value, detail.value?.createdBy ?? form.value?.createdBy, role)
)
function setOpenSections(mode) {
  if (mode === 'assign') openSections.value = ['assign']
  else openSections.value = ['pricing']
}

const pageTitle = computed(() => {
  const no = String(form.value?.orderNo || detail.value?.orderNo || '').trim()
  return no || '工单'
})
const browseNoticeTitle = computed(() => {
  if (canEdit({ status: editingStatus.value }) && !canEditCreator.value) {
    return '这张工单不是你创建的，不能改。请联系创建人或管理员处理。'
  }
  if (canEdit({ status: editingStatus.value })) {
    return '当前是浏览。要改内容点左上角「编辑」'
  }
  if (editingStatus.value === 0 || editingStatus.value === 1) {
    return '已审核工单不能直接改。无报工：列表点「反审核」退回已保存再改；有报工：点「复制」开新单'
  }
  return '当前是浏览，这张工单不能编辑'
})

const form = ref({ orderNo: '', productId: null, qty: 1, dueDate: null, ext: {}, tasks: [] })
const detail = ref({ orderNo: '', productName: '', qty: 0, status: 0, tasks: [] })
const guides = ref([])
const imgPreviewVisible = ref(false)
const imgPreviewUrls = ref([])
const docPreviewVisible = ref(false)
const docPreviewFile = ref(null)
const productOptions = ref([]), customFields = ref([]), workerOptions = ref([])
const deptOptionsMap = ref({})
const suggestMap = ref({})

function refreshTabTitle() {
  setPcTabTitle(route.path, pageTitle.value)
}

function leaveToList() {
  skipLeaveGuard.value = true
  const next = closePcTab(route.path, route.path)
  router.push(next || '/order')
}

function closeEdit() {
  if (saving.value || requoting.value) return
  leaveToList()
}

function switchToEdit() {
  if (!canEdit({ status: editingStatus.value })) {
    ElMessage.warning(`当前状态「${statusLabel(editingStatus.value)}」不能编辑，只能浏览`)
    return
  }
  if (!canEditCreator.value) {
    ElMessage.warning('这张工单不是你创建的，不能改。请联系创建人或管理员处理。')
    return
  }
  browseOnly.value = false
  router.replace({ path: route.path, query: { ...route.query, mode: 'edit' } })
}

onBeforeRouteLeave(() => {
  if (skipLeaveGuard.value) return true
  if (saving.value || requoting.value) return false
  return true
})

async function loadWorkers() {
  try {
    const res = await assignWorkers()
    workerOptions.value = res.data || []
  } catch {
    workerOptions.value = []
  }
}

async function loadDeptOptions(row) {
  if (!row?.id) return
  try {
    const res = await assignDepartments(row.id)
    deptOptionsMap.value = { ...deptOptionsMap.value, [row.id]: res.data || [] }
  } catch {
    deptOptionsMap.value = { ...deptOptionsMap.value, [row.id]: [] }
  }
}

async function loadSuggests(tasks) {
  const next = {}
  for (const t of (tasks || []).filter(x => x.id)) {
    const res = await assignSuggest(t.id)
    const list = res.data || []
    if (list.length) next[t.id] = list
  }
  suggestMap.value = next
}

async function loadBill() {
  booting.value = true
  ready.value = false
  try {
    const isNew = route.name === 'OrderNew'
    const wantBrowse = route.query.mode === 'browse'
    const focusAssign = route.query.focus === 'assign'
    if (isNew) {
      editingId.value = null
      editingStatus.value = null
      browseOnly.value = false
      detail.value = { orderNo: '', productName: '', qty: 0, status: 0, tasks: [] }
      guides.value = []
      suggestMap.value = {}
      const copyId = Number(route.query.copyFrom)
      if (Number.isFinite(copyId) && copyId > 0) {
        const res = await getOrder(copyId)
        const d = res.data
        processingTasks.value = d.tasks || []
        copyFromId.value = copyId
        form.value = {
          orderNo: '',
          productId: d.productId,
          qty: d.qty,
          dueDate: null,
          ext: Object.fromEntries(customFields.value.filter(f => Object.hasOwn(d.ext || {}, f.id)).map(f => [f.id, d.ext[f.id]])),
          tasks: []
        }
        setOpenSections('create')
      } else {
        processingTasks.value = []
        copyFromId.value = null
        form.value = { orderNo: '', productId: null, qty: 1, dueDate: null, ext: {}, tasks: [] }
        setOpenSections('create')
      }
      ready.value = true
      refreshTabTitle()
      return
    }
    const id = Number(route.params.id)
    if (!Number.isFinite(id) || id <= 0) {
      ElMessage.error('工单链接无效，请从列表重新打开')
      leaveToList()
      return
    }
    const res = await getOrder(id)
    const d = res.data
    detail.value = d
    processingTasks.value = d.tasks || []
    editingId.value = id
    editingStatus.value = d.status
    copyFromId.value = null
    const editable = canEdit({ status: d.status })
      && canEditByCreator(onlyCreatorEditWorkOrder.value, d.createdBy, role)
    browseOnly.value = wantBrowse || !editable
    form.value = {
      orderNo: d.orderNo,
      productId: d.productId,
      qty: d.qty,
      dueDate: d.dueDate || null,
      ext: { ...d.ext },
      tasks: (d.tasks || []).map(t => ({ operationId: t.operationId, operationName: t.operationName, planQty: t.planQty, doneQty: t.doneQty }))
    }
    guides.value = []
    suggestMap.value = {}
    setOpenSections(focusAssign ? 'assign' : browseOnly.value ? 'browse' : 'edit')
    ready.value = true
    refreshTabTitle()
    try {
      const g = await getKnowledgeFileByOrder(id)
      guides.value = g.data || []
    } catch {
      guides.value = []
      ElMessage.error('图纸或作业指导书加载失败，请检查文件是否已被删除或移动，也可到「产品定义」重新上传附件；若仍失败，联系管理员处理')
    }
    if (canAssign) await loadSuggests(detail.value.tasks)
    if (focusAssign) {
      await nextTick()
      document.querySelector('.order-assign-anchor')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  } finally {
    booting.value = false
  }
}

function onProductChange() {
  processingTasks.value = []
  if (editingId.value) form.value.tasks = []
}

async function onRequote() {
  if (requoting.value || saving.value) return
  if (!processingEditor.value?.requoteAll) return
  requoting.value = true
  try {
    await processingEditor.value.requoteAll()
  } finally {
    requoting.value = false
  }
}

/** @param {'draft'|'pending'|'approve'} saveAction */
async function onSave(saveAction = 'approve') {
  if (saving.value) return
  if (!form.value.productId || form.value.qty <= 0) {
    ElMessage.error('请先选择一个产品，并在数量栏填写大于零的数字后再保存')
    return
  }
  const needQuote = saveAction === 'approve'
  let quoteTokens
  saving.value = true
  try {
    if (needQuote && processingEditor.value?.ensureQuotes) await processingEditor.value.ensureQuotes()
    try { quoteTokens = processingEditor.value?.getQuoteTokens(needQuote) || {} } catch (e) { ElMessage.error(e.message); return }
    const asDraft = saveAction === 'draft'
    if (editingId.value) {
      const payload = {
        asDraft,
        saveAction,
        quoteTokens, operationAttributes: processingEditor.value?.getOperationAttributes(),
        orderNo: form.value.orderNo || undefined,
        productId: form.value.productId,
        qty: form.value.qty,
        dueDate: form.value.dueDate || null,
        ext: form.value.ext,
        planQty: form.value.tasks.length ? form.value.tasks.map(t => ({ operationId: t.operationId, planQty: t.planQty })) : undefined
      }
      await updateOrder(editingId.value, payload)
      const msg = saveAction === 'draft'
        ? '已暂存为草稿，可随时继续编辑'
        : saveAction === 'pending'
          ? '已保存；审核前工人看不到、也不能报工'
          : (editingStatus.value === 4 || editingStatus.value === 5
            ? '已审核锁价，工人可在报工页看到并开始报工'
            : '已保存')
      ElMessage.success(msg)
    } else {
      await createOrder({
        asDraft,
        saveAction,
        quoteTokens, operationAttributes: processingEditor.value?.getOperationAttributes(),
        copyFromId: copyFromId.value, orderNo: form.value.orderNo || undefined,
        productId: form.value.productId, qty: form.value.qty, dueDate: form.value.dueDate || null, ext: form.value.ext
      })
      ElMessage.success(
        saveAction === 'draft' ? '已暂存为草稿'
          : saveAction === 'pending' ? '已保存，待审核'
            : '已审核锁价'
      )
    }
    leaveToList()
  } finally { saving.value = false }
}

function isSuggestedPicked(taskRow, userId) {
  if (taskRow.assigneeDepartmentId) return false
  return (taskRow.assignees || []).some(a => a.userId === userId)
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
    ElMessage.error('预览没打开成功，等一下再点试试。')
  }
}

function closeImgPreview() {
  imgPreviewVisible.value = false
  for (const u of imgPreviewUrls.value) URL.revokeObjectURL(u)
  imgPreviewUrls.value = []
}

async function onPickSuggest(taskRow, userId) {
  await assign(taskRow.id, [userId])
  ElMessage.success('已派工')
  const res = await getOrder(detail.value.id)
  detail.value = res.data
}

async function onAssignChange(taskRow, v) {
  const ids = Array.isArray(v) ? v.filter(x => x != null && x !== '') : []
  await assign(taskRow.id, ids)
  ElMessage.success(ids.length ? '已派工' : '已取消派工')
  const res = await getOrder(detail.value.id)
  detail.value = res.data
}

async function onClearAssign(taskRow) {
  await assign(taskRow.id, [])
  ElMessage.success('已取消派工')
  const res = await getOrder(detail.value.id)
  detail.value = res.data
}

async function onAssignDeptChange(taskRow, deptId) {
  if (deptId) {
    await assign(taskRow.id, [], deptId)
    ElMessage.success('已派给班组')
  } else {
    await assign(taskRow.id, [])
    ElMessage.success('已取消派工')
  }
  const res = await getOrder(detail.value.id)
  detail.value = res.data
}

watch(() => form.value.orderNo, () => refreshTabTitle())
watch(
  () => ({ name: route.name, fullPath: route.fullPath }),
  (cur, prev) => {
    if (!prev) return
    if (cur.name !== 'OrderBill' && cur.name !== 'OrderNew') return
    if (cur.fullPath === prev.fullPath) return
    loadBill()
  }
)

onMounted(async () => {
  try {
    const perm = await getDocumentPermission()
    onlyCreatorEditWorkOrder.value = !!perm.data?.onlyCreatorEditWorkOrder
  } catch {
    onlyCreatorEditWorkOrder.value = false
  }
  const fields = await getCustomFields('work_order')
  customFields.value = fields.data || []
  const products = await getProductList({ page: 1, pageSize: 200 })
  productOptions.value = products.data.list || []
  if (canAssign) await loadWorkers()
  await loadBill()
})
</script>

<style scoped>
.order-bill { min-height: 200px; }
.order-edit-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-start;
  gap: 8px 16px;
  width: 100%;
  margin-bottom: 12px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.order-edit-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.order-edit-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--el-text-color-secondary);
  line-height: 32px;
  margin-left: auto;
}
.browse-notice { margin-bottom: 12px; }
.order-form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  column-gap: 16px;
}
.order-form-grid :deep(.el-form-item) { margin-bottom: 12px; }
.order-form-full { margin-top: 4px; }
.plan-row { display: flex; gap: 8px; align-items: center; margin-bottom: 6px; }
.plan-name { width: 120px; }
.plan-hint { color: var(--app-text-3); font-size: 12px; }
@media (min-width: 1000px) {
  .order-form-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
@media (max-width: 640px) {
  .order-form-grid { grid-template-columns: 1fr; }
}
.order-section-collapse { margin-top: var(--app-section-gap); }
.order-section-collapse :deep(.el-collapse-item__header) { font-weight: 600; font-size: 15px; }
.detail-tasks { margin-top: 0; }
.order-assign-anchor { scroll-margin-top: 12px; }
.assign-cell { display: flex; flex-direction: column; }
.assign-clear-btn { align-self: flex-start; margin-top: 2px; padding: 0; height: auto; }
.assign-suggest { margin-bottom: 6px; }
.assign-suggest-title { font-size: 12px; color: var(--app-text-3); margin-bottom: 4px; }
.assign-suggest-list { display: flex; flex-direction: column; gap: 4px; max-height: 132px; overflow: auto; }
.assign-suggest-item {
  text-align: left; border: 1px solid var(--app-border); background: var(--app-card);
  border-radius: 6px; padding: 4px 8px; cursor: pointer; font: inherit; color: inherit;
}
.assign-suggest-item.top { border-color: var(--app-primary); background: var(--app-primary-light); }
.assign-suggest-item .name { display: block; font-weight: 600; font-size: 13px; }
.assign-suggest-item.top .name { color: var(--app-primary); }
.assign-suggest-item .meta { display: block; font-size: 12px; color: var(--app-text-3); }
.kf-block { margin-top: var(--app-section-gap); }
.kf-title { font-weight: 600; margin-bottom: 8px; color: #303133; }
.kf-empty { color: var(--app-text-3, #909399); font-size: 13px; padding: 4px 0; }
.order-sys-meta {
  margin-top: 14px;
  padding-top: 10px;
  border-top: 1px solid var(--el-border-color-lighter, #ebeef5);
  font-size: 12px;
  color: var(--app-text-3, #909399);
  line-height: 1.6;
}
</style>
