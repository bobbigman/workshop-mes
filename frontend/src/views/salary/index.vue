<template>

  <div>

    <ValueTip :text="VALUE_TIPS.salary" />

    <div class="toolbar">

      <el-select v-model="q.periodType" style="width:100px">

        <el-option :value="1" label="按月" />

        <el-option :value="2" label="按周" />

      </el-select>

      <el-input v-model="q.periodValue" :placeholder="q.periodType === 1 ? 'yyyy-MM' : 'yyyy-Www'" style="width:130px" />

      <el-select v-model="q.status" clearable placeholder="状态" style="width:110px">

        <el-option :value="0" label="草稿" />

        <el-option :value="1" label="已确认" />

      </el-select>

      <el-button type="primary" @click="load">查询</el-button>

      <el-button type="success" @click="openGen">生成工资单</el-button>

      <el-button @click="onExport">导出 CSV</el-button>

    </div>



    <el-table :data="list">

      <el-table-column prop="userName" label="人员" width="90" fixed />

      <el-table-column prop="periodValue" label="周期" width="100" />

      <el-table-column prop="baseSalary" label="底薪" width="90" />

      <el-table-column prop="mealAllowance" label="餐补" width="80" />

      <el-table-column prop="otherAllowance" label="其他补贴" width="90" />

      <el-table-column prop="socialTax" label="社保个税" width="90" />

      <el-table-column prop="otherDeduction" label="其他扣款" width="90" />

      <el-table-column prop="totalAmount" label="应发合计" width="100" />

      <el-table-column label="状态" width="80">

        <template #default="{ row }">

          <el-tag v-if="row.status === 0" type="info">草稿</el-tag>

          <el-tag v-else type="success">已确认</el-tag>

        </template>

      </el-table-column>

      <el-table-column prop="createdAt" label="生成时间" width="160" />

      <el-table-column label="操作" width="320" fixed="right">

        <template #default="{ row }">

          <el-button link type="primary" @click="openDetail(row)">明细</el-button>

          <el-button v-if="row.status === 0" link type="warning" @click="onRecalc(row)">重算</el-button>

          <el-button v-if="row.status === 0" link type="success" @click="onConfirm(row)">确认</el-button>

          <el-button v-if="row.status === 0" link type="danger" @click="onDelete(row)">删除</el-button>

          <el-button v-if="row.status === 1" link type="danger" @click="onRevoke(row)">撤回重算</el-button>

        </template>

      </el-table-column>

    </el-table>



    <el-dialog v-model="genDlg" title="生成工资单" width="420px">

      <el-form label-width="90px">

        <el-form-item label="周期类型">

          <el-select v-model="gen.periodType" style="width:100%">

            <el-option :value="1" label="按月" />

            <el-option :value="2" label="按周" />

          </el-select>

        </el-form-item>

        <el-form-item label="周期值" required>

          <el-input v-model="gen.periodValue" :placeholder="gen.periodType === 1 ? '如 2026-09' : '如 2026-W37'" />

        </el-form-item>

        <el-form-item label="人员">

          <el-select v-model="gen.userId" clearable filterable style="width:100%" placeholder="空=全厂有报工人">

            <el-option v-for="u in users" :key="u.id" :label="u.name" :value="u.id" />

          </el-select>

        </el-form-item>

      </el-form>

      <template #footer>

        <el-button @click="genDlg = false">取消</el-button>

        <el-button type="primary" @click="onGenerate">生成</el-button>

      </template>

    </el-dialog>



    <el-drawer v-model="detailDlg" title="工资明细" size="640px">

      <div v-if="detail" class="detail-head">

        <span>{{ detail.userName }}</span>

        <span>{{ detail.periodValue }}</span>

      </div>



      <div v-if="detail" class="summary-grid">

        <div class="summary-item">

          <div class="summary-label">计件合计</div>

          <div class="summary-value">{{ fmtMoney(pieceworkAmount) }}</div>

        </div>

        <div class="summary-item">

          <div class="summary-label">应发补贴小计</div>

          <div class="summary-value">{{ fmtMoney(allowanceSubtotal) }}</div>

        </div>

        <div class="summary-item">

          <div class="summary-label">应扣小计</div>

          <div class="summary-value">{{ fmtMoney(deductionSubtotal) }}</div>

        </div>

        <div class="summary-item summary-total">

          <div class="summary-label">应发合计</div>

          <div class="summary-value">{{ fmtMoney(previewTotal) }}</div>

        </div>

      </div>



      <div v-if="detail && detail.allowManualComponents" class="components-form">

        <el-form label-width="90px" size="small">

          <el-form-item label="底薪">

            <el-input-number

              v-model="components.baseSalary"

              :min="0"

              :precision="2"

              :step="100"

              :disabled="!canEditComponents"

              controls-position="right"

              style="width:100%"

            />

          </el-form-item>

          <el-form-item label="餐补">

            <el-input-number

              v-model="components.mealAllowance"

              :min="0"

              :precision="2"

              :step="10"

              :disabled="!canEditComponents"

              controls-position="right"

              style="width:100%"

            />

          </el-form-item>

          <el-form-item label="其他补贴">

            <el-input-number

              v-model="components.otherAllowance"

              :min="0"

              :precision="2"

              :step="10"

              :disabled="!canEditComponents"

              controls-position="right"

              style="width:100%"

            />

          </el-form-item>

          <el-form-item label="社保个税">

            <el-input-number

              v-model="components.socialTax"

              :min="0"

              :precision="2"

              :step="10"

              :disabled="!canEditComponents"

              controls-position="right"

              style="width:100%"

            />

          </el-form-item>

          <el-form-item label="其他扣款">

            <el-input-number

              v-model="components.otherDeduction"

              :min="0"

              :precision="2"

              :step="10"

              :disabled="!canEditComponents"

              controls-position="right"

              style="width:100%"

            />

          </el-form-item>

          <el-form-item v-if="canEditComponents">

            <el-button type="primary" :loading="saving" @click="onSaveComponents">保存</el-button>

          </el-form-item>

        </el-form>

      </div>



      <el-table :data="detail?.items || []" size="small">

        <el-table-column prop="orderNo" label="工单" width="110" />

        <el-table-column prop="operationName" label="工序" width="90" />

        <el-table-column prop="goodQty" label="良品" width="60" />

        <el-table-column prop="defectQty" label="不良" width="60" />

        <el-table-column prop="unitPrice" label="单价" width="70" />

        <el-table-column prop="amount" label="小计" width="70" />

        <el-table-column prop="deductAmount" label="扣款" width="70" />

        <el-table-column prop="netAmount" label="应发" width="70" />

        <el-table-column label="匹配" width="70">

          <template #default="{ row }">

            <el-tag v-if="row.matched" type="success" size="small">是</el-tag>

            <el-tag v-else type="danger" size="small">未匹配</el-tag>

          </template>

        </el-table-column>

      </el-table>

    </el-drawer>

  </div>

</template>



<script setup>

import { ref, computed, onMounted } from 'vue'

import {

  generateSalary, getSalaryList, getSalary, updateSalaryComponents,

  confirmSalary, recalcSalary, revokeSalary, deleteSalary, exportSalary

} from '@/api/salary'

import { getUserList } from '@/api/sys'

import { ElMessage, ElMessageBox } from 'element-plus'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'



const now = new Date()

const defaultPeriod = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`

const q = ref({ periodType: 1, periodValue: defaultPeriod, status: null, page: 1, pageSize: 50 })

const list = ref([])

const users = ref([])

const genDlg = ref(false)

const gen = ref({ periodType: 1, periodValue: defaultPeriod, userId: null })

const detailDlg = ref(false)

const detail = ref(null)

const saving = ref(false)

const components = ref({

  baseSalary: 0,

  mealAllowance: 0,

  otherAllowance: 0,

  socialTax: 0,

  otherDeduction: 0

})



const canEditComponents = computed(() =>

  !!detail.value && detail.value.status === 0 && detail.value.allowManualComponents)



const pieceworkAmount = computed(() => Number(detail.value?.pieceworkAmount ?? 0))

const allowanceSubtotal = computed(() =>

  Number(components.value.baseSalary || 0)

  + Number(components.value.mealAllowance || 0)

  + Number(components.value.otherAllowance || 0))

const deductionSubtotal = computed(() =>

  Number(components.value.socialTax || 0)

  + Number(components.value.otherDeduction || 0))

const previewTotal = computed(() =>

  pieceworkAmount.value + allowanceSubtotal.value - deductionSubtotal.value)



function fmtMoney(v) {

  const n = Number(v)

  if (Number.isNaN(n)) return '0.00'

  return n.toFixed(2)

}



onMounted(async () => {

  const us = await getUserList({ page: 1, pageSize: 500 })

  users.value = us.data?.list || []

  load()

})



async function load() {

  const res = await getSalaryList(q.value)

  list.value = res.data?.list || []

}



function openGen() {

  gen.value = { periodType: q.value.periodType, periodValue: q.value.periodValue, userId: null }

  genDlg.value = true

}



async function onGenerate() {

  if (!gen.value.periodValue?.trim()) {

    ElMessage.error('请填写周期值')

    return

  }

  const res = await generateSalary(gen.value)

  const created = res.data?.created ?? 0
  const unmatched = res.data?.unmatchedCount ?? 0
  if (unmatched > 0) {
    ElMessage.warning(`已生成 ${created} 张，但有 ${unmatched} 条报工未匹配工价、金额为0，请先到工价表补价，别把缺价当零工资发`)
  } else {
    ElMessage.success(`已生成 ${created} 张工资单`)
  }

  genDlg.value = false

  q.value.periodType = gen.value.periodType

  q.value.periodValue = gen.value.periodValue

  load()

}



async function openDetail(row) {

  const res = await getSalary(row.id)

  detail.value = res.data

  components.value = {

    baseSalary: Number(res.data?.baseSalary ?? 0),

    mealAllowance: Number(res.data?.mealAllowance ?? 0),

    otherAllowance: Number(res.data?.otherAllowance ?? 0),

    socialTax: Number(res.data?.socialTax ?? 0),

    otherDeduction: Number(res.data?.otherDeduction ?? 0)

  }

  detailDlg.value = true

}



async function onSaveComponents() {

  if (!detail.value) return

  const vals = [

    components.value.baseSalary,

    components.value.mealAllowance,

    components.value.otherAllowance,

    components.value.socialTax,

    components.value.otherDeduction

  ]

  if (vals.some(v => Number(v) < 0)) {

    ElMessage.error('不能填负数')

    return

  }

  saving.value = true

  try {

    const res = await updateSalaryComponents(detail.value.id, {

      baseSalary: Number(components.value.baseSalary || 0),

      mealAllowance: Number(components.value.mealAllowance || 0),

      otherAllowance: Number(components.value.otherAllowance || 0),

      socialTax: Number(components.value.socialTax || 0),

      otherDeduction: Number(components.value.otherDeduction || 0)

    })

    detail.value.totalAmount = res.data?.totalAmount ?? previewTotal.value

    detail.value.pieceworkAmount = res.data?.pieceworkAmount ?? detail.value.pieceworkAmount

    ElMessage.success('已保存')

    load()

  } finally {

    saving.value = false

  }

}



async function onConfirm(row) {

  await ElMessageBox.confirm('确认后报工将冻结不可再改，继续？', '确认工资单')

  await confirmSalary(row.id)

  ElMessage.success('已确认')

  load()

}



async function onDelete(row) {

  await ElMessageBox.confirm('删除该草稿工资单？', '确认')

  await deleteSalary(row.id)

  ElMessage.success('已删除')

  load()

}



async function onRecalc(row) {

  await ElMessageBox.confirm('按当前工价重算计件明细，底薪补贴等手工项保留，继续？', '重算')

  const res = await recalcSalary(row.id)

  const unmatched = res?.data?.unmatchedCount ?? 0

  if (unmatched > 0) {

    ElMessage.warning(`已重算，仍有 ${unmatched} 条未匹配工价，请先补齐工价`)

  } else {

    ElMessage.success('已按当前工价重算')

  }

  load()

}



async function onRevoke(row) {

  await ElMessageBox.confirm('撤回将作废该工资单并解除报工冻结，改价后可重新生成重算，继续？', '撤回重算')

  await revokeSalary(row.id)

  ElMessage.success('已撤回，可重新生成工资单重算')

  load()

}



async function onExport() {

  if (!q.value.periodValue) {

    ElMessage.error('请先填写周期值')

    return

  }

  const res = await exportSalary({ periodType: q.value.periodType, periodValue: q.value.periodValue })

  const blob = res.data instanceof Blob ? res.data : new Blob([res.data])

  const url = URL.createObjectURL(blob)

  const a = document.createElement('a')

  a.href = url

  a.download = `salary_${q.value.periodValue}.csv`

  a.click()

  URL.revokeObjectURL(url)

}

</script>



<style scoped>

.toolbar { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; align-items: center; }

.detail-head { display: flex; gap: 16px; margin-bottom: 12px; font-weight: 600; }

.summary-grid {

  display: grid;

  grid-template-columns: 1fr 1fr;

  gap: 8px;

  margin-bottom: 16px;

}

.summary-item {

  border: 1px solid var(--el-border-color);

  border-radius: 4px;

  padding: 10px 12px;

}

.summary-total { grid-column: 1 / -1; background: var(--el-fill-color-light); }

.summary-label { font-size: 12px; color: var(--el-text-color-secondary); margin-bottom: 4px; }

.summary-value { font-size: 18px; font-weight: 600; }

.summary-total .summary-value { font-size: 22px; color: var(--el-color-primary); }

.components-form { margin-bottom: 16px; }

</style>

