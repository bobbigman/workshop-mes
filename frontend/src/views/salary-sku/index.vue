<template>
  <div>
    <div class="toolbar">
      <el-select v-model="periodType" style="width:100px" @change="onPeriodType">
        <el-option :value="1" label="按月" />
        <el-option :value="2" label="按周" />
      </el-select>
      <el-input
        v-model="periodValue"
        :placeholder="periodType === 1 ? 'yyyy-MM' : 'yyyy-Www'"
        style="width:130px"
        @keyup.enter="load"
      />
      <el-input v-model="productCode" clearable placeholder="产品编号" style="width:140px" @keyup.enter="load" />
      <el-input v-model="color" clearable placeholder="例：红色（输入例：红色（输入颜色筛选）筛选）" style="width:120px" @keyup.enter="load" />
      <el-input v-model="spec" clearable placeholder="例：XL / 红色（输入规格或尺码筛选）" style="width:120px" @keyup.enter="load" />
      <el-button type="primary" @click="load">查询</el-button>
      <el-button :disabled="!enabled" @click="onExport">导出 CSV</el-button>
    </div>

    <el-alert
      v-if="loaded && !enabled"
      type="info"
      :closable="false"
      show-icon
      title="本厂还没设置颜色或规格分类，本页不按色码汇总；如需启用，请到「自定义字段」添加编号为 color 和 spec 的栏目"
      description="如果需要按颜色或规格汇总工资，请到「自定义字段」页面为工单添加两个栏目，编号分别填 color 和 spec；五金厂不需要此功能可忽略"
      style="margin-bottom:12px"
    />

    <mes-table table-key="views/salary-sku/index:1" :data="items">
      <el-table-column prop="productCode" label="产品编号" min-width="120" />
      <el-table-column prop="productName" label="产品名称" min-width="140" />
      <el-table-column prop="color" label="颜色" width="100" />
      <el-table-column prop="spec" label="例：XL / 红色（输入规格或尺码筛选）" width="100" />
      <el-table-column prop="qty" label="数量" width="90" />
      <el-table-column prop="amount" label="金额" width="110" />
      <el-table-column prop="reportCount" label="报工次数" width="100" />
    </mes-table>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getSalarySkuSummary, exportSalarySkuSummary } from '@/api/salary'
import { ElMessage } from 'element-plus'

const periodType = ref(1)
const periodValue = ref(defaultMonth())
const productCode = ref('')
const color = ref('')
const spec = ref('')
const items = ref([])
const enabled = ref(false)
const loaded = ref(false)

function defaultMonth() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

function onPeriodType() {
  periodValue.value = periodType.value === 1 ? defaultMonth() : ''
}

function buildParams() {
  const params = {
    periodType: periodType.value,
    periodValue: periodValue.value.trim()
  }
  if (productCode.value.trim()) params.productCode = productCode.value.trim()
  if (color.value.trim()) params.color = color.value.trim()
  if (spec.value.trim()) params.spec = spec.value.trim()
  return params
}

async function load() {
  if (!periodValue.value.trim()) {
    ElMessage.warning(periodType.value === 1 ? '请按「年-月」格式填写，例如 2026-09' : '请按「年-W周数」格式填写，例如 2026-W37 表示 2026 年第 37 周')
    return
  }
  const res = await getSalarySkuSummary(buildParams())
  enabled.value = !!res.data?.enabled
  items.value = res.data?.items || []
  loaded.value = true
}

async function onExport() {
  if (!periodValue.value.trim()) {
    ElMessage.warning('请先在上方选择周期类型并填写周期值，再查询')
    return
  }
  const res = await exportSalarySkuSummary(buildParams())
  const blob = res.data instanceof Blob ? res.data : new Blob([res.data])
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `salary_sku_${periodValue.value.trim()}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

onMounted(load)
</script>

<style scoped>
.toolbar { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 12px; align-items: center; }
</style>
