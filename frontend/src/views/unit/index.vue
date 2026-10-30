<template>
  <div>
    <div class="toolbar"><el-button type="success" @click="openCreate">+ 创建单位</el-button></div>
    <mes-table table-key="views/unit/index:1" :data="list">
      <el-table-column prop="name" label="单位名称" />
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </mes-table>
    <el-dialog v-model="dlg" :title="editingId ? '编辑单位' : '创建单位'" width="400px">
      <el-input v-model="name" placeholder="例：只、件、套、kg" />
      <template #footer>
        <el-button @click="dlg=false">取消</el-button>
        <el-button type="primary" @click="onSave">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>
<script setup>
import { ref, onMounted } from 'vue'
import { getUnitList, createUnit, updateUnit, deleteUnit } from '@/api/base'
import { ElMessage, ElMessageBox } from 'element-plus'
const list = ref([]), dlg = ref(false), name = ref(''), editingId = ref(null)
onMounted(load)
async function load() { const res = await getUnitList(); list.value = res.data || [] }
function openCreate() { editingId.value = null; name.value = ''; dlg.value = true }
function openEdit(row) { editingId.value = row.id; name.value = row.name; dlg.value = true }
async function onSave() {
  if (!name.value.trim()) { ElMessage.error('单位名称不能为空，请在上方输入框填写后保存'); return }
  if (editingId.value) await updateUnit(editingId.value, { name: name.value.trim() })
  else await createUnit({ name: name.value.trim() })
  ElMessage.success('保存成功'); dlg.value = false; await load()
}
async function onDelete(row) {
  await ElMessageBox.confirm('确认删除该产品吗？若已被工单或工序价目表引用则不能删除；删除后无法恢复', '提示', { type: 'warning' })
  await deleteUnit(row.id); ElMessage.success('已删除'); await load()
}
</script>
<style scoped>.toolbar{margin-bottom:12px}</style>
