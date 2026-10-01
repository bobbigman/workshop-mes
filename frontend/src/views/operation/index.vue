<template>
  <div>
    <div class="toolbar">
      <el-input v-model="keyword" placeholder="编号/名称" clearable style="width:200px" @keyup.enter="load" />
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" @click="openCreate">+ 创建工序</el-button>
      <ImportDialog entity-type="operation" title="导入工序" @done="load" />
    </div>
    <el-table :data="list">
      <el-table-column prop="code" label="工序编号" />
      <el-table-column prop="name" label="工序名称" />
      <el-table-column prop="deptNames" label="报工权限(部门)" />
      <el-table-column prop="defectNames" label="不良品项" />
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dlg" :title="editingId ? '编辑工序' : '创建工序'" width="520px">
      <el-form :model="form" label-width="110px">
        <el-form-item label="工序编号" required><el-input v-model="form.code" /></el-form-item>
        <el-form-item label="工序名称" required><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="报工权限">
          <el-select v-model="form.deptIds" multiple filterable style="width:100%" placeholder="选择部门">
            <el-option v-for="d in deptOptions" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="不良品项">
          <el-select v-model="form.defectIds" multiple filterable style="width:100%" placeholder="选择不良品项">
            <el-option v-for="d in defectOptions" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
      </el-form>
      <div v-if="editingId" class="kf-block">
        <div class="kf-title">作业指导书 / 图纸</div>
        <KnowledgeFiles ref-type="operation" :ref-id="editingId" />
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
import { getOperationList, getOperation, createOperation, updateOperation, deleteOperation, getDefectList } from '@/api/base'
import { getDeptList } from '@/api/sys'
import ImportDialog from '@/components/ImportDialog.vue'
import KnowledgeFiles from '@/components/KnowledgeFiles.vue'
import { ElMessage, ElMessageBox } from 'element-plus'

const list = ref([]), keyword = ref(''), dlg = ref(false), editingId = ref(null)
const form = ref({ code: '', name: '', deptIds: [], defectIds: [] })
const deptOptions = ref([]), defectOptions = ref([])

onMounted(async () => {
  await load()
  deptOptions.value = ((await getDeptList({ page: 1, pageSize: 200 })).data.list) || []
  defectOptions.value = (await getDefectList()).data || []
})

async function load() {
  const res = await getOperationList({ keyword: keyword.value, page: 1, pageSize: 100 })
  list.value = res.data.list || []
}

function openCreate() {
  editingId.value = null
  form.value = { code: '', name: '', deptIds: [], defectIds: [] }
  dlg.value = true
}

async function openEdit(row) {
  const res = await getOperation(row.id)
  editingId.value = row.id
  const d = res.data
  form.value = { code: d.code, name: d.name, deptIds: d.deptIds, defectIds: d.defectIds }
  dlg.value = true
}

async function onSave() {
  if (!form.value.code || !form.value.name) {
    ElMessage.error('请填写工序编号和名称')
    return
  }
  if (editingId.value) await updateOperation(editingId.value, form.value)
  else await createOperation(form.value)
  ElMessage.success('保存成功')
  dlg.value = false
  await load()
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除工序 ${row.name}？`, '提示', { type: 'warning' })
  await deleteOperation(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; }
.kf-block { margin-top: 4px; }
.kf-title { font-weight: 600; margin-bottom: 8px; color: #303133; }
</style>
