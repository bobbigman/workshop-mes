<template>
  <div>
    <div class="toolbar">
      <el-radio-group v-model="period" @change="load">
        <el-radio-button value="today">今日</el-radio-button>
        <el-radio-button value="month">本月</el-radio-button>
        <el-radio-button value="lastMonth">上月</el-radio-button>
      </el-radio-group>
    </div>
    <el-table :data="items">
      <el-table-column prop="userName" label="报工人" />
      <el-table-column prop="totalGood" label="良品合计" />
      <el-table-column prop="totalDefect" label="不良品合计" />
      <el-table-column prop="reportCount" label="报工次数" />
    </el-table>
  </div>
</template>
<script setup>
import { ref, onMounted } from 'vue'
import { getProductionStat } from '@/api/prod'
const period = ref('today'), items = ref([])
onMounted(load)
async function load() {
  const res = await getProductionStat({ period: period.value })
  items.value = res.data.items || []
}
</script>
<style scoped>.toolbar{margin-bottom:12px}</style>
