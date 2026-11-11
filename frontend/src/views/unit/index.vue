<template>
  <div>
    <div class="toolbar">
      <el-dropdown trigger="click" @command="onCreateMenu">
        <el-button>
          新增<el-icon class="el-icon--right"><ArrowDown /></el-icon>
        </el-button>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item command="create">新增</el-dropdown-item>
            <el-dropdown-item command="copy">复制</el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>
      <el-button @click="onToolbarEdit">编辑</el-button>
      <el-button @click="onToolbarDelete">删除</el-button>
    </div>
    <mes-table
      ref="tableRef"
      table-key="views/unit/index:1"
      :data="list"
      @selection-change="onSelectionChange"
      @row-click="onRowClick"
    >
      <el-table-column type="selection" width="48" fixed />
      <el-table-column prop="name" label="单位名称" />
    </mes-table>
    <el-dialog v-model="dlg" :title="editingId ? '编辑单位' : (copying ? '复制单位' : '创建单位')" width="400px">
      <el-input v-model="name" placeholder="例：只、件、套、kg" />
      <template #footer>
        <el-button @click="dlg=false">取消</el-button>
        <el-button type="primary" @click="onSave">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>
<script setup>
import { ref, computed, onMounted, nextTick } from 'vue'
import { getUnitList, createUnit, updateUnit, deleteUnit } from '@/api/base'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowDown } from '@element-plus/icons-vue'
const list = ref([]), dlg = ref(false), name = ref(''), editingId = ref(null), copying = ref(false)
const tableRef = ref(null), selectedRows = ref([])
const selectedOne = computed(() => selectedRows.value.length === 1 ? selectedRows.value[0] : null)
onMounted(load)
async function load() {
  const res = await getUnitList()
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
  if (!selectedOne.value) { ElMessage.warning(`请先勾选一条单位再${actionLabel}`); return null }
  return selectedOne.value
}
function onToolbarEdit() { const row = requireOne('编辑'); if (row) openEdit(row) }
function onToolbarDelete() { const row = requireOne('删除'); if (row) onDelete(row) }
function onCreateMenu(cmd) {
  if (cmd === 'create') { openCreate(); return }
  if (cmd === 'copy') { const row = requireOne('复制'); if (row) openCopy(row) }
}
function openCreate() { editingId.value = null; copying.value = false; name.value = ''; dlg.value = true }
function openCopy(row) { editingId.value = null; copying.value = true; name.value = row.name || ''; dlg.value = true }
function openEdit(row) { editingId.value = row.id; copying.value = false; name.value = row.name; dlg.value = true }
async function onSave() {
  if (!name.value.trim()) { ElMessage.error('单位名称不能为空，请在上方输入框填写后保存'); return }
  if (editingId.value) await updateUnit(editingId.value, { name: name.value.trim() })
  else await createUnit({ name: name.value.trim() })
  ElMessage.success('保存成功'); dlg.value = false; await load()
}
async function onDelete(row) {
  await ElMessageBox.confirm('确认删除该单位吗？若已被产品或工序价目表引用则不能删除；删除后无法恢复', '提示', { type: 'warning' })
  await deleteUnit(row.id); ElMessage.success('已删除'); await load()
}
</script>
<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; align-items: center; }
</style>
