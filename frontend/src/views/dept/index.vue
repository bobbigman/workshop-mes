<template>
  <div>
    <div class="toolbar">
      <el-input v-model="keyword" placeholder="搜索部门编码或名称，例：SC01 或 生产部" clearable style="width:200px" @keyup.enter="onSearch" />
      <el-button @click="onSearch">查询</el-button>
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
      table-key="views/dept/index:1"
      :data="list"
      @selection-change="onSelectionChange"
      @row-click="onRowClick"
    >
      <el-table-column type="selection" width="48" fixed />
      <el-table-column prop="code" label="部门编码" />
      <el-table-column prop="name" label="部门名称" />
      <el-table-column prop="members" label="成员" />
    </mes-table>
    <mes-pager v-model:page="page" v-model:page-size="pageSize" :total="total" @change="load" />

    <el-dialog v-model="dlg" :title="editingId ? '编辑部门' : (copying ? '复制部门' : '创建部门')" width="480px">
      <el-alert v-if="copying" title="请填写新的部门编码；名称和成员已带入，可再改。" type="info" :closable="false" show-icon style="margin-bottom:12px" />
      <el-form :model="form" label-width="90px">
        <el-form-item label="部门编码"><el-input v-model="form.code" /></el-form-item>
        <el-form-item label="部门名称"><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="成员">
          <el-select v-model="form.memberIds" multiple filterable style="width:100%" placeholder="搜索并选择要加入此部门的人员姓名">
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
import { ref, computed, onMounted, nextTick } from 'vue'
import { getDeptList, getDept, createDept, updateDept, deleteDept, getUserList } from '@/api/sys'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowDown } from '@element-plus/icons-vue'

const list = ref([]), keyword = ref(''), dlg = ref(false), editingId = ref(null), copying = ref(false)
const page = ref(1), pageSize = ref(20), total = ref(0)
const form = ref({ code: '', name: '', memberIds: [] })
const userOptions = ref([])
const tableRef = ref(null), selectedRows = ref([])
const selectedOne = computed(() => selectedRows.value.length === 1 ? selectedRows.value[0] : null)

onMounted(async () => {
  await load()
  const res = await getUserList({ page: 1, pageSize: 200 })
  userOptions.value = res.data.list || []
})

async function load() {
  const res = await getDeptList({ keyword: keyword.value, page: page.value, pageSize: pageSize.value })
  list.value = res.data.list || []
  total.value = res.data.total || 0
  selectedRows.value = []
  await nextTick()
  tableRef.value?.clearSelection?.()
}
function onSearch() {
  page.value = 1
  load()
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
  if (!selectedOne.value) { ElMessage.warning(`请先勾选一条部门再${actionLabel}`); return null }
  return selectedOne.value
}
function onToolbarEdit() { const row = requireOne('编辑'); if (row) openEdit(row) }
function onToolbarDelete() { const row = requireOne('删除'); if (row) onDelete(row) }
function onCreateMenu(cmd) {
  if (cmd === 'create') { openCreate(); return }
  if (cmd === 'copy') { const row = requireOne('复制'); if (row) openCopy(row) }
}

function openCreate() {
  editingId.value = null
  copying.value = false
  form.value = { code: '', name: '', memberIds: [] }
  dlg.value = true
}

async function openCopy(row) {
  const res = await getDept(row.id)
  editingId.value = null
  copying.value = true
  const d = res.data
  form.value = { code: '', name: d.name, memberIds: [...(d.memberIds || [])] }
  dlg.value = true
}

async function openEdit(row) {
  const res = await getDept(row.id)
  editingId.value = row.id
  copying.value = false
  const d = res.data
  form.value = { code: d.code, name: d.name, memberIds: d.memberIds }
  dlg.value = true
}

async function onSave() {
  if (!form.value.code || !form.value.name) {
    ElMessage.error('请同时填写部门编码和部门名称，两项都是必填项')
    return
  }
  if (editingId.value) await updateDept(editingId.value, form.value)
  else await createDept(form.value)
  ElMessage.success('保存成功')
  dlg.value = false
  await load()
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除部门“${row.name}”？若仍被工序设为报工权限、部门下还有人员、或已被派工为班组，则不能删除`, '提示', { type: 'warning' })
  await deleteDept(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; align-items: center; }
</style>
