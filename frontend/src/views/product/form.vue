<template>
  <div v-loading="booting" class="pc-bill-page product-bill">
    <div class="bill-top">
      <div class="product-edit-header sticky-toolbar">
        <div class="product-edit-toolbar" role="toolbar" :aria-label="pageTitle">
          <el-button
            type="primary"
            :loading="saving"
            :disabled="loadingRouting || detailLocked"
            @click="onSave"
          >保存</el-button>
          <el-button :disabled="saving" @click="closeEdit">退出</el-button>
        </div>
        <span class="product-edit-title">{{ pageTitle }}</span>
      </div>
      <el-alert
        v-if="copyFromId"
        class="browse-notice"
        title="请填写新编号；作业指导书、图纸和独立计价规则不会被复制，需要在新工序中重新上传或设置"
        type="info"
        :closable="false"
        show-icon
      />
      <el-alert
        v-if="detailLocked"
        class="browse-notice"
        title="已审核，表单已锁定。请先反审核再改。"
        type="warning"
        :closable="false"
        show-icon
      />
      <el-form
        :model="form"
        :disabled="saving || detailLocked"
        label-width="100px"
        class="product-edit-form"
      >
        <div class="product-form-grid">
          <el-form-item label="产品编号" required>
            <el-input v-model="form.code" />
          </el-form-item>
          <el-form-item label="产品名称" required>
            <el-input v-model="form.name" />
          </el-form-item>
          <el-form-item v-if="isAdmin" label="重点产品" class="wide">
            <el-checkbox v-model="form.isRiskFocus">标记为重点产品（风险简报优先展示）</el-checkbox>
          </el-form-item>
          <el-form-item label="单位">
            <el-select v-model="form.unitId" clearable placeholder="选择计量单位（如：件、把、打）" style="width:100%">
              <el-option v-for="u in unitOptions" :key="u.id" :label="u.name" :value="u.id" />
            </el-select>
          </el-form-item>
          <el-form-item label="工艺路线">
            <el-select
              v-model="form.routingId"
              :disabled="!!form.newRouting"
              clearable
              filterable
              placeholder="选择"
              style="width:100%"
            >
              <el-option v-for="r in routingOptions" :key="r.id" :label="r.name" :value="r.id" />
            </el-select>
          </el-form-item>
          <el-form-item v-if="!productId && form.routingId" label="调整工艺" class="wide">
            <el-button v-if="!form.newRouting" :loading="loadingRouting" @click="copyRouting">复制工艺并修改</el-button>
            <el-button v-else @click="form.newRouting = null">沿用原工艺</el-button>
          </el-form-item>
          <template v-if="form.newRouting">
            <el-form-item label="新路线编号" required>
              <el-input v-model="form.newRouting.code" />
            </el-form-item>
            <el-form-item label="新路线名称" required>
              <el-input v-model="form.newRouting.name" />
            </el-form-item>
            <el-form-item label="工序顺序" class="wide">
              <RoutingSteps v-model="form.newRouting.steps" :options="opOptions" />
            </el-form-item>
          </template>
          <el-form-item label="供应商">
            <el-input v-model="form.supplier" />
          </el-form-item>
          <el-form-item label="单价">
            <el-input-number v-model="form.price" :min="0" :precision="2" />
            <div class="field-note">参考单价，不计入计件工资；工人计件工价在「工序价目表」维护。</div>
          </el-form-item>
          <ProductAttributes v-model="form.customValues" :fields="attributeFields" />
        </div>
      </el-form>
      <div v-if="productId" class="kf-block">
        <div class="kf-title">作业指导书 / 图纸</div>
        <KnowledgeFiles ref-type="product" :ref-id="productId" />
      </div>
      <div v-if="productId && detailMeta" class="sys-meta">
        <div>审核：{{ detailMeta.auditStatus === 1 ? '已审核' : '未审核' }}
          · 审核人 {{ detailMeta.auditedByName || '—' }}
          · 审核时间 {{ formatTime(detailMeta.auditedAt) }}</div>
        <div>禁用：禁用人 {{ detailMeta.forbidByName || '—' }}
          · 禁用时间 {{ formatTime(detailMeta.forbidAt) }}
          · 反禁用人 {{ detailMeta.unforbidByName || '—' }}
          · 反禁用时间 {{ formatTime(detailMeta.unforbidAt) }}</div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  getProduct, createProduct, updateProduct,
  getUnitList, getRoutingList, getRouting, getOperationList
} from '@/api/base'
import ProductAttributes from '@/components/ProductAttributes.vue'
import { getCustomFields } from '@/api/prod'
import RoutingSteps from '@/components/RoutingSteps.vue'
import KnowledgeFiles from '@/components/KnowledgeFiles.vue'
import { ElMessage } from 'element-plus'
import { setPcTabTitle, closePcTabAndNavigate } from '@/composables/usePcTabs'

const route = useRoute()
const router = useRouter()
const isAdmin = Number(localStorage.getItem('role')) === 1

const booting = ref(true)
const saving = ref(false)
const loadingRouting = ref(false)
const productId = ref(null)
const copyFromId = ref(null)
const detailMeta = ref(null)
const form = ref(emptyForm())
const attributeFields = ref([])
const unitOptions = ref([])
const routingOptions = ref([])
const opOptions = ref([])

const detailLocked = computed(() => detailMeta.value?.auditStatus === 1)
const pageTitle = computed(() => {
  if (copyFromId.value) return '复制产品'
  const code = String(form.value?.code || '').trim()
  if (productId.value) return code || '产品'
  return '新建产品'
})

function emptyForm() {
  return {
    code: '', name: '', unitId: null, routingId: null,
    supplier: '', price: null, isRiskFocus: false, customValues: {}
  }
}

function formatTime(t) {
  return t ? String(t).replace('T', ' ').slice(0, 19) : '—'
}

function refreshTabTitle() {
  setPcTabTitle(route.path, pageTitle.value)
}

function leaveToList() {
  const path = route.path
  closePcTabAndNavigate(router, path, path, '/product')
}

function closeEdit() {
  if (saving.value) return
  leaveToList()
}

async function loadBill() {
  if (route.name !== 'ProductBill' && route.name !== 'ProductNew') return
  booting.value = true
  try {
    const isNew = route.name === 'ProductNew'
    if (isNew) {
      productId.value = null
      detailMeta.value = null
      form.value = emptyForm()
      const copyId = Number(route.query.copyFrom)
      if (Number.isFinite(copyId) && copyId > 0) {
        const res = await getProduct(copyId)
        const d = res.data
        copyFromId.value = copyId
        form.value = {
          code: '',
          name: d.name,
          unitId: d.unitId,
          routingId: d.routingId,
          supplier: d.supplier,
          price: d.price,
          isRiskFocus: false,
          customValues: d.customValues || {}
        }
      } else {
        copyFromId.value = null
      }
      refreshTabTitle()
      return
    }
    const id = Number(route.params.id)
    if (!Number.isFinite(id) || id <= 0) {
      ElMessage.error('产品链接无效，请从列表重新打开')
      leaveToList()
      return
    }
    const res = await getProduct(id)
    const d = res.data
    productId.value = id
    copyFromId.value = null
    detailMeta.value = {
      auditStatus: d.auditStatus,
      auditedByName: d.auditedByName,
      auditedAt: d.auditedAt,
      forbidByName: d.forbidByName,
      forbidAt: d.forbidAt,
      unforbidByName: d.unforbidByName,
      unforbidAt: d.unforbidAt
    }
    form.value = {
      code: d.code,
      name: d.name,
      unitId: d.unitId,
      routingId: d.routingId,
      supplier: d.supplier,
      price: d.price,
      isRiskFocus: !!d.isRiskFocus,
      customValues: d.customValues || {}
    }
    refreshTabTitle()
    if (d.auditStatus === 1) {
      ElMessage.info('已审核的产品不能直接改，请先反审核')
    }
  } finally {
    booting.value = false
  }
}

async function onSave() {
  if (saving.value) return
  if (!form.value.code.trim() || !form.value.name.trim()) {
    ElMessage.error('产品编号和名称都是必填项，请填写后再保存；产品编号在系统中必须唯一')
    return
  }
  if (form.value.newRouting) {
    const r = form.value.newRouting
    if (!r.code.trim() || !r.name.trim() || !r.steps.length || r.steps.some(s => !s.operationId)) {
      ElMessage.error('请填写新路线的编号和名称，并为每一道工序选择对应的加工步骤，不能留空')
      return
    }
    if (new Set(r.steps.map(s => s.operationId)).size !== r.steps.length) {
      ElMessage.error('同一条工艺路线中不能重复添加同一道工序，请检查工序顺序列表并去掉重复项')
      return
    }
  }
  saving.value = true
  try {
    const payload = {
      ...form.value,
      customValues: Object.fromEntries(
        Object.entries(form.value.customValues || {}).map(([k, v]) => [k, v == null ? null : String(v)])
      )
    }
    if (productId.value) await updateProduct(productId.value, payload)
    else await createProduct({ ...payload, copyFromId: copyFromId.value })
    const wasCopy = !productId.value && !!copyFromId.value
    let tip = productId.value
      ? '保存成功'
      : '已保存为未审核；请勾选后点「业务操作 → 审核」，审核后才能建工单选用'
    if (wasCopy) tip += '。计价规则、作业指导书、图纸未随复制带来，请到新资料中补齐'
    ElMessage.success(tip)
    leaveToList()
  } finally {
    saving.value = false
  }
}

async function copyRouting() {
  if (loadingRouting.value) return
  const sourceId = form.value.routingId
  const targetForm = form.value
  loadingRouting.value = true
  try {
    const [routeRes, ops] = await Promise.all([
      getRouting(sourceId),
      getOperationList({ page: 1, pageSize: 1000 })
    ])
    if (form.value !== targetForm || form.value.routingId !== sourceId || productId.value) return
    opOptions.value = ops.data.list || []
    const d = routeRes.data
    form.value.newRouting = {
      copyFromId: d.id,
      code: '',
      name: d.name,
      steps: d.steps.length
        ? d.steps.map((s, i) => ({ operationId: s.operationId, seq: i + 1 }))
        : [{ operationId: null }]
    }
  } finally {
    loadingRouting.value = false
  }
}

watch(() => form.value.code, () => refreshTabTitle())
watch(
  () => ({ name: route.name, fullPath: route.fullPath }),
  (cur, prev) => {
    if (!prev) return
    if (cur.name !== 'ProductBill' && cur.name !== 'ProductNew') return
    if (cur.fullPath === prev.fullPath) return
    loadBill()
  }
)

onMounted(async () => {
  attributeFields.value = (await getCustomFields('product')).data || []
  unitOptions.value = (await getUnitList()).data || []
  routingOptions.value = ((await getRoutingList({ page: 1, pageSize: 200 })).data.list) || []
  await loadBill()
})
</script>

<style scoped>
.product-bill { min-height: 0; }
.bill-top { flex-shrink: 0; }
.product-edit-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-start;
  gap: 6px 12px;
  width: 100%;
  margin: 0 0 4px;
  padding: 2px 0 !important;
  min-height: 32px;
  background: transparent !important;
  border: none !important;
  border-radius: 0 !important;
  box-shadow: none !important;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.product-edit-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}
.product-edit-toolbar :deep(.el-button) {
  height: 28px;
  padding: 4px 12px;
}
.product-edit-title {
  font-size: 13px;
  font-weight: 500;
  color: var(--el-text-color-secondary);
  line-height: 28px;
  margin-left: auto;
}
.browse-notice { margin: 4px 0; }
.product-edit-form :deep(.el-form-item) { margin-bottom: 6px; }
.product-form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  column-gap: 12px;
}
.product-form-grid :deep(.wide),
.product-form-grid :deep(.attr-wide) {
  grid-column: 1 / -1;
}
.product-form-grid :deep(.el-select),
.product-form-grid :deep(.el-input-number) {
  width: 100%;
}
@media (min-width: 1100px) {
  .product-form-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
@media (max-width: 640px) {
  .product-form-grid { grid-template-columns: 1fr; }
}
.kf-block { margin-top: 12px; }
.kf-title { font-weight: 600; margin-bottom: 8px; color: #303133; }
.sys-meta {
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid #ebeef5;
  color: #909399;
  font-size: 12px;
  line-height: 1.7;
}
.field-note { margin-top: 4px; color: #909399; font-size: 12px; line-height: 1.4; }
</style>
