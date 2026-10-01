<template>
  <div>
    <div class="toolbar">
      <el-select v-model="target" style="width:160px" @change="load">
        <el-option value="work_order" label="工单" />
        <el-option value="product" label="产品" />
        <el-option value="operation" label="工序" />
        <el-option value="report" label="报工" />
      </el-select>
      <el-button type="success" @click="openCreate">+ 添加字段</el-button>
    </div>
    <el-table :data="list">
      <el-table-column prop="fieldName" label="字段名称" />
      <el-table-column prop="fieldKey" label="编码" width="120">
        <template #default="{ row }">{{ row.fieldKey || '—' }}</template>
      </el-table-column>
      <el-table-column label="字段类型" width="120">
        <template #default="{ row }">{{ typeLabel(row.fieldType) }}</template>
      </el-table-column>
      <el-table-column label="选项">
        <template #default="{ row }">{{ (row.options || []).join('、') }}</template>
      </el-table-column>
      <el-table-column v-if="target === 'work_order'" label="列表显示" width="100">
        <template #default="{ row }">{{ row.showInOrderList ? '是' : '否' }}</template>
      </el-table-column>
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" :disabled="deletingId !== null" :loading="deletingId === row.id" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dlg" :title="editingId ? '编辑自定义字段' : '创建自定义字段'" width="480px">
      <el-form :model="form" label-width="120px">
        <el-form-item label="字段名称" required>
          <el-input v-model="form.fieldName" placeholder="如：颜色" />
        </el-form-item>
        <el-form-item label="字段编码">
          <el-input v-model="form.fieldKey" placeholder="可选；色码填 color / spec" />
          <div class="hint">小写字母开头；色码汇总认 color（颜色）、spec（规格/尺码）。空=普通字段</div>
        </el-form-item>
        <el-form-item label="字段类型" required>
          <el-select v-model="form.fieldType" style="width:100%" :disabled="!!editingId">
            <el-option value="single" label="单选" />
            <el-option value="text" label="文本" />
            <el-option value="number" label="数字" />
          </el-select>
          <div v-if="editingId" class="hint">字段类型不可修改</div>
        </el-form-item>
        <el-form-item v-if="form.fieldType === 'single'" label="选项">
          <div v-for="(o, i) in form.options" :key="i" class="opt-row">
            <el-input v-model="form.options[i]" />
            <el-button @click="form.options.splice(i, 1)">×</el-button>
          </div>
          <el-button size="small" @click="form.options.push('')">+ 添加选项</el-button>
        </el-form-item>
        <el-form-item v-if="target === 'work_order'" label="在工单列表显示">
          <el-switch v-model="form.showInOrderList" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg=false">取消</el-button>
        <el-button type="primary" @click="onSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getCustomFields, createCustomField, updateCustomField, deleteCustomField } from '@/api/prod'
import { ElMessage, ElMessageBox } from 'element-plus'

const target = ref('work_order'), list = ref([]), dlg = ref(false), editingId = ref(null)
const form = ref({ fieldName: '', fieldKey: '', fieldType: 'single', options: [''], showInOrderList: false })
const deletingId = ref(null)

async function onDelete(row) {
  if (deletingId.value !== null) return
  deletingId.value = row.id
  try {
    try {
      await ElMessageBox.confirm(`确定删除字段“${row.fieldName}”及其选项吗？已有业务数据的字段不能删除。`, '删除自定义字段', {
        type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消'
      })
    } catch (action) {
      if (action === 'cancel' || action === 'close') return
      throw action
    }
    await deleteCustomField(row.id)
    ElMessage.success('删除成功')
    await load()
  } finally {
    deletingId.value = null
  }
}

const typeMap = { single: '单选', text: '文本', number: '数字' }
function typeLabel(t) { return typeMap[t] || t }

onMounted(load)

async function load() {
  const res = await getCustomFields(target.value)
  list.value = res.data || []
}

function openCreate() {
  editingId.value = null
  form.value = { fieldName: '', fieldKey: '', fieldType: 'single', options: [''], showInOrderList: false }
  dlg.value = true
}

function openEdit(row) {
  editingId.value = row.id
  form.value = {
    fieldName: row.fieldName,
    fieldKey: row.fieldKey || '',
    fieldType: row.fieldType,
    options: row.options && row.options.length ? [...row.options] : [''],
    showInOrderList: !!row.showInOrderList
  }
  dlg.value = true
}

async function onSave() {
  if (!form.value.fieldName.trim()) {
    ElMessage.error('请填写字段名称')
    return
  }
  if (form.value.fieldType === 'single' && form.value.options.filter(x => x.trim()).length === 0) {
    ElMessage.error('单选字段必须添加选项')
    return
  }
  const payload = {
    target: target.value,
    fieldName: form.value.fieldName.trim(),
    fieldKey: (form.value.fieldKey || '').trim(),
    fieldType: form.value.fieldType,
    options: form.value.options.filter(x => x.trim())
  }
  if (target.value === 'work_order') {
    payload.showInOrderList = !!form.value.showInOrderList
  }
  if (editingId.value) await updateCustomField(editingId.value, payload)
  else await createCustomField(payload)
  ElMessage.success('保存成功')
  dlg.value = false
  await load()
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; }
.opt-row { display: flex; gap: 8px; margin-bottom: 6px; }
.hint { color: #909399; font-size: 12px; }
</style>
