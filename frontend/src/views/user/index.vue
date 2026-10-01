<template>
  <div>
    <div class="toolbar">
      <el-input v-model="keyword" placeholder="姓名/账号/微信号" clearable style="width:220px" @keyup.enter="load" />
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" @click="openCreate">+ 创建用户</el-button>
      <ImportDialog entity-type="user" title="导入用户" @done="load" />
    </div>
    <el-table :data="list">
      <el-table-column prop="account" label="账号" />
      <el-table-column prop="name" label="姓名" />
      <el-table-column prop="phone" label="手机号" />
      <el-table-column prop="wechatId" label="微信号" />
      <el-table-column label="角色">
        <template #default="{ row }">
          <el-tag v-if="row.role === 1" type="primary">管理员</el-tag>
          <el-tag v-else-if="row.role === 3" type="warning">班组长</el-tag>
          <el-tag v-else type="info">生产人员</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dlg" :title="editingId ? '编辑用户' : '创建用户'" width="480px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="账号"><el-input v-model="form.account" /></el-form-item>
        <el-form-item label="手机号"><el-input v-model="form.phone" /></el-form-item>
        <el-form-item label="微信号"><el-input v-model="form.wechatId" placeholder="选填" /></el-form-item>
        <el-form-item label="姓名"><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="角色">
          <el-select v-model="form.role" style="width:100%">
            <el-option :value="1" label="管理员（电脑全权限）" />
            <el-option :value="3" label="班组长（复核 + 报工）" />
            <el-option :value="2" label="生产人员（仅手机报工）" />
          </el-select>
        </el-form-item>
        <el-form-item :label="editingId ? '新密码' : '初始密码'">
          <el-input v-model="form.password" :placeholder="editingId ? '不填则保持原密码' : '至少 6 位，无其它限制'" />
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
import { getUserList, createUser, updateUser, deleteUser } from '@/api/sys'
import ImportDialog from '@/components/ImportDialog.vue'
import { ElMessage, ElMessageBox } from 'element-plus'

const list = ref([]), keyword = ref(''), dlg = ref(false), editingId = ref(null)
const emptyForm = () => ({ account: '', phone: '', wechatId: '', name: '', role: 1, password: '' })
const form = ref(emptyForm())

onMounted(load)

async function load() {
  const res = await getUserList({ keyword: keyword.value, page: 1, pageSize: 100 })
  list.value = res.data.list || []
}

function openCreate() {
  editingId.value = null
  form.value = { ...emptyForm(), password: 'Admin123' }
  dlg.value = true
}

function openEdit(row) {
  editingId.value = row.id
  form.value = {
    account: row.account,
    phone: row.phone,
    wechatId: row.wechatId || '',
    name: row.name,
    role: row.role,
    password: ''
  }
  dlg.value = true
}

async function onSave() {
  if (form.value.password && form.value.password.length < 6) {
    ElMessage.error('密码至少 6 位')
    return
  }
  if (editingId.value) await updateUser(editingId.value, form.value)
  else await createUser(form.value)
  ElMessage.success('保存成功')
  dlg.value = false
  await load()
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除用户 ${row.name}？`, '提示', { type: 'warning' })
  await deleteUser(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<style scoped>.toolbar{display:flex;gap:8px;margin-bottom:12px}</style>
