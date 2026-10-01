<template>
  <div>
    <div class="toolbar">
      <el-input v-model="keyword" placeholder="编号/名称" clearable style="width:200px" @keyup.enter="load" />
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" @click="openCreate">+ 创建工艺路线</el-button>
    </div>
    <el-table :data="list">
      <el-table-column prop="code" label="编号" />
      <el-table-column prop="name" label="名称" />
      <el-table-column prop="stepNames" label="工序顺序" />
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dlg" :title="editingId ? '编辑工艺路线' : '创建工艺路线'" width="560px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="路线编号" required><el-input v-model="form.code" /></el-form-item>
        <el-form-item label="路线名称" required><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="工序明细">
          <div v-for="(s, i) in form.steps" :key="i" class="step-row">
            <span class="seq">{{ i + 1 }}</span>
            <el-select v-model="s.operationId" placeholder="选择工序" style="flex:1" filterable>
              <el-option v-for="o in opOptions" :key="o.id" :label="o.code + ' ' + o.name" :value="o.id" />
            </el-select>
            <el-button @click="form.steps.splice(i, 1)" :disabled="form.steps.length <= 1">×</el-button>
          </div>
          <el-button size="small" @click="addStep">+ 添加一行</el-button>
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
import { getRoutingList, getRouting, createRouting, updateRouting, deleteRouting, getOperationList } from '@/api/base'
import { ElMessage, ElMessageBox } from 'element-plus'

const list = ref([]), keyword = ref(''), dlg = ref(false), opOptions = ref([]), editingId = ref(null)
const form = ref({ code: '', name: '', steps: [] })

onMounted(async () => {
  await load()
  opOptions.value = ((await getOperationList({ page: 1, pageSize: 200 })).data.list) || []
})

async function load() {
  const res = await getRoutingList({ keyword: keyword.value, page: 1, pageSize: 100 })
  list.value = res.data.list || []
}

function openCreate() {
  editingId.value = null
  form.value = { code: '', name: '', steps: [{ operationId: null }] }
  dlg.value = true
}

async function openEdit(row) {
  const res = await getRouting(row.id)
  editingId.value = row.id
  const d = res.data
  form.value = { code: d.code, name: d.name, steps: d.steps.length ? d.steps.map(s => ({ operationId: s.operationId })) : [{ operationId: null }] }
  dlg.value = true
}

function addStep() {
  form.value.steps.push({ operationId: null })
}

async function onSave() {
  if (!form.value.code || !form.value.name) {
    ElMessage.error('请填写路线编号和名称')
    return
  }
  const steps = form.value.steps
    .filter(s => s.operationId)
    .map((s, i) => ({ operationId: s.operationId, seq: i + 1 }))
  if (steps.length === 0) {
    ElMessage.error('请至少选择一道工序')
    return
  }
  const payload = { code: form.value.code, name: form.value.name, steps }
  if (editingId.value) await updateRouting(editingId.value, payload)
  else await createRouting(payload)
  ElMessage.success('保存成功')
  dlg.value = false
  await load()
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除工艺路线 ${row.name}？`, '提示', { type: 'warning' })
  await deleteRouting(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; }
.step-row { display: flex; gap: 8px; align-items: center; margin-bottom: 8px; }
.seq { width: 20px; color: #909399; text-align: center; }
</style>
