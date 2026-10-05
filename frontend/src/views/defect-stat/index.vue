<template>
  <div>
    <ValueTip :text="VALUE_TIPS.defectStat" />
    <div class="toolbar">
      <el-radio-group v-model="period" @change="load">
        <el-radio-button value="today">今日</el-radio-button>
        <el-radio-button value="month">本月</el-radio-button>
        <el-radio-button value="lastMonth">上月</el-radio-button>
      </el-radio-group>
      <el-input
        v-model="keyword"
        clearable
        placeholder="工单号 / 产品编号 / 名称"
        style="width:220px"
        @keyup.enter="load"
      />
      <el-button type="primary" @click="load">查询</el-button>
    </div>

    <el-tabs v-model="tab">
      <el-tab-pane label="分布" name="distribution">
        <el-table :data="distribution" row-key="defectKey">
          <el-table-column type="expand">
            <template #default="{ row }">
              <div class="expand-box">
                <div class="expand-title">该原因集中在哪些工序</div>
                <el-table :data="row.byOperation || []" size="small" border>
                  <el-table-column prop="operationName" label="工序" />
                  <el-table-column prop="totalDefect" label="不良数" width="100" />
                  <el-table-column prop="reportCount" label="报工次数" width="100" />
                </el-table>
              </div>
            </template>
          </el-table-column>
          <el-table-column prop="defectName" label="不良原因" min-width="140" />
          <el-table-column prop="totalDefect" label="不良数" width="100" />
          <el-table-column prop="reportCount" label="报工次数" width="100" />
          <el-table-column label="占比" width="100">
            <template #default="{ row }">{{ formatPct(row.sharePercent) }}</template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="汇总" name="summary">
        <el-table :data="summary">
          <el-table-column prop="operationName" label="工序" min-width="120" />
          <el-table-column prop="defectName" label="不良原因" min-width="120" />
          <el-table-column prop="userName" label="报工人" width="120" />
          <el-table-column prop="totalDefect" label="不良数" width="100" />
          <el-table-column prop="totalGood" label="良品数" width="100" />
          <el-table-column label="不良率" width="100">
            <template #default="{ row }">{{ formatPct(row.defectRate) }}</template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="明细" name="details">
        <el-table :data="details">
          <el-table-column prop="orderNo" label="工单号" min-width="130" />
          <el-table-column prop="productCode" label="产品编号" min-width="110" />
          <el-table-column prop="productName" label="产品名称" min-width="120" />
          <el-table-column prop="operationName" label="工序" width="110" />
          <el-table-column prop="userName" label="报工人" width="100" />
          <el-table-column label="报工时间" width="170">
            <template #default="{ row }">{{ formatTime(row.reportTime) }}</template>
          </el-table-column>
          <el-table-column prop="defectName" label="不良原因" min-width="110" />
          <el-table-column prop="defectQty" label="不良数" width="90" />
          <el-table-column prop="goodQty" label="良品数" width="90" />
          <el-table-column label="复核" width="90">
            <template #default="{ row }">{{ reviewLabel(row.reviewStatus) }}</template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getDefectStat } from '@/api/prod'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'

const period = ref('today')
const keyword = ref('')
const tab = ref('distribution')
const distribution = ref([])
const summary = ref([])
const details = ref([])

onMounted(load)

async function load() {
  const params = { period: period.value }
  if (keyword.value.trim()) params.keyword = keyword.value.trim()
  const res = await getDefectStat(params)
  const data = res.data || {}
  distribution.value = (data.distribution || []).map((row, i) => ({
    ...row,
    defectKey: `${row.defectId ?? 'null'}-${i}`
  }))
  summary.value = data.summary || []
  details.value = data.details || []
}

function formatPct(v) {
  const n = Number(v)
  if (!Number.isFinite(n)) return '—'
  return `${n}%`
}

function formatTime(v) {
  if (!v) return '—'
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v)
  const pad = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}

function reviewLabel(s) {
  if (s === 0) return '待复核'
  if (s === 1) return '已通过'
  if (s === 2) return '已退回'
  return '—'
}
</script>

<style scoped>
.toolbar { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 12px; align-items: center; }
.expand-box { padding: 8px 24px 12px; }
.expand-title { margin-bottom: 8px; color: #606266; font-size: 13px; }
</style>
