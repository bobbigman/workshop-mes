<template>
  <div class="page">
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>MCP 钥匙（每厂一把）</template>
      生成后明文只显示一次，请立刻复制发给客户。吊销后该钥匙立即失效。库里只存哈希，看不到明文。
    </el-alert>

    <el-card>
      <template #header>
        <div class="card-head">
          <span>钥匙列表</span>
          <div class="actions">
            <el-select v-model="filterFactoryId" clearable placeholder="全部工厂" style="width:200px;margin-right:8px" @change="load">
              <el-option v-for="f in factories" :key="f.id" :label="f.factoryName" :value="f.id" />
            </el-select>
            <el-button type="primary" @click="openCreate">生成钥匙</el-button>
          </div>
        </div>
      </template>

      <el-table :data="list" v-loading="loading" stripe>
        <el-table-column prop="factoryName" label="工厂" min-width="120" />
        <el-table-column prop="keyAlias" label="别名" min-width="140" />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'info'">{{ row.status === 1 ? '有效' : '已吊销' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="有效期" width="170">
          <template #default="{ row }">{{ row.expireAt || '长期' }}</template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" width="170" />
        <el-table-column prop="remark" label="备注" min-width="120" show-overflow-tooltip />
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
      </el-table>
    </el-card>

    <el-dialog v-model="createDlg" title="生成 MCP 钥匙" width="480px" @closed="onCreateClosed">
      <el-form label-width="88px">
        <el-form-item label="工厂" required>
          <el-select v-model="form.factoryId" placeholder="请选择工厂" style="width:100%">
            <el-option v-for="f in factories" :key="f.id" :label="f.factoryName" :value="f.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="别名">
          <el-input v-model="form.keyAlias" placeholder="如：五金厂-老板专用" maxlength="64" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="form.remark" placeholder="可选" maxlength="255" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDlg = false">取消</el-button>
        <el-button type="primary" :loading="creating" @click="onCreate">生成</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="plainDlg" title="请立即复制钥匙" width="560px" :close-on-click-modal="false">
      <el-alert type="warning" :closable="false" show-icon style="margin-bottom:12px">
        关闭后不再显示明文。请复制发给客户配置到 MCP 的 Authorization: Bearer …
      </el-alert>
      <p><strong>别名：</strong>{{ created?.keyAlias }}</p>
      <el-input :model-value="created?.apiKey" readonly>
        <template #append>
          <el-button @click="copyKey">复制</el-button>
        </template>
      </el-input>
      <template #footer>
        <el-button type="primary" @click="plainDlg = false">我已复制</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getMcpKeyFactories, getMcpKeyList, createMcpKey, revokeMcpKey } from '@/api/mcpKey'

const factories = ref([])
const list = ref([])
const loading = ref(false)
const filterFactoryId = ref(null)

const createDlg = ref(false)
const creating = ref(false)
const form = ref({ factoryId: null, keyAlias: '', remark: '' })

const plainDlg = ref(false)
const created = ref(null)

onMounted(async () => {
  await loadFactories()
  await load()
})

async function loadFactories() {
  const res = await getMcpKeyFactories()
  factories.value = res.data || []
}

async function load() {
  loading.value = true
  try {
    const params = {}
    if (filterFactoryId.value) params.factoryId = filterFactoryId.value
    const res = await getMcpKeyList(params)
    list.value = res.data || []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  form.value = {
    factoryId: filterFactoryId.value || (factories.value[0]?.id ?? null),
    keyAlias: '',
    remark: ''
  }
  createDlg.value = true
}

function onCreateClosed() {
  form.value = { factoryId: null, keyAlias: '', remark: '' }
}

async function onCreate() {
  if (!form.value.factoryId) {
    ElMessage.warning('请先选择工厂')
    return
  }
  creating.value = true
  try {
    const res = await createMcpKey({
      factoryId: form.value.factoryId,
      keyAlias: form.value.keyAlias || undefined,
      remark: form.value.remark || undefined
    })
    created.value = res.data
    createDlg.value = false
    plainDlg.value = true
    await load()
  } finally {
    creating.value = false
  }
}

async function copyKey() {
  const text = created.value?.apiKey || ''
  if (!text) return
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制')
  } catch {
    ElMessage.error('复制失败，请手动选中复制')
  }
}

async function onRevoke(row) {
  await ElMessageBox.confirm(
    `吊销后客户立刻连不上（别名：${row.keyAlias}），确认吗？`,
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
.actions {
  display: flex;
  align-items: center;
}
.muted {
  color: #999;
}
</style>
