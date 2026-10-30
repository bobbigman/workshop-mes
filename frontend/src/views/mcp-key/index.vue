<template>
  <div class="page">
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>MCP 连接报文（每厂一把）</template>
      点「生成报文」得到完整 JSON 连接配置。客户端支持导入此格式时可整段粘贴；也可按客户端要求填写地址和请求头。明文只显示一次，请立刻复制发给对接人。吊销后该钥匙立即失效。
    </el-alert>

    <el-card>
      <template #header>
        <div class="card-head">
          <span>钥匙列表</span>
          <el-button type="primary" :loading="creating" :disabled="loading" @click="onCreate">生成报文</el-button>
        </div>
      </template>

      <mes-table table-key="views/mcp-key/index:1" :data="list" v-loading="loading" stripe>
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'info'">{{ row.status === 1 ? '有效' : '已吊销' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="有效期" width="170">
          <template #default="{ row }">{{ row.expireAt || '长期' }}</template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" width="170" />
        <el-table-column label="操作" width="100" fixed="right">
          <template #default="{ row }">
            <el-button
              v-if="row.status === 1"
              type="danger"
              link
              @click="onRevoke(row)"
            >吊销</el-button>
            <span v-else class="muted">—</span>
          </template>
        </el-table-column>
      </mes-table>
    </el-card>

    <el-dialog v-model="plainDlg" title="请立即复制完整报文" width="560px" :close-on-click-modal="false" destroy-on-close @close="created = null">
      <el-alert type="warning" :closable="false" show-icon style="margin-bottom:12px">
        {{ created?.tip || '请立即复制发给客户，关闭后不再显示' }}
      </el-alert>
      <el-input
        type="textarea"
        :model-value="created?.connectionText"
        :rows="14"
        readonly
      />
      <template #footer>
        <el-button @click="copyConnection">复制整段</el-button>
        <el-button type="primary" @click="plainDlg = false">我已复制</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getMcpKeyList, createMcpKey, revokeMcpKey } from '@/api/mcpKey'

const list = ref([])
const loading = ref(false)
const creating = ref(false)

const plainDlg = ref(false)
const created = ref(null)

onMounted(() => load())

async function load() {
  loading.value = true
  try {
    const res = await getMcpKeyList()
    list.value = res.data || []
  } finally {
    loading.value = false
  }
}

async function onCreate() {
  if (creating.value || loading.value) return
  creating.value = true
  try {
    if (list.value.some(row => row.status === 1)) {
      const confirmed = await ElMessageBox.confirm(
        '继续生成将使旧连接失效，客户需改用新报文，确认继续吗？',
        '替换连接报文',
        { type: 'warning', confirmButtonText: '确认替换', cancelButtonText: '取消' }
      ).then(() => true, action => {
        if (action === 'cancel' || action === 'close') return false
        throw action
      })
      if (!confirmed) return
    }
    const res = await createMcpKey()
    created.value = res.data
    plainDlg.value = true
    if (res.data?.revokedOld) {
      ElMessage.warning('已吊销旧钥匙，客户需改用新报文')
    }
    await load()
  } finally {
    creating.value = false
  }
}

async function copyConnection() {
  const text = created.value?.connectionText || ''
  if (!text) return
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制整段报文')
  } catch {
    ElMessage.error('复制失败，请手动选中复制')
  }
}

async function onRevoke(row) {
  await ElMessageBox.confirm(
    '吊销后客户立即连不上，确认吗？',
    '吊销钥匙',
    { type: 'warning' }
  )
  await revokeMcpKey(row.keyId)
  ElMessage.success('已吊销')
  await load()
}
</script>

<style scoped>
.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.muted {
  color: #999;
}
</style>
