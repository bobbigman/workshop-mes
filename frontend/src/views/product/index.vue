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
      <el-table-column prop="unitName" label="单位" />
      <el-table-column prop="routingName" label="工艺路线" />
      <el-table-column prop="supplier" label="供应商" />
      <el-table-column prop="price" label="单价" width="90" />
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dlg" :title="editingId ? '编辑产品' : '创建产品'" width="520px">
      <el-form :model="form" label-width="100px">
        <el-form-item label="产品编号" required><el-input v-model="form.code" /></el-form-item>
        <el-form-item label="产品名称" required><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="单位">
          <el-select v-model="form.unitId" clearable placeholder="选择" style="width:100%">
            <el-option v-for="u in unitOptions" :key="u.id" :label="u.name" :value="u.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="工艺路线">
          <el-select v-model="form.routingId" clearable placeholder="选择" style="width:100%">
            <el-option v-for="r in routingOptions" :key="r.id" :label="r.name" :value="r.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="供应商"><el-input v-model="form.supplier" /></el-form-item>
        <el-form-item label="单价"><el-input-number v-model="form.price" :min="0" :precision="2" /></el-form-item>
      </el-form>
      <div v-if="editingId" class="kf-block">
        <div class="kf-title">作业指导书 / 图纸</div>
        <KnowledgeFiles ref-type="product" :ref-id="editingId" />
      </div>
      <template #footer>
        <el-button @click="dlg=false">取消</el-button>
        <el-button type="primary" @click="onSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
<script setup>
import { ref, onMounted } from 'vue'
import { getProductList, getProduct, createProduct, updateProduct, deleteProduct, getUnitList, getRoutingList } from '@/api/base'
import ImportDialog from '@/components/ImportDialog.vue'
import KnowledgeFiles from '@/components/KnowledgeFiles.vue'
import { ElMessage, ElMessageBox } from 'element-plus'

const list = ref([]), keyword = ref(''), dlg = ref(false), editingId = ref(null)
const form = ref({ code: '', name: '', unitId: null, routingId: null, supplier: '', price: null })
const unitOptions = ref([]), routingOptions = ref([])

onMounted(async () => {
  await load()
  unitOptions.value = (await getUnitList()).data || []
  routingOptions.value = ((await getRoutingList({ page: 1, pageSize: 200 })).data.list) || []
})

async function load() {
  const res = await getProductList({ keyword: keyword.value, page: 1, pageSize: 100 })
  list.value = res.data.list || []
}
function openCreate() {
  editingId.value = null
  form.value = { code: '', name: '', unitId: null, routingId: null, supplier: '', price: null }
  dlg.value = true
}
async function openEdit(row) {
  const res = await getProduct(row.id)
  editingId.value = row.id
  const d = res.data
  form.value = { code: d.code, name: d.name, unitId: d.unitId, routingId: d.routingId, supplier: d.supplier, price: d.price }
  dlg.value = true
}
async function onSave() {
  if (!form.value.code) { ElMessage.error('请填写产品编号'); return }
  if (editingId.value) await updateProduct(editingId.value, form.value)
  else await createProduct(form.value)
  ElMessage.success('保存成功'); dlg.value = false; await load()
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
