<template>
  <div>
    <div class="toolbar">
      <el-radio-group v-model="period" @change="load">
        <el-radio-button value="today">今日</el-radio-button>
        <el-radio-button value="month">本月</el-radio-button>
        <el-radio-button value="lastMonth">上月</el-radio-button>
      </el-radio-group>
      <el-input v-model="productCode" clearable placeholder="产品编号" style="width:140px" @keyup.enter="load" />
      <el-input v-model="color" clearable placeholder="颜色" style="width:120px" @keyup.enter="load" />
      <el-input v-model="spec" clearable placeholder="规格/尺码" style="width:120px" @keyup.enter="load" />
      <el-button type="primary" @click="load">查询</el-button>
    </div>

    <el-alert
      v-if="loaded && !enabled"
      type="info"
      :closable="false"
      show-icon
      title="本厂未配置色码字段"
      description="请在「自定义字段」为工单添加编码为 color（颜色）、spec（规格/尺码）的字段后，本报表才会汇总。五金厂可不配。"
      style="margin-bottom:12px"
    />

    <el-table :data="items">
      <el-table-column prop="productCode" label="产品编号" min-width="120" />
      <el-table-column prop="productName" label="产品名称" min-width="140" />
      <el-table-column prop="color" label="颜色" width="100" />
      <el-table-column prop="spec" label="规格/尺码" width="100" />
      <el-table-column prop="totalGood" label="良品合计" width="100" />
      <el-table-column prop="totalDefect" label="不良合计" width="100" />
      <el-table-column prop="reportCount" label="报工次数" width="100" />
    </el-table>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getSkuSummary } from '@/api/prod'

const period = ref('today')
const productCode = ref('')
const color = ref('')
const spec = ref('')
const items = ref([])
const enabled = ref(false)
const loaded = ref(false)

onMounted(load)

async function load() {
  const params = { period: period.value }
  if (productCode.value.trim()) params.productCode = productCode.value.trim()
  if (color.value.trim()) params.color = color.value.trim()
  if (spec.value.trim()) params.spec = spec.value.trim()
  const res = await getSkuSummary(params)
  enabled.value = !!res.data?.enabled
  items.value = res.data?.items || []
  loaded.value = true
}
</script>

<style scoped>
.toolbar { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 12px; align-items: center; }
</style>
