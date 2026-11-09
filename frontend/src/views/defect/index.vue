<template>
  <div>
    <div class="toolbar">
      <el-button type="success" @click="openCreate">+ 创建不良品项</el-button>
      <el-button @click="onToolbarEdit">编辑</el-button>
      <el-button type="danger" @click="onToolbarDelete">删除</el-button>
    </div>
    <mes-table
      ref="tableRef"
      table-key="views/defect/index:1"
      :data="list"
      @selection-change="onSelectionChange"
      @row-click="onRowClick"
    >
      <el-table-column type="selection" width="48" fixed />
      <el-table-column prop="name" label="名称" />
    </mes-table>
    <el-dialog v-model="dlg" :title="editingId ? '编辑不良品项' : '创建不良品项'" width="400px">
      <el-input v-model="name" placeholder="例：粘锅、划伤、尺寸偏大（填常见不良现象名称）" />
      <template #footer>
        <el-button @click="dlg=false">取消</el-button>
        <el-button type="primary" @click="onSave">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, nextTick } from 'vue'
import { getDefectList, createDefect, updateDefect, deleteDefect } from '@/api/base'
import { ElMessage, ElMessageBox } from 'element-plus'

const list = ref([]), dlg = ref(false), name = ref(''), editingId = ref(null)
const tableRef = ref(null), selectedRows = ref([])
const selectedOne = computed(() => selectedRows.value.length === 1 ? selectedRows.value[0] : null)
onMounted(load)

async function load() {
  const res = await getDefectList()
  list.value = res.data || []
  selectedRows.value = []
  await nextTick()
  tableRef.value?.clearSelection?.()
}
function onSelectionChange(rows) { selectedRows.value = rows || [] }
function onRowClick(row, column, event) {
  if (event?.target?.closest?.('button, a, .el-checkbox, input, label')) return
  const table = tableRef.value
  if (!table?.toggleRowSelection) return
  table.clearSelection?.(); table.toggleRowSelection(row, true)
}
function requireOne(actionLabel) {
  if (selectedRows.value.length > 1) { ElMessage.warning(`「${actionLabel}」一次只能选一条`); return null }
  if (!selectedOne.value) { ElMessage.warning(`请先勾选一条不良品项再${actionLabel}`); return null }
  return selectedOne.value
}
function onToolbarEdit() { const row = requireOne('编辑'); if (row) openEdit(row) }
function onToolbarDelete() { const row = requireOne('删除'); if (row) onDelete(row) }

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
    ElMessage.error('请填写不良品项名称，例如：粘锅、划伤、尺寸偏大')
    return
  }
  if (editingId.value) await updateDefect(editingId.value, { name: name.value.trim() })
  else await createDefect({ name: name.value.trim() })
  ElMessage.success('保存成功')
  dlg.value = false
  await load()
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除不良品项“${row.name}”？若仍被工序引用则不能删除；删除后无法恢复`, '提示', { type: 'warning' })
  await deleteDefect(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; align-items: center; }
</style>
