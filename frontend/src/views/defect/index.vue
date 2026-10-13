<template>
  <div>
    <div class="toolbar"><el-button type="success" @click="openCreate">+ 创建不良品项</el-button></div>
    <el-table :data="list">
      <el-table-column prop="name" label="名称" />
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-dialog v-model="dlg" :title="editingId ? '编辑不良品项' : '创建不良品项'" width="400px">
      <el-input v-model="name" placeholder="如：粘锅" />
      <template #footer>
        <el-button @click="dlg=false">取消</el-button>
        <el-button type="primary" @click="onSave">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getDefectList, createDefect, updateDefect, deleteDefect } from '@/api/base'
import { ElMessage, ElMessageBox } from 'element-plus'

const list = ref([]), dlg = ref(false), name = ref(''), editingId = ref(null)
onMounted(load)

async function load() {
  const res = await getDefectList()
  list.value = res.data || []
}

function openCreate() {
  editingId.value = null
  name.value = ''
  dlg.value = true
}

function openEdit(row) {
  editingId.value = row.id
  name.value = row.name
  dlg.value = true
}

async function onSave() {
  if (!name.value.trim()) {
    ElMessage.error('请填写不良品项名称')
    return
  }
  if (editingId.value) await updateDefect(editingId.value, { name: name.value.trim() })
  else await createDefect({ name: name.value.trim() })
  ElMessage.success('保存成功')
  dlg.value = false
  await load()
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除「${row.name}」？`, '提示', { type: 'warning' })
  await deleteDefect(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<style scoped>
.toolbar { margin-bottom: 12px; }
</style>
