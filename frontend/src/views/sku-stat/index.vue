<template>

  <div>

    <div class="toolbar">

      <el-radio-group v-model="period" @change="load">

        <el-radio-button value="today">今日</el-radio-button>

        <el-radio-button value="month">本月</el-radio-button>

        <el-radio-button value="lastMonth">上月</el-radio-button>

      </el-radio-group>

      <el-input v-model="productCode" clearable placeholder="产品编号" style="width:140px" @keyup.enter="load" />

      <el-input v-model="color" clearable placeholder="例：红色（输入例：红色（输入颜色筛选）筛选）" style="width:120px" @keyup.enter="load" />

      <el-input v-model="spec" clearable placeholder="例：XL / 红色（输入规格或尺码筛选）" style="width:120px" @keyup.enter="load" />

      <el-button type="primary" @click="load">查询</el-button>

    </div>



    <el-alert

      v-if="loaded && !enabled"

      type="info"

      :closable="false"

      show-icon

      title="本厂还没设置颜色或规格分类，本页不按色码汇总；如需启用，请到「自定义字段」添加编号为 color 和 spec 的栏目"

      description="如果需要按颜色或规格汇总数据，请到「自定义字段」页面为工单添加两个栏目，编号分别填 color 和 spec；五金厂不需要此功能可忽略"

      style="margin-bottom:12px"

    />



    <mes-table table-key="views/sku-stat/index:1" :data="items">

      <el-table-column prop="productCode" label="产品编号" min-width="120" />

      <el-table-column prop="productName" label="产品名称" min-width="140" />

      <el-table-column prop="color" label="颜色" width="100" />

      <el-table-column prop="spec" label="例：XL / 红色（输入规格或尺码筛选）" width="100" />

      <el-table-column prop="totalGood" label="良品合计" width="100" />

      <el-table-column prop="totalDefect" label="不良合计" width="100" />

      <el-table-column prop="reportCount" label="报工次数" width="100" />

    </mes-table>

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


