<template>
  <div class="h5-page">
    <div class="header">
      <button type="button" class="back" @click="router.back()">‹</button>
      <h2>我的工资</h2>
      <H5LogoutBtn />
    </div>

    <div class="filters">
      <div class="seg">
        <button type="button" :class="{ on: period === 'day' }" @click="setPeriod('day')">今天</button>
        <button type="button" :class="{ on: period === 'week' }" @click="setPeriod('week')">本周</button>
        <button type="button" :class="{ on: period === 'month' }" @click="setPeriod('month')">本月</button>
      </div>
      <input v-model="dateStr" type="date" class="date" @change="reload" />
    </div>

    <div class="sum" v-if="!loading && !loadError">
      <div class="sum-label">预估合计（仅已复核）</div>
      <div class="sum-val">¥ {{ formatMoney(totalAmount) }}</div>
      <div class="sum-sub">良品 {{ totalGoodQty }} 件 · {{ rangeText }}</div>
    </div>

    <div v-if="loading" class="state">加载中…</div>
    <div v-else-if="loadError" class="state err">
      加载失败，请重试
      <button type="button" class="btn-sm" @click="reload">重试</button>
    </div>
    <div v-else-if="list.length === 0" class="state">该周期没有报工</div>

    <div v-for="row in list" :key="row.reportId" class="card" :class="{ muted: !row.countedInTotal }">
      <div class="row1">
        <span class="no">{{ row.orderNo }}</span>
        <span class="tag" :class="tagClass(row)">{{ statusLabel(row) }}</span>
      </div>
      <div class="product">
        <span v-if="row.productCode" class="code">{{ row.productCode }}</span>
        {{ row.productName }}
      </div>
      <div v-if="skuEnabled && (row.color || row.spec)" class="sku">
        {{ row.color || '—' }} / {{ row.spec || '—' }}
      </div>
      <div class="meta">
        <span>{{ row.operationName }}</span>
        <span>良品 {{ row.goodQty }} / 不良 {{ row.defectQty }}</span>
      </div>
      <div class="money">
        <span v-if="row.unitPrice != null">单价 ¥{{ formatMoney(row.unitPrice) }}</span>
        <span v-else class="wage-miss">未配工价</span>
        <span class="amt" :class="{ dim: !row.countedInTotal }">
          {{ row.amount != null ? '¥' + formatMoney(row.amount) : '—' }}
        </span>
      </div>
      <div class="time">{{ formatTime(row.reportTime) }}</div>
    </div>

    <p class="tip">已结算显示工资单固化金额；未结算显示报工快照。待复核/退回不计入合计。</p>
    <TabBar active="home" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { getMyWage } from '@/api/salary'
import TabBar from './TabBar.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'

const router = useRouter()
const period = ref('month')
const dateStr = ref(toDateInput(new Date()))
const list = ref([])
const totalAmount = ref(0)
const totalGoodQty = ref(0)
const skuEnabled = ref(false)
const start = ref(null)
const end = ref(null)
const loading = ref(false)
const loadError = ref(false)

const rangeText = computed(() => {
  if (!start.value) return ''
  const a = formatShort(start.value)
  const b = end.value ? formatShort(new Date(new Date(end.value).getTime() - 86400000)) : ''
  return period.value === 'day' ? a : `${a} ~ ${b}`
})

function toDateInput(d) {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

function formatShort(t) {
  const d = new Date(t)
  if (Number.isNaN(d.getTime())) return ''
  return `${d.getMonth() + 1}/${d.getDate()}`
}

function formatMoney(n) {
  const v = Number(n)
  if (Number.isNaN(v)) return '0.00'
  return v.toFixed(2)
}

function formatTime(t) {
  if (!t) return ''
  const d = new Date(t)
  if (Number.isNaN(d.getTime())) return String(t)
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  const hh = String(d.getHours()).padStart(2, '0')
  const mi = String(d.getMinutes()).padStart(2, '0')
  return `${mm}-${dd} ${hh}:${mi}`
}

function statusLabel(row) {
  if (row.settled) return '已结算'
  if (row.reviewStatus === 0) return '待复核'
  if (row.reviewStatus === 1) return '已通过'
  if (row.reviewStatus === 2) return '已退回'
  return '-'
}

function tagClass(row) {
  if (row.settled) return 'settled'
  return 'r' + row.reviewStatus
}

function setPeriod(p) {
  period.value = p
  reload()
}

async function reload() {
  loading.value = true
  loadError.value = false
  try {
    const res = await getMyWage({ period: period.value, date: dateStr.value })
    const d = res.data || {}
    list.value = d.items || []
    totalAmount.value = d.totalAmount || 0
    totalGoodQty.value = d.totalGoodQty || 0
    skuEnabled.value = !!d.skuEnabled
    start.value = d.start
    end.value = d.end
  } catch (e) {
    loadError.value = true
    list.value = []
  } finally {
    loading.value = false
  }
}

onMounted(reload)
</script>

<style scoped>
.h5-page {
  padding: var(--h5-page-padding);
  padding-bottom: calc(var(--h5-tabbar-height) + 16px + env(safe-area-inset-bottom));
  min-height: 100vh;
  background: var(--app-bg);
}
.header {
  display: flex; align-items: center; gap: 8px; margin-bottom: 12px;
}
.header h2 { flex: 1; margin: 0; font-size: 18px; }
.back {
  border: none; background: transparent; font-size: 28px; line-height: 1;
  padding: 0 4px; color: var(--app-text);
}
.filters { display: flex; flex-direction: column; gap: 8px; margin-bottom: 12px; }
.seg {
  display: flex; background: var(--app-card); border-radius: 8px; overflow: hidden;
  border: 1px solid var(--app-border);
}
.seg button {
  flex: 1; border: none; background: transparent; padding: 8px 0;
  font-size: 14px; color: var(--app-text-3);
}
.seg button.on { background: var(--app-primary); color: #fff; font-weight: 600; }
.date {
  border: 1px solid var(--app-border); border-radius: 8px; padding: 8px 10px;
  font-size: 14px; background: var(--app-card);
}
.sum {
  background: var(--app-card); border-radius: 10px; padding: 14px 16px;
  margin-bottom: 12px; border: 1px solid var(--app-border);
}
.sum-label { font-size: 13px; color: var(--app-text-3); }
.sum-val { font-size: 28px; font-weight: 700; margin: 4px 0; color: var(--app-primary); }
.sum-sub { font-size: 12px; color: var(--app-text-3); }
.card {
  background: var(--app-card); border-radius: 10px; padding: 12px 14px;
  margin-bottom: 10px; border: 1px solid var(--app-border);
}
.card.muted { opacity: 0.75; }
.row1 { display: flex; justify-content: space-between; align-items: center; }
.no { font-weight: 600; font-size: 14px; }
.tag {
  font-size: 11px; padding: 2px 6px; border-radius: 4px;
  background: #f0f0f0; color: #666;
}
.tag.r0 { background: #fff7e6; color: #d48806; }
.tag.r1 { background: #f6ffed; color: #389e0d; }
.tag.r2 { background: #fff1f0; color: #cf1322; }
.tag.settled { background: #e6f4ff; color: #0958d9; }
.product { margin-top: 6px; font-size: 14px; }
.code { color: var(--app-text-3); margin-right: 6px; }
.sku { margin-top: 4px; font-size: 13px; color: var(--app-text-2); }
.meta {
  display: flex; justify-content: space-between; margin-top: 6px;
  font-size: 13px; color: var(--app-text-3);
}
.money {
  display: flex; justify-content: space-between; margin-top: 8px;
  font-size: 14px;
}
.wage-miss { color: #cf1322; font-weight: 600; }
.amt { font-weight: 700; color: var(--app-primary); }
.amt.dim { color: var(--app-text-3); font-weight: 500; }
.time { margin-top: 4px; font-size: 12px; color: var(--app-text-3); }
.state { text-align: center; padding: 24px; color: var(--app-text-3); }
.state.err { color: #cf1322; }
.btn-sm {
  margin-left: 8px; border: 1px solid var(--app-border); background: #fff;
  border-radius: 6px; padding: 4px 10px; font-size: 13px;
}
.tip { font-size: 12px; color: var(--app-text-3); margin-top: 8px; line-height: 1.5; }
</style>
