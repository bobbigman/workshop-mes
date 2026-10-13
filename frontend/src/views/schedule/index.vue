<template>
  <div class="schedule">
    <ValueTip :text="VALUE_TIPS.schedule" />
    <!-- 权重配置 -->
    <el-card shadow="never" class="mb">
      <div class="weights">
        <span class="label">排产权重（合计自动显示）</span>
        <div class="weight-item"><span>交期紧迫度</span><el-input-number v-model="weights.due" :min="0" :max="1" :step="0.05" :precision="2" size="small" /></div>
        <div class="weight-item"><span>齐套性</span><el-input-number v-model="weights.kit" :min="0" :max="1" :step="0.05" :precision="2" size="small" /></div>
        <div class="weight-item"><span>客户等级</span><el-input-number v-model="weights.customer" :min="0" :max="1" :step="0.05" :precision="2" size="small" /></div>
        <div class="weight-item"><span>换型相似度</span><el-input-number v-model="weights.changeover" :min="0" :max="1" :step="0.05" :precision="2" size="small" /></div>
        <div class="weight-item"><span>订单金额</span><el-input-number v-model="weights.amount" :min="0" :max="1" :step="0.05" :precision="2" size="small" /></div>
        <span class="sum">合计：{{ sum.toFixed(2) }}</span>
        <el-button type="primary" @click="calc">重新计算</el-button>
        <el-button @click="resetWeights">恢复默认</el-button>
        <el-button type="success" @click="exportXlsx" :loading="exporting">导出 Excel</el-button>
      </div>
      <div class="tip">交期紧迫度自动按完工日算；客户/金额/换型/齐套四维先在表格里手填（0-100，齐套填低分会提示顺延）。接金蝶后改自动取数。</div>
    </el-card>

    <!-- 评分表 -->
    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" v-loading="loading">
        <el-table-column prop="suggestSeq" label="序号" width="56" align="center" />
        <el-table-column prop="moNo" label="任务单号" width="130" />
        <el-table-column label="产品" width="140">
          <template #default="{ row }">{{ row.productCode }} / {{ row.productName }}</template>
        </el-table-column>
        <el-table-column prop="qty" label="数量" width="70" align="right" />
        <el-table-column label="要求完工日" width="110" align="center">
          <template #default="{ row }">{{ row.dueDate ? row.dueDate.slice(0,10) : '未设' }}</template>
        </el-table-column>
        <el-table-column prop="scoreDue" label="交期紧迫度" width="95" align="center" />
        <el-table-column label="客户等级" width="100" align="center">
          <template #default="{ row }">
            <el-input-number v-model="row.scoreCustomer" :min="0" :max="100" size="small" controls-position="right" style="width: 80px" />
          </template>
        </el-table-column>
        <el-table-column label="订单金额" width="100" align="center">
          <template #default="{ row }">
            <el-input-number v-model="row.scoreAmount" :min="0" :max="100" size="small" controls-position="right" style="width: 80px" />
          </template>
        </el-table-column>
        <el-table-column label="换型相似度" width="105" align="center">
          <template #default="{ row }">
            <el-input-number v-model="row.scoreChangeover" :min="0" :max="100" size="small" controls-position="right" style="width: 80px" />
          </template>
        </el-table-column>
        <el-table-column label="齐套性" width="100" align="center">
          <template #default="{ row }">
            <el-input-number v-model="row.scoreKit" :min="0" :max="100" size="small" controls-position="right" style="width: 80px" />
          </template>
        </el-table-column>
        <el-table-column label="加权总分" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="row.scoreKit != null && row.scoreKit < 60 ? 'danger' : 'success'" effect="light">{{ row.total }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="rank" label="优先级排名" width="90" align="center" />
        <el-table-column prop="suggestSeq" label="建议开工顺序" width="105" align="center" />
        <el-table-column prop="remark" label="备注" min-width="180" />
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { getSchedule, querySchedule, exportSchedule } from '@/api/schedule'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'

const loading = ref(false)
const exporting = ref(false)
const rows = ref([])

// 默认权重（docs/36 §8.7）
const weights = ref({
  due: 0.30,
  kit: 0.25,
  customer: 0.20,
  changeover: 0.15,
  amount: 0.10
})

const sum = computed(() =>
  weights.value.due + weights.value.kit + weights.value.customer + weights.value.changeover + weights.value.amount
)

function resetWeights() {
  weights.value = { due: 0.30, kit: 0.25, customer: 0.20, changeover: 0.15, amount: 0.10 }
  calc()
}

// 把手填的四维分整理成 {orderNo: {customer,amount,changeover,kit}}
function buildManual() {
  const manual = {}
  rows.value.forEach(r => {
    manual[r.moNo] = {
      customer: r.scoreCustomer,
      amount: r.scoreAmount,
      changeover: r.scoreChangeover,
      kit: r.scoreKit
    }
  })
  return manual
}

async function load() {
  loading.value = true
  try {
    const { code, data } = await getSchedule()
    if (code === 0) {
      rows.value = data
    }
  } finally {
    loading.value = false
  }
}

async function calc() {
  loading.value = true
  try {
    const { code, data } = await querySchedule({ manualScores: buildManual(), weights: weights.value })
    if (code === 0) {
      rows.value = data
      ElMessage.success('已重新计算')
    }
  } finally {
    loading.value = false
  }
}

async function exportXlsx() {
  exporting.value = true
  try {
    const res = await exportSchedule({ manualScores: buildManual(), weights: weights.value })
    const blob = new Blob([res.data], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `排产优先级_${Date.now()}.xlsx`
    a.click()
    URL.revokeObjectURL(url)
    ElMessage.success('已导出')
  } finally {
    exporting.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.mb { margin-bottom: 14px; }
.weights { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; }
.label { font-weight: 600; margin-right: 4px; }
.weight-item { display: flex; align-items: center; gap: 6px; font-size: 13px; color: var(--app-text-2); }
.sum { font-weight: 600; color: var(--app-text-1); }
.tip { margin-top: 10px; font-size: 12px; color: var(--app-text-3); }
</style>
