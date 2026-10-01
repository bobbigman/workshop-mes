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
      <el-input v-model="color" clearable placeholder="颜色" style="width:120px" @keyup.enter="load" />
      <el-input v-model="spec" clearable placeholder="规格/尺码" style="width:120px" @keyup.enter="load" />
      <el-button type="primary" @click="load">查询</el-button>
      <el-button :disabled="!enabled" @click="onExport">导出 CSV</el-button>
    </div>

    <el-alert
      v-if="loaded && !enabled"
      type="info"
      :closable="false"
      show-icon
      title="本厂未配置色码字段"
      description="请在「自定义字段」为工单添加编码为 color / spec 的字段后，本页才会按色码汇总工资。五金厂可不配。"
      style="margin-bottom:12px"
    />

    <el-table :data="items">
      <el-table-column prop="productCode" label="产品编号" min-width="120" />
      <el-table-column prop="productName" label="产品名称" min-width="140" />
      <el-table-column prop="color" label="颜色" width="100" />
      <el-table-column prop="spec" label="规格/尺码" width="100" />
      <el-table-column prop="qty" label="数量" width="90" />
      <el-table-column prop="amount" label="金额" width="110" />
      <el-table-column prop="reportCount" label="报工次数" width="100" />
    </el-table>
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
    ElMessage.warning(periodType.value === 1 ? '请填写 yyyy-MM' : '请填写 yyyy-Www')
    return
  }
  const res = await getSalarySkuSummary(buildParams())
  enabled.value = !!res.data?.enabled
  items.value = res.data?.items || []
  loaded.value = true
}

async function onExport() {
  if (!periodValue.value.trim()) {
    ElMessage.warning('请先填写周期')
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
