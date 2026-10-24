<template>
  <div>
    <div class="toolbar">
      <el-input v-model="keyword" placeholder="编号/名称" clearable style="width:200px" @keyup.enter="load" />
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" @click="openCreate">+ 创建产品</el-button>
      <ImportDialog entity-type="product" title="导入产品" @done="load" />
    </div>
    <el-table :data="list">
      <el-table-column prop="code" label="产品编号" />
      <el-table-column prop="name" label="产品名称" />
      <el-table-column label="重点产品" width="100">
        <template #default="{ row }"><el-tag v-if="row.isRiskFocus" type="warning">重点</el-tag></template>
      </el-table-column>
      <el-table-column prop="unitName" label="单位" />
      <el-table-column prop="routingName" label="工艺路线" />
      <el-table-column prop="supplier" label="供应商" />
      <el-table-column prop="price" label="单价" width="90" />
      <el-table-column label="操作" width="220">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button v-if="isAdmin" link type="primary" @click="openCopy(row)">复制</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dlg" :title="editingId ? '编辑产品' : copyFromId ? '复制产品' : '创建产品'" :close-on-click-modal="!saving" :close-on-press-escape="!saving" :show-close="!saving" width="520px">
      <el-alert v-if="copyFromId" title="请填写新编号；作业指导书、图纸及独立计价规则未复制。" type="info" :closable="false" show-icon style="margin-bottom:12px" />
      <el-form :model="form" :disabled="saving" label-width="100px">
        <el-form-item label="产品编号" required><el-input v-model="form.code" /></el-form-item>
        <el-form-item label="产品名称" required><el-input v-model="form.name" /></el-form-item>
        <el-form-item v-if="isAdmin" label="重点产品">
          <el-checkbox v-model="form.isRiskFocus">风险简报优先关注</el-checkbox>
        </el-form-item>
        <el-form-item label="单位">
          <el-select v-model="form.unitId" clearable placeholder="选择" style="width:100%">
            <el-option v-for="u in unitOptions" :key="u.id" :label="u.name" :value="u.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="工艺路线">
          <el-select v-model="form.routingId" :disabled="!!form.newRouting" clearable filterable placeholder="选择" style="width:100%">
            <el-option v-for="r in routingOptions" :key="r.id" :label="r.name" :value="r.id" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="!editingId && form.routingId" label="调整工艺">
          <el-button v-if="!form.newRouting" :loading="loadingRouting" @click="copyRouting">复制工艺并修改</el-button>
          <el-button v-else @click="form.newRouting = null">沿用原工艺</el-button>
        </el-form-item>
        <template v-if="form.newRouting">
          <el-form-item label="新路线编号" required><el-input v-model="form.newRouting.code" /></el-form-item>
          <el-form-item label="新路线名称" required><el-input v-model="form.newRouting.name" /></el-form-item>
          <el-form-item label="工序顺序"><RoutingSteps v-model="form.newRouting.steps" :options="opOptions" /></el-form-item>
        </template>
        <el-form-item label="供应商"><el-input v-model="form.supplier" /></el-form-item>
        <el-form-item label="单价"><el-input-number v-model="form.price" :min="0" :precision="2" /></el-form-item>
        <ProductAttributes v-model="form.customValues" :fields="attributeFields" />
      </el-form>
      <ProductPricingModes v-if="editingId" :product-id="editingId" :routing-id="form.routingId" />
      <div v-if="editingId" class="kf-block">
        <div class="kf-title">作业指导书 / 图纸</div>
        <KnowledgeFiles ref-type="product" :ref-id="editingId" />
      </div>
      <template #footer>
        <el-button :disabled="saving" @click="dlg=false">取消</el-button>
        <el-button type="primary" :disabled="loadingRouting" :loading="saving" @click="onSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
<script setup>
import { ref, onMounted } from 'vue'
import { getProductList, getProduct, createProduct, updateProduct, deleteProduct, getUnitList, getRoutingList, getRouting, getOperationList } from '@/api/base'
import ProductPricingModes from '@/components/ProductPricingModes.vue'
import ProductAttributes from '@/components/ProductAttributes.vue'
import { getCustomFields } from '@/api/prod'
import RoutingSteps from '@/components/RoutingSteps.vue'
import ImportDialog from '@/components/ImportDialog.vue'
import KnowledgeFiles from '@/components/KnowledgeFiles.vue'
import { ElMessage, ElMessageBox } from 'element-plus'

const isAdmin = Number(localStorage.getItem('role')) === 1
const copyFromId = ref(null), saving = ref(false), loadingRouting = ref(false)
const list = ref([]), keyword = ref(''), dlg = ref(false), editingId = ref(null)
const form = ref({ code: '', name: '', unitId: null, routingId: null, supplier: '', price: null, isRiskFocus: false, customValues: {} })
const attributeFields = ref([])
const unitOptions = ref([]), routingOptions = ref([]), opOptions = ref([])

onMounted(async () => {
  attributeFields.value = (await getCustomFields('product')).data || []
  await load()
  unitOptions.value = (await getUnitList()).data || []
  routingOptions.value = ((await getRoutingList({ page: 1, pageSize: 200 })).data.list) || []
})

async function load() {
  const res = await getProductList({ keyword: keyword.value, page: 1, pageSize: 100 })
  list.value = res.data.list || []
}
function openCreate() {
  copyFromId.value = null
  editingId.value = null
  form.value = { code: '', name: '', unitId: null, routingId: null, supplier: '', price: null, isRiskFocus: false, customValues: {} }
  dlg.value = true
}
async function openEdit(row) {
  copyFromId.value = null
  const res = await getProduct(row.id)
  editingId.value = row.id
  const d = res.data
  form.value = { code: d.code, name: d.name, unitId: d.unitId, routingId: d.routingId, supplier: d.supplier, price: d.price, isRiskFocus: !!d.isRiskFocus, customValues: d.customValues || {} }
  dlg.value = true
}
async function openCopy(row) {
  await openEdit(row)
  editingId.value = null
  copyFromId.value = row.id
  form.value.code = ''
  form.value.isRiskFocus = false
}

async function onSave() {
  if (saving.value) return
  if (!form.value.code.trim() || !form.value.name.trim()) { ElMessage.error('请填写产品编号和名称'); return }
  if (form.value.newRouting) {
    const r = form.value.newRouting
    if (!r.code.trim() || !r.name.trim() || !r.steps.length || r.steps.some(s => !s.operationId)) {
      ElMessage.error('请填写新路线编号、名称并选择每一道工序'); return
    }
    if (new Set(r.steps.map(s => s.operationId)).size !== r.steps.length) {
      ElMessage.error('同一路线不能重复选择同一道工序'); return
    }
  }
  saving.value = true
  try {
    const payload = { ...form.value, customValues: Object.fromEntries(Object.entries(form.value.customValues || {}).map(([k,v])=>[k,v==null?null:String(v)])) }
    if (editingId.value) await updateProduct(editingId.value, payload)
    else await createProduct({ ...payload, copyFromId: copyFromId.value })
    ElMessage.success('保存成功'); dlg.value = false
    await load()
    if (form.value.newRouting) routingOptions.value = (await getRoutingList({ page: 1, pageSize: 200 })).data.list || []
  } finally { saving.value = false }
}
async function copyRouting() {
  if (loadingRouting.value) return
  const sourceId = form.value.routingId
  const targetForm = form.value
  loadingRouting.value = true
  try {
    const [route, ops] = await Promise.all([
      getRouting(sourceId), getOperationList({ page: 1, pageSize: 1000 })
    ])
    if (!dlg.value || form.value !== targetForm || form.value.routingId !== sourceId || editingId.value) return
    opOptions.value = ops.data.list || []
    const d = route.data
    form.value.newRouting = {
      copyFromId: d.id, code: '', name: d.name,
      steps: d.steps.length ? d.steps.map((s, i) => ({ operationId: s.operationId, seq: i + 1 })) : [{ operationId: null }]
    }
  } finally { loadingRouting.value = false }
}
async function onDelete(row) {
  await ElMessageBox.confirm('确认删除？', '提示', { type: 'warning' })
  await deleteProduct(row.id); ElMessage.success('已删除'); await load()
}
</script>
<style scoped>.toolbar{display:flex;gap:8px;margin-bottom:12px}
.kf-block { margin-top: 4px; }
.kf-title { font-weight: 600; margin-bottom: 8px; color: #303133; }
</style>
