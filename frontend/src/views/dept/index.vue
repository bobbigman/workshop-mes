<template>
  <div>
    <div class="toolbar">
      <el-input v-model="keyword" placeholder="编码/名称" clearable style="width:200px" @keyup.enter="load" />
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" @click="openCreate">+ 创建部门</el-button>
    </div>
    <el-table :data="list">
      <el-table-column prop="code" label="部门编码" />
      <el-table-column prop="name" label="部门名称" />
      <el-table-column prop="members" label="成员" />
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dlg" :title="editingId ? '编辑部门' : '创建部门'" width="480px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="部门编码"><el-input v-model="form.code" /></el-form-item>
        <el-form-item label="部门名称"><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="成员">
          <el-select v-model="form.memberIds" multiple filterable style="width:100%" placeholder="选择人员">
            <el-option v-for="u in userOptions" :key="u.id" :label="u.name" :value="u.id" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg=false">取消</el-button>
        <el-button type="primary" @click="onSave">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getDeptList, getDept, createDept, updateDept, deleteDept, getUserList } from '@/api/sys'
import { ElMessage, ElMessageBox } from 'element-plus'

const list = ref([]), keyword = ref(''), dlg = ref(false), editingId = ref(null)
const form = ref({ code: '', name: '', memberIds: [] })
const userOptions = ref([])

onMounted(async () => {
  await load()
  const res = await getUserList({ page: 1, pageSize: 200 })
  userOptions.value = res.data.list || []
})

async function load() {
  const res = await getDeptList({ keyword: keyword.value, page: 1, pageSize: 100 })
  list.value = res.data.list || []
}

function openCreate() {
  editingId.value = null
  form.value = { code: '', name: '', memberIds: [] }
  dlg.value = true
}

async function openEdit(row) {
  const res = await getDept(row.id)
  editingId.value = row.id
  const d = res.data
  form.value = { code: d.code, name: d.name, memberIds: d.memberIds }
  dlg.value = true
}

async function onSave() {
  if (!form.value.code || !form.value.name) {
    ElMessage.error('请填写部门编码和名称')
    return
  }
  if (editingId.value) await updateDept(editingId.value, form.value)
  else await createDept(form.value)
  ElMessage.success('保存成功')
  dlg.value = false
  await load()
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除部门 ${row.name}？`, '提示', { type: 'warning' })
  await deleteDept(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; }
</style>
