<template>
  <div>
    <div class="toolbar">
      <el-select v-model="filter.productId" clearable filterable placeholder="产品" style="width:200px">
        <el-option v-for="p in products" :key="p.id" :label="`${p.code} ${p.name}`" :value="p.id" />
      </el-select>
      <el-select v-model="filter.operationId" clearable filterable placeholder="工序" style="width:180px">
        <el-option v-for="o in operations" :key="o.id" :label="`${o.code} ${o.name}`" :value="o.id" />
      </el-select>
      <el-select v-model="filter.departmentId" clearable filterable placeholder="部门" style="width:140px">
        <el-option v-for="d in depts" :key="d.id" :label="d.name" :value="d.id" />
      </el-select>
      <el-select v-model="filter.userId" clearable filterable placeholder="人员" style="width:140px">
        <el-option v-for="u in users" :key="u.id" :label="u.name" :value="u.id" />
      </el-select>
      <el-button type="primary" @click="load">查询</el-button>
      <el-button @click="load">刷新</el-button>
      <el-button type="success" @click="openCreate">+ 新增工价</el-button>
      <ImportDialog entity-type="priceRule" title="导入工价" @done="load" />
    </div>
    <el-table :data="list" :row-class-name="rowClassName">
      <el-table-column label="产品" min-width="120">
        <template #default="{ row }">{{ row.productName || '不限' }}</template>
      </el-table-column>
      <el-table-column label="工序" min-width="100">
        <template #default="{ row }">{{ row.operationName || '不限' }}</template>
      </el-table-column>
      <el-table-column label="部门" width="100">
        <template #default="{ row }">{{ row.departmentName || '不限' }}</template>
      </el-table-column>
      <el-table-column label="人员" width="90">
        <template #default="{ row }">{{ row.userName || '不限' }}</template>
      </el-table-column>
      <el-table-column label="类型" width="80">
        <template #default="{ row }">{{ priceTypeLabel(row.priceType) }}</template>
      </el-table-column>
      <el-table-column prop="unitPrice" label="单价" width="90" />
      <el-table-column label="扣款单价" width="90">
        <template #default="{ row }">{{ row.deductPrice ?? '-' }}</template>
      </el-table-column>
      <el-table-column label="生效起" width="110">
        <template #default="{ row }">{{ fmtDate(row.effectiveFrom) }}</template>
      </el-table-column>
      <el-table-column label="生效止" width="110">
        <template #default="{ row }">{{ row.effectiveTo ? fmtDate(row.effectiveTo) : '长期' }}</template>
      </el-table-column>
      <el-table-column prop="priority" label="优先级" width="70" />
      <el-table-column label="操作" width="140" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dlg" :title="editingId ? '编辑工价' : '新增工价'" width="520px">
      <el-form :model="form" label-width="100px">
        <el-form-item label="产品">
          <el-select v-model="form.productId" clearable filterable style="width:100%" placeholder="不限">
            <el-option v-for="p in products" :key="p.id" :label="`${p.code} ${p.name}`" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="工序">
          <el-select v-model="form.operationId" clearable filterable style="width:100%" placeholder="不限">
            <el-option v-for="o in operations" :key="o.id" :label="`${o.code} ${o.name}`" :value="o.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="部门">
          <el-select v-model="form.departmentId" clearable filterable style="width:100%" placeholder="不限">
            <el-option v-for="d in depts" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="人员">
          <el-select v-model="form.userId" clearable filterable style="width:100%" placeholder="不限">
            <el-option v-for="u in users" :key="u.id" :label="u.name" :value="u.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="计价方式" required>
          <el-select v-model="form.priceType" style="width:100%">
            <el-option :value="1" label="计件（元/件）" />
            <el-option :value="2" label="计时（元/小时）" />
            <el-option :value="3" label="固定（单笔）" />
          </el-select>
        </el-form-item>
        <el-form-item label="单价" required>
          <el-input-number v-model="form.unitPrice" :min="0.01" :precision="4" :step="0.01" style="width:100%" />
        </el-form-item>
        <el-form-item label="不良扣款">
          <el-input-number v-model="form.deductPrice" :min="0" :precision="4" :step="0.01" style="width:100%" />
        </el-form-item>
        <el-form-item label="生效时间" required>
          <el-date-picker v-model="form.effectiveFrom" type="datetime" style="width:100%" />
        </el-form-item>
        <el-form-item label="失效时间">
          <el-date-picker v-model="form.effectiveTo" type="datetime" style="width:100%" clearable />
        </el-form-item>
        <el-form-item label="优先级">
          <el-input-number v-model="form.priority" :min="0" style="width:100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg = false">取消</el-button>
        <el-button type="primary" @click="onSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getPriceRuleList, createPriceRule, updatePriceRule, deletePriceRule } from '@/api/priceRule'
import { getProductList, getOperationList } from '@/api/base'
import { getDeptList, getUserList } from '@/api/sys'
import { ElMessage, ElMessageBox } from 'element-plus'
import ImportDialog from '@/components/ImportDialog.vue'

const list = ref([])
const products = ref([])
const operations = ref([])
const depts = ref([])
const users = ref([])
const filter = ref({ productId: null, operationId: null, departmentId: null, userId: null })
const dlg = ref(false)
const editingId = ref(null)
const form = ref(emptyForm())

onMounted(async () => {
  const [pr, op, dep, us] = await Promise.all([
    getProductList({ page: 1, pageSize: 500 }),
    getOperationList({ page: 1, pageSize: 500 }),
    getDeptList({ page: 1, pageSize: 500 }),
    getUserList({ page: 1, pageSize: 500 })
  ])
  products.value = pr.data?.list || []
  operations.value = op.data?.list || []
  depts.value = dep.data?.list || []
  users.value = us.data?.list || []
  load()
})

function emptyForm() {
  return {
    productId: null, operationId: null, departmentId: null, userId: null,
    priceType: 1, unitPrice: 0, deductPrice: null,
    effectiveFrom: new Date(), effectiveTo: null, priority: 0
  }
}

function priceTypeLabel(t) {
  return ({ 1: '计件', 2: '计时', 3: '固定' })[t] || t
}
function fmtDate(d) {
  if (!d) return ''
  const x = new Date(d)
  return `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`
}

function rowClassName({ row }) {
  if (!row.effectiveTo) return ''
  if (new Date(row.effectiveTo) < new Date()) return 'row-expired'
  return ''
}

async function load() {
  const params = { page: 1, pageSize: 200 }
  if (filter.value.productId) params.productId = filter.value.productId
  if (filter.value.operationId) params.operationId = filter.value.operationId
  if (filter.value.departmentId) params.departmentId = filter.value.departmentId
  if (filter.value.userId) params.userId = filter.value.userId
  const res = await getPriceRuleList(params)
  list.value = res.data?.list || []
}

function openCreate() {
  editingId.value = null
  form.value = emptyForm()
  dlg.value = true
}

function openEdit(row) {
  editingId.value = row.id
  form.value = {
    productId: row.productId, operationId: row.operationId,
    departmentId: row.departmentId, userId: row.userId,
    priceType: row.priceType, unitPrice: row.unitPrice,
    deductPrice: row.deductPrice ?? null,
    effectiveFrom: row.effectiveFrom ? new Date(row.effectiveFrom) : new Date(),
    effectiveTo: row.effectiveTo ? new Date(row.effectiveTo) : null,
    priority: row.priority
  }
  dlg.value = true
}

async function onSave() {
  if (form.value.unitPrice == null || form.value.unitPrice <= 0) {
    ElMessage.warning('请填写单价（须大于0）')
    return
  }
  const payload = {
    ...form.value,
    effectiveFrom: form.value.effectiveFrom,
    effectiveTo: form.value.effectiveTo || null,
    deductPrice: form.value.deductPrice === null || form.value.deductPrice === undefined
      ? null : form.value.deductPrice
  }
  if (editingId.value) await updatePriceRule(editingId.value, payload)
  else await createPriceRule(payload)
  ElMessage.success('已保存')
  dlg.value = false
  load()
}

async function onDelete(row) {
  await ElMessageBox.confirm(`删除该工价规则？`, '确认')
  await deletePriceRule(row.id)
  ElMessage.success('已删除')
  load()
}
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; align-items: center; }
:deep(.row-expired) {
  --el-table-tr-bg-color: #f5f5f5;
  color: #909399;
}
</style>
