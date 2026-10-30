<template>
  <div>
    <div class="toolbar">
      <el-input v-model="keyword" placeholder="搜索姓名、登录账号或微信号，回车即可筛选" clearable style="width:220px" @keyup.enter="load" />
      <el-select v-model="userTypeFilter" clearable placeholder="人员类型" style="width:140px" @change="load">
        <el-option v-for="o in USER_TYPE_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
      </el-select>
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" @click="openCreate">+ 创建用户</el-button>
      <ImportDialog entity-type="user" title="导入用户" @done="load" />
    </div>
    <mes-table table-key="views/user/index:1" :data="list">
      <el-table-column prop="account" label="账号" />
      <el-table-column prop="name" label="姓名" />
      <el-table-column prop="phone" label="手机号" />
      <el-table-column prop="wechatId" label="微信号" />
      <el-table-column prop="wecomUserId" label="企微用户 ID" min-width="120" show-overflow-tooltip />
      <el-table-column label="所属部门" min-width="140" show-overflow-tooltip>
        <template #default="{ row }">{{ row.deptNames || '—' }}</template>
      </el-table-column>
      <el-table-column label="人员类型" width="110">
        <template #default="{ row }">
          <el-tag :type="userTypeTagType(row.userType)" size="small">{{ userTypeLabel(row.userType) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="工价倍数" width="90">
        <template #default="{ row }">{{ formatCoeff(row.wageCoeff) }}</template>
      </el-table-column>
      <el-table-column label="角色">
        <template #default="{ row }">
          <el-tag v-if="row.role === 1" type="primary">管理员</el-tag>
          <el-tag v-else-if="row.role === 3" type="warning">班组长</el-tag>
          <el-tag v-else type="info">生产人员</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag v-if="row.status === 1" type="success" size="small">启用</el-tag>
          <el-tag v-else type="info" size="small">停用</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="280">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link type="primary" @click="openQr(row)">二维码</el-button>
          <el-button
            link
            :type="row.status === 1 ? 'warning' : 'success'"
            :disabled="row.status === 1 && isSelf(row)"
            @click="onToggleStatus(row)"
          >{{ row.status === 1 ? '停用' : '启用' }}</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </mes-table>

    <el-dialog v-model="dlg" :title="editingId ? '编辑用户' : '创建用户'" width="480px">
      <el-form :model="form" label-width="110px">
        <el-form-item label="账号"><el-input v-model="form.account" /></el-form-item>
        <el-form-item label="手机号"><el-input v-model="form.phone" /></el-form-item>
        <el-form-item label="微信号"><el-input v-model="form.wechatId" placeholder="选填，用于微信消息通知，用于微信消息通知" /></el-form-item>
        <el-form-item label="企微用户 ID">
          <el-input v-model="form.wecomUserId" maxlength="64" placeholder="选填，通讯录账号，如 HuZhongQian" clearable />
          <div class="form-hint">派工通知只 @ 这个账号。留空则只发群，不 @。</div>
        </el-form-item>
        <el-form-item label="姓名"><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="所属部门">
          <el-select v-model="form.deptIds" multiple filterable style="width:100%" placeholder="可多选部门，选中后该用户的可报工范围与这些部门关联">
            <el-option v-for="d in deptOptions" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="form.role" style="width:100%">
            <el-option :value="1" label="管理员（电脑全部功能）" />
            <el-option :value="3" label="班组长（复核 + 报工）" />
            <el-option :value="2" label="生产人员（仅手机报工）" />
          </el-select>
        </el-form-item>
        <el-form-item label="人员类型">
          <el-select v-model="form.userType" style="width:100%" @change="onUserTypeChange">
            <el-option v-for="o in USER_TYPE_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
          <div v-if="form.userType === 2" class="form-hint">外部供应商不能登录，由管理员在报工页代报工</div>
        </el-form-item>
        <el-form-item label="工价倍数">
          <el-input-number v-model="form.wageCoeff" :min="0.1" :max="9.99" :step="0.1" :precision="2" style="width:160px" />
          <div class="form-hint">1 为原价，计件、计时工资按此倍数相乘；月薪和不良扣款不乘；选「临时用工」且当前倍数为 1 时，自动带出 0.8，可自行修改</div>
        </el-form-item>
        <el-form-item :label="editingId ? '新密码' : '初始密码'">
          <el-input v-model="form.password" :placeholder="editingId ? '不填则保持原密码' : '至少 6 位，例如 abc123，无其它限制'" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg=false">取消</el-button>
        <el-button type="primary" @click="onSave">确定</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="qrDlg" title="个人免密二维码" width="420px" @closed="onQrClosed">
      <div v-loading="qrLoading" class="qr-wrap">
        <p v-if="qrInfo?.status === 0" class="warn">该用户已停用，扫码将无法登录；如需恢复，请在用户列表点击启用</p>
        <div id="user-login-qr-sheet" class="qr-sheet">
          <div class="qr-meta">
            <div class="qr-name">{{ qrInfo?.name || '—' }}</div>
            <div class="qr-account">账号：{{ qrInfo?.account || '—' }}</div>
            <div class="qr-tip">扫码免密登录（手机打开后长期记住）</div>
          </div>
          <img v-if="qrDataUrl" class="qr-img" :src="qrDataUrl" alt="个人免密二维码" />
          <div v-if="loginUrl" class="qr-url" :title="loginUrl">{{ loginUrl }}</div>
        </div>
        <p class="print-hint">可复制链接发企微/微信；打印时请勾选「背景图形」，码边长建议 ≥5cm。</p>
      </div>
      <template #footer>
        <el-button :disabled="!qrInfo?.code" @click="onRegenerate">重新生成</el-button>
        <el-button :disabled="!loginUrl" @click="onCopyLink">复制链接</el-button>
        <el-button :disabled="!qrDataUrl" @click="onExportPng">导出 PNG</el-button>
        <el-button type="primary" :disabled="!qrDataUrl" @click="onPrintQr">打印</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import QRCode from 'qrcode'
import {
  getUserList, createUser, updateUser, deleteUser, setUserStatus,
  getUserLoginQr, regenerateUserLoginQr, getDeptList
} from '@/api/sys'
import ImportDialog from '@/components/ImportDialog.vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { USER_TYPE_OPTIONS, userTypeLabel, userTypeTagType } from '@/constants/userType'

const list = ref([]), keyword = ref(''), userTypeFilter = ref(null), dlg = ref(false), editingId = ref(null)
const emptyForm = () => ({ account: '', phone: '', wechatId: '', wecomUserId: '', name: '', role: 1, userType: 0, wageCoeff: 1, password: '', deptIds: [] })
const form = ref(emptyForm())
const deptOptions = ref([])
const currentUserId = Number(localStorage.getItem('userId') || 0)

function isSelf(row) {
  return !!currentUserId && Number(row.id) === currentUserId
}

const qrDlg = ref(false)
const qrLoading = ref(false)
const qrInfo = ref(null)
const qrDataUrl = ref('')
const loginUrl = ref('')

onMounted(async () => {
  await load()
  const res = await getDeptList({ page: 1, pageSize: 200 })
  deptOptions.value = res.data.list || []
})

async function load() {
  const params = { keyword: keyword.value, page: 1, pageSize: 100 }
  if (userTypeFilter.value != null) params.userType = userTypeFilter.value
  const res = await getUserList(params)
  list.value = res.data.list || []
}

function formatCoeff(v) {
  const n = Number(v)
  return Number.isFinite(n) ? n.toFixed(2) : '1.00'
}

function onUserTypeChange(val) {
  if (val === 1 && Number(form.value.wageCoeff) === 1)
    form.value.wageCoeff = 0.8
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
    wecomUserId: row.wecomUserId || '',
    name: row.name,
    role: row.role,
    userType: row.userType ?? 0,
    wageCoeff: row.wageCoeff ?? 1,
    password: '',
    deptIds: [...(row.deptIds || [])]
  }
  dlg.value = true
}

async function onSave() {
  const coeff = Number(form.value.wageCoeff)
  if (!Number.isFinite(coeff) || coeff < 0.1 || coeff > 9.99) {
    ElMessage.error('工价倍数须在 0.10 到 9.99 之间，请在工价倍数输入框修改后重新保存')
    return
  }
  form.value.wageCoeff = Math.round(coeff * 100) / 100
  if (form.value.password && form.value.password.length < 6) {
    ElMessage.error('密码至少需要 6 位，请在密码输入框重新填写')
    return
  }
  if (editingId.value) await updateUser(editingId.value, form.value)
  else await createUser(form.value)
  ElMessage.success('保存成功')
  dlg.value = false
  await load()
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确定要删除用户「${row.name}」吗？若已有报工、开工、工资单或派工记录则不能删除（可改为停用账号）`, '提示', { type: 'warning' })
  await deleteUser(row.id)
  ElMessage.success('已删除')
  await load()
}

async function onToggleStatus(row) {
  const next = row.status === 1 ? 0 : 1
  if (next === 0) {
    if (isSelf(row)) {
      ElMessage.warning('不能停用自己的账号；如需停用，请联系其他管理员在人员管理中操作的账号；如需停用，请联系其他管理员在人员管理中操作')
      return
    }
    await ElMessageBox.confirm(
      `停用后该用户将不能登录、不能报工、不能被派工；历史报工与工资单保留。确定停用「${row.name}」？`,
      '停用确认',
      { type: 'warning' }
    )
  }
  await setUserStatus(row.id, next)
  ElMessage.success(next === 0 ? '已停用' : '已启用')
  await load()
}

function buildLoginUrl(loginPath) {
  const origin = window.location.origin
  const path = (loginPath || '').startsWith('/') ? loginPath : `/${loginPath || ''}`
  return `${origin}${path}`
}

async function renderQr(info) {
  qrInfo.value = info
  const url = buildLoginUrl(info.loginPath)
  loginUrl.value = url
  qrDataUrl.value = await QRCode.toDataURL(url, {
    errorCorrectionLevel: 'M',
    margin: 2,
    width: 360
  })
}

async function openQr(row) {
  qrDlg.value = true
  qrLoading.value = true
  qrDataUrl.value = ''
  loginUrl.value = ''
  qrInfo.value = null
  try {
    const res = await getUserLoginQr(row.id)
    await renderQr(res.data)
  } catch (e) {
    ElMessage.error(e?.message || '个人二维码加载失败，请检查网络连接或刷新页面重试；持续报错请联系管理员')
    qrDlg.value = false
  } finally {
    qrLoading.value = false
  }
}

function onQrClosed() {
  qrInfo.value = null
  qrDataUrl.value = ''
  loginUrl.value = ''
}

async function onCopyLink() {
  const text = loginUrl.value
  if (!text) return
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制免密登录链接，可发给工人')
  } catch {
    ElMessage.error('复制失败，请手动选中上方链接复制')
  }
}

async function onRegenerate() {
  if (!qrInfo.value?.userId) return
  await ElMessageBox.confirm(
    '重新生成后，旧二维码立刻失效，已贴工牌/存手机的需替换。确定？',
    '重新生成',
    { type: 'warning' }
  )
  qrLoading.value = true
  try {
    const res = await regenerateUserLoginQr(qrInfo.value.userId)
    await renderQr(res.data)
    ElMessage.success('已重新生成，旧码已失效')
  } finally {
    qrLoading.value = false
  }
}

function onExportPng() {
  if (!qrDataUrl.value) return
  const name = (qrInfo.value?.name || 'user').replace(/[\\/:*?"<>|]/g, '_')
  const a = document.createElement('a')
  a.href = qrDataUrl.value
  a.download = `${name}-login-qr.png`
  a.click()
}

function onPrintQr() {
  if (!qrDataUrl.value || !qrInfo.value) return
  const name = qrInfo.value.name || ''
  const account = qrInfo.value.account || ''
  const win = window.open('', '_blank')
  if (!win) {
    ElMessage.error('浏览器拦截了打印窗口。请在地址栏右侧找到被拦截的弹窗提示，选择「始终允许」后重试。')
    return
  }
  win.document.write(`<!DOCTYPE html><html><head><title>个人免密二维码</title>
<meta charset="utf-8"/>
<style>
@page { margin: 8mm; }
body { font-family: sans-serif; margin: 0; padding: 8mm; }
.sheet { width: 80mm; border: 1px solid #333; padding: 4mm; box-sizing: border-box; }
.name { font-size: 18px; font-weight: 700; margin-bottom: 2mm; }
.line { font-size: 12px; color: #333; margin-bottom: 1mm; }
.tip { font-size: 11px; color: #666; margin: 2mm 0 3mm; }
.qr { text-align: center; }
.qr img { width: 50mm; height: 50mm; }
.hint { margin-top: 4mm; font-size: 11px; color: #888; }
</style></head><body>
<div class="sheet">
  <div class="name">${escapeHtml(name)}</div>
  <div class="line">账号：${escapeHtml(account)}</div>
  <div class="tip">扫码免密登录</div>
  <div class="qr"><img src="${qrDataUrl.value}" alt="QR"/></div>
</div>
<p class="hint">打印时请勾选「背景图形」；码边长 ≥5cm。</p>
</body></html>`)
  win.document.close()
  win.focus()
  setTimeout(() => {
    win.print()
    win.close()
  }, 300)
}

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap }
.form-hint { font-size: 12px; color: #909399; line-height: 1.4; margin-top: 4px }
.qr-wrap { min-height: 200px }
.qr-sheet {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  padding: 12px;
  border: 1px solid #e5e5e5;
}
.qr-name { font-size: 18px; font-weight: 600; text-align: center }
.qr-account, .qr-tip { font-size: 13px; color: #666; text-align: center }
.qr-img { width: 220px; height: 220px }
.qr-url {
  max-width: 100%;
  font-size: 12px;
  color: #666;
  word-break: break-all;
  text-align: center;
  user-select: all;
}
.print-hint { margin: 12px 0 0; font-size: 12px; color: #888 }
.warn { color: #e6a23c; margin: 0 0 10px; font-size: 13px }
</style>
