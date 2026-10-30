<template>
  <div>
    <div class="toolbar">
      <el-input v-model="keyword" placeholder="例：GX001 / 车削（填编号或名称都能搜）" clearable style="width:200px" @keyup.enter="load" />
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" @click="openCreate">+ 创建工艺路线</el-button>
    </div>
    <mes-table table-key="views/routing/index:1" :data="list">
      <el-table-column prop="code" label="编号" />
      <el-table-column prop="name" label="名称" />
      <el-table-column prop="stepNames" label="工序顺序" />
      <el-table-column label="操作" width="220">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button v-if="isAdmin" link type="primary" @click="openCopy(row)">复制</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </mes-table>

    <el-dialog v-model="dlg" :title="editingId ? '编辑工艺路线' : copyFromId ? '复制工艺路线' : '创建工艺路线'" :close-on-click-modal="!saving" :close-on-press-escape="!saving" :show-close="!saving" width="560px">
      <el-form :model="form" :disabled="saving" label-width="90px">
        <el-form-item label="路线编号" required><el-input v-model="form.code" /></el-form-item>
        <el-form-item label="路线名称" required><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="工序明细">
          <RoutingSteps v-model="form.steps" :options="opOptions" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="saving" @click="dlg=false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="onSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import RoutingSteps from '@/components/RoutingSteps.vue'
import { getRoutingList, getRouting, createRouting, updateRouting, deleteRouting, getOperationList } from '@/api/base'
import { ElMessage, ElMessageBox } from 'element-plus'

const isAdmin = Number(localStorage.getItem('role')) === 1
const copyFromId = ref(null), saving = ref(false)
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
  copyFromId.value = null
  editingId.value = null
  form.value = { code: '', name: '', steps: [{ operationId: null }] }
  dlg.value = true
}

async function openEdit(row) {
  copyFromId.value = null
  const res = await getRouting(row.id)
  editingId.value = row.id
  const d = res.data
  form.value = { code: d.code, name: d.name, steps: d.steps.length ? d.steps.map(s => ({ operationId: s.operationId })) : [{ operationId: null }] }
  dlg.value = true
}

async function openCopy(row) {
  await openEdit(row)
  editingId.value = null
  copyFromId.value = row.id
  form.value.code = ''
}

async function onSave() {
  if (saving.value) return
  if (!form.value.code || !form.value.name) {
    ElMessage.error('请填写工艺路线的编号和名称，这两项都是必填项')
    return
  }
  const steps = form.value.steps
    .filter(s => s.operationId)
    .map((s, i) => ({ operationId: s.operationId, seq: i + 1 }))
  if (steps.length !== form.value.steps.length || new Set(steps.map(s => s.operationId)).size !== steps.length) {
    ElMessage.error('每道工序都必须选择，且同一条路线里不能重复添加同一道工序；请检查工序明细中的空白行或重复项')
    return
  }
  if (steps.length === 0) {
    ElMessage.error('工艺路线至少要包含一道工序，请在工序明细中添加')
    return
  }
  const payload = { code: form.value.code, name: form.value.name, steps }
  saving.value = true
  try {
    if (editingId.value) await updateRouting(editingId.value, payload)
    else await createRouting({ ...payload, copyFromId: copyFromId.value })
    ElMessage.success('保存成功')
    dlg.value = false
    await load()
  } finally { saving.value = false }
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除工艺路线「${row.name}」吗？若仍有产品引用该路线则不能删除；删除后无法恢复`, '提示', { type: 'warning' })
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
