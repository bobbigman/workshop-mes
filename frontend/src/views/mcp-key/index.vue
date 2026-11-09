<template>
  <div class="page">
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>MCP 连接报文（每厂一把）</template>
      生成后整段发给对接人即可；报文保存在本系统。点「复制报文」须再输当前账号密码，防止旁人顺手点开。重新生成会使旧钥匙失效，对接方必须改用新报文
    </el-alert>

    <el-card v-loading="loading">
      <template #header>
        <div class="card-head">
          <span>本厂 MCP 钥匙</span>
          <div class="card-actions">
            <el-button
              v-if="status.hasActive && status.canReveal"
              type="primary"
              :loading="revealing"
              @click="onReveal"
            >复制报文</el-button>
            <el-button
              type="primary"
              :plain="status.hasActive"
              :loading="creating"
              :disabled="loading"
              @click="onCreate"
            >{{ status.hasActive ? '重新生成' : '生成报文' }}</el-button>
            <el-button
              v-if="status.hasActive"
              type="danger"
              plain
              :loading="revoking"
              @click="onRevoke"
            >吊销</el-button>
          </div>
        </div>
      </template>

      <template v-if="!status.hasActive">
        <el-empty :image-size="72">
          <template #description>
            <div class="empty-desc">
              <div>当前厂「{{ factoryName || '本厂' }}」还没有有效 MCP 钥匙</div>
              <div class="empty-hint">以前若在别的厂生成过，请先切换工厂再进本页；本页只看当前厂，不会显示别厂历史。点「生成报文」会给本厂新建一把，之后即可随时再复制。</div>
            </div>
          </template>
        </el-empty>
      </template>
      <el-descriptions v-else :column="1" border>
        <el-descriptions-item label="状态">
          <el-tag type="success">有效</el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="有效期">{{ status.expireAt || '长期' }}</el-descriptions-item>
        <el-descriptions-item label="生成时间">{{ status.createdAt || '—' }}</el-descriptions-item>
        <el-descriptions-item v-if="!status.canReveal" label="说明">
          <span class="warn">这是升级前生成的旧钥匙，库里没有明文，无法再查看。请点「重新生成」一次，之后即可随时复制（对接方需改用新报文）。</span>
        </el-descriptions-item>
      </el-descriptions>
    </el-card>

    <el-dialog
      v-model="plainDlg"
      title="连接报文"
      width="560px"
      :close-on-click-modal="false"
      destroy-on-close
      @close="payload = null"
    >
      <el-alert type="info" :closable="false" show-icon style="margin-bottom:12px">
        {{ payload?.tip || '可整段复制；关闭后仍可随时再打开复制' }}
      </el-alert>
      <el-input type="textarea" :model-value="payload?.connectionText" :rows="14" readonly />
      <template #footer>
        <el-button @click="copyConnection">复制整段</el-button>
        <el-button type="primary" @click="plainDlg = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getMcpKeyStatus, getMcpKeyConnection, createMcpKey, revokeMcpKey } from '@/api/mcpKey'

const loading = ref(false)
const creating = ref(false)
const revealing = ref(false)
const revoking = ref(false)
const factoryName = ref(localStorage.getItem('factoryName') || '')
const status = reactive({
  hasActive: false,
  keyId: null,
  keyAlias: '',
  createdAt: null,
  expireAt: null,
  canReveal: false
})

const plainDlg = ref(false)
const payload = ref(null)

onMounted(() => load())

async function load() {
  loading.value = true
  try {
    const res = await getMcpKeyStatus()
    const d = res.data || {}
    status.hasActive = d.hasActive === true
    status.keyId = d.keyId ?? null
    status.keyAlias = d.keyAlias || ''
    status.createdAt = d.createdAt || null
    status.expireAt = d.expireAt || null
    status.canReveal = d.canReveal === true
  } finally {
    loading.value = false
  }
}

async function askLoginPassword(title) {
  try {
    const { value } = await ElMessageBox.prompt(
      '请输入当前登录账号的密码，确认是本人在操作。',
      title,
      {
        inputType: 'password',
        inputPlaceholder: '登录密码',
        confirmButtonText: '确认',
        cancelButtonText: '取消',
        inputValidator: (v) => (v && String(v).length > 0 ? true : '请输入密码')
      }
    )
    return String(value || '')
  } catch (action) {
    if (action === 'cancel' || action === 'close') return null
    throw action
  }
}

async function onReveal() {
  if (revealing.value) return
  const password = await askLoginPassword('复制报文前验证')
  if (password == null) return
  revealing.value = true
  try {
    const res = await getMcpKeyConnection(password)
    payload.value = { ...res.data, tip: '可整段复制；关闭后再次打开仍须输入密码' }
    plainDlg.value = true
  } finally {
    revealing.value = false
  }
}

async function onCreate() {
  if (creating.value || loading.value) return
  creating.value = true
  try {
    if (status.hasActive) {
      const confirmed = await ElMessageBox.confirm(
        '继续生成将使当前钥匙失效，对接方需要改用新报文才能重新连接。确认要继续生成吗？',
        '替换连接报文',
        { type: 'warning', confirmButtonText: '确认替换', cancelButtonText: '取消' }
      ).then(() => true, action => {
        if (action === 'cancel' || action === 'close') return false
        throw action
      })
      if (!confirmed) return
    }
    const res = await createMcpKey()
    payload.value = res.data
    plainDlg.value = true
    if (res.data?.revokedOld) {
      ElMessage.warning('旧钥匙已吊销，请把新生成的报文发给对接方，对方更新后即可重新连接')
    } else {
      ElMessage.success('已生成，可随时再复制')
    }
    await load()
  } finally {
    creating.value = false
  }
}

async function copyConnection() {
  const text = payload.value?.connectionText || ''
  if (!text) return
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制整段报文')
  } catch {
    ElMessage.error('自动复制失败，请手动选中文本框中的全部内容后按 Ctrl+C 复制')
  }
}

async function onRevoke() {
  await ElMessageBox.confirm(
    '吊销后对接方将立即断开连接，需要重新生成报文才能恢复。确认要吊销吗？',
    '吊销钥匙',
    { type: 'warning' }
  )
  revoking.value = true
  try {
    await revokeMcpKey()
    ElMessage.success('已吊销')
    await load()
  } finally {
    revoking.value = false
  }
}
</script>

<style scoped>
.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.card-actions { display: flex; gap: 8px; flex-wrap: wrap; }
.warn { color: #e6a23c; line-height: 1.5; }
.empty-desc { line-height: 1.6; color: #606266; }
.empty-hint { margin-top: 8px; font-size: 13px; color: #909399; max-width: 420px; margin-left: auto; margin-right: auto; }
</style>
