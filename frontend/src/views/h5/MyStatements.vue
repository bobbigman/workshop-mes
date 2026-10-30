<template>
  <section class="statements">
    <h3>已确认工资单</h3>
    <form class="filters" @submit.prevent="load(false)">
      <label>周期
        <select v-model="periodType" @change="resetPeriod">
          <option :value="1">月结</option><option :value="2">周结</option>
        </select>
      </label>
      <input v-model="periodValue" :type="periodType === 1 ? 'month' : 'week'" aria-label="结算周期" required />
      <button type="submit">查询</button>
      <button type="button" @click="load(true)">最近一期</button>
    </form>
    <p v-if="loading" role="status">加载中…</p>
    <div v-else-if="error" role="alert">{{ error }} <button @click="load(lastLatest)">重试</button></div>
    <p v-else-if="!statement">还没有确认过的工资单。工资单由管理员确认后才会显示在这里，有疑问找班组长或管理员了解。</p>
    <template v-else>
      <div class="summary">
        <strong>{{ statement.periodValue }} · {{ statement.periodType === 1 ? '月结' : '周结' }}</strong>
        <div>计价金额 ¥{{ money(statement.grossAmount) }}</div>
        <div>扣款 ¥{{ money(statement.deductAmount) }}</div>
        <div class="payable">应发工资 ¥{{ money(statement.totalAmount) }}</div>
        <button @click="expanded = !expanded">{{ expanded ? '收起明细' : '查看明细' }}</button>
      </div>
      <template v-if="expanded">
        <p v-if="!items.length">这条工资单暂时没有明细记录。如果刚确认过，稍后下拉刷新看看；还是没有就找管理员。</p>
        <article v-for="item in items" :key="item.id" class="item">
          <strong>{{ item.operationName }}</strong> · {{ item.orderNo }}
          <div>{{ calculation(item) }} = ¥{{ money(item.amount) }}</div>
          <div v-if="item.deductAmount">不良扣款 ¥{{ money(item.deductAmount) }}（不良 {{ item.defectQty }}）</div>
        </article>
      </template>
    </template>
  </section>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { getMyStatements, getMyStatementItems } from '@/api/salary'

const periodType = ref(1)
const periodValue = ref(new Date().toISOString().slice(0, 7))
const statement = ref(null)
const items = ref([])
const expanded = ref(false)
const loading = ref(false)
const error = ref('')
const lastLatest = ref(true)
let request = 0

function money(value) { return Number(value).toFixed(2) }
function resetPeriod() {
  // 类型切换后清空旧值，避免用月字符串查询周结。
  request++
  loading.value = false
  statement.value = null
  items.value = []
  error.value = ''
  periodValue.value = ''
}
function calculation(item) {
  const coeff = Number(item.wageCoeff) !== 1 ? ` × 倍数${item.wageCoeff}` : ''
  if (item.priceType === 1) return `${item.goodQty}件 × ${money(item.unitPrice)}元/件${coeff}`
  if (item.priceType === 2) {
    const minutes = item.durationMinutes
    const duration = minutes < 60 ? `${minutes}分钟` : `${Math.floor(minutes / 60)}小时${minutes % 60}分`
    return `${duration} × ${money(item.unitPrice)}元/小时${coeff}`
  }
  if (item.priceType === 3) return `固定${money(item.unitPrice)}元`
  return '这条记录的计件单价没记全，请联系管理员补一下。'
}
async function load(latest = false) {
  const current = ++request
  lastLatest.value = latest
  loading.value = true
  error.value = ''
  statement.value = null
  items.value = []
  expanded.value = false
  try {
    const res = await getMyStatements(latest ? undefined : { periodType: periodType.value, periodValue: periodValue.value })
    if (current !== request) return
    const selected = res.data?.[0]
    if (!selected) return
    const detail = await getMyStatementItems(selected.id)
    if (current !== request) return
    statement.value = selected
    items.value = detail.data || []
    periodType.value = selected.periodType
    periodValue.value = selected.periodValue
  } catch (e) {
    if (current === request) error.value = e.response?.data?.msg || e.message || '工资单加载不出来，可能是网络问题，请检查一下网络再刷新；反复失败找管理员看看。'
  } finally {
    if (current === request) loading.value = false
  }
}
onMounted(() => load(true))
onBeforeUnmount(() => { request++ })
</script>

<style scoped>
.statements { margin-top: 24px; padding-top: 12px; border-top: 1px solid var(--app-border); }
h3 { font-size: 17px; }
.filters { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 12px; }
input, select, button { font: inherit; padding: 8px; border: 1px solid var(--app-border); border-radius: 6px; background: var(--app-card); color: var(--app-text); }
.summary, .item { padding: 14px; margin: 8px 0; background: var(--app-card); border: 1px solid var(--app-border); border-radius: 10px; line-height: 1.8; overflow-wrap: anywhere; }
.payable { font-size: 23px; font-weight: 700; color: var(--app-primary); }
.item { font-size: 14px; }
</style>
