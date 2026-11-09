<template>
  <aside v-if="price" class="processing-requirements" aria-label="本单加工要求与预算工价">
    <div class="summary-row">
      <span class="summary-text">预算 {{ formatMoney(price.unitPrice) }} 元/{{ price.priceUnit || '单位' }}</span>
      <button type="button" class="toggle" :aria-expanded="open" @click="open = !open">
        {{ open ? '收起计价依据' : '查看计价依据' }}
      </button>
    </div>
    <div v-if="open" class="detail">
      <div class="price-main">
        单价 <b>{{ formatMoney(price.unitPrice) }}</b> 元/{{ price.priceUnit || '单位' }}
        <span v-if="Number(price.deductPrice) > 0" class="deduct">（不良扣 {{ formatMoney(price.deductPrice) }} 元/{{ price.priceUnit || '单位' }}）</span>
      </div>
      <div v-if="composition.length" class="composition">
        <div class="comp-title">组成</div>
        <ul>
          <li v-for="(item, i) in composition" :key="'C' + i">
            <span class="comp-name">{{ item.name }}</span>
            <span class="comp-amt">{{ item.note || (item.sign + formatMoney(item.amount)) }}</span>
          </li>
        </ul>
        <div v-if="composition.length > 1" class="comp-total">合计 {{ formatMoney(price.unitPrice) }} 元/{{ price.priceUnit || '单位' }}</div>
      </div>
      <div v-if="hitText" class="hit">命中 {{ hitText }}</div>
      <div v-if="specText" class="spec">规格 {{ specText }}</div>
      <p class="hint">以上为本单预算参考；实际结算按报工业务日期的有效价，调价后可能不同。</p>
    </div>
  </aside>
</template>
<script setup>
import { computed, ref, watch } from 'vue'

const props = defineProps({ price: Object })
const open = ref(false)

watch(() => props.price, () => { open.value = false })

function formatMoney(v) {
  const n = Number(v)
  if (!Number.isFinite(n)) return '—'
  return Number.isInteger(n) ? String(n) : String(n)
}

function fieldLabel(defs, id) {
  if (!defs) return `属性${id}`
  const f = defs[id] ?? defs[String(id)]
  return (f && (f.fieldName || f.FieldName)) || `属性${id}`
}

function kindName(kind) {
  if (kind === 'final') return '最终价'
  if (kind === 'extra') return '附加'
  return '基础价'
}

const hitText = computed(() => {
  const p = props.price
  if (!p) return ''
  const code = p.attributeEvidence?.priceListCode || ''
  const rows = p.attributeEvidence?.rows || []
  const primary = rows.find(r => r.matched && r.kind !== 'extra')
  const rowId = primary?.rowId || (typeof p.baseName === 'string' && p.baseName.includes('/')
    ? p.baseName.split('/').pop().trim()
    : '')
  if (code && rowId) return `价目表 ${code} · 价格行 ${rowId}`
  if (p.baseName) return p.baseName
  return ''
})

const specText = computed(() => {
  const attrs = props.price?.attributeEvidence?.attributes
  if (!attrs || typeof attrs !== 'object') return ''
  const defs = props.price?.fieldDefinitions
  const parts = Object.keys(attrs)
    .map(k => {
      const v = attrs[k]
      if (v == null || String(v).trim() === '') return null
      return `${fieldLabel(defs, k)}=${v}`
    })
    .filter(Boolean)
  return parts.join('，')
})

/** 单价组成：基础/最终价行 + 收费组附加（已含记 0） */
const composition = computed(() => {
  const p = props.price
  if (!p) return []
  const items = []
  const rows = p.attributeEvidence?.rows || []
  const primary = rows.find(r => r.matched && r.kind !== 'extra')
  if (primary) {
    items.push({
      name: `${kindName(primary.kind)} ${primary.rowId}`,
      amount: Number(primary.amount),
      sign: '',
      note: ''
    })
  } else if (p.basePrice != null || p.unitPrice != null) {
    items.push({
      name: p.baseName || '基础价',
      amount: Number(p.basePrice ?? p.unitPrice),
      sign: '',
      note: ''
    })
  }
  const lines = p.lines || []
  if (lines.length) {
    for (const line of lines) {
      const included = !!line.included
      items.push({
        name: `${line.group}：${line.option}`,
        amount: included ? 0 : Number(line.extra || 0),
        sign: included ? '' : '+',
        note: included ? '已含，不另加价' : ''
      })
    }
  } else {
    for (const r of rows.filter(r => r.matched && r.kind === 'extra')) {
      items.push({
        name: `附加 ${r.rowId}`,
        amount: r.included ? 0 : Number(r.amount),
        sign: r.included ? '' : '+',
        note: r.included ? '已含，不另加价' : ''
      })
    }
  }
  return items
})
</script>
<style scoped>
.processing-requirements {
  padding: 8px 12px;
  margin: 12px 0;
  background: #f0f7f4;
  border: 1px solid #d5e8de;
  border-radius: 8px;
  line-height: 1.6;
  font-size: 14px;
  color: #24563e;
}
.summary-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
}
.summary-text {
  font-weight: 600;
}
.toggle {
  border: 0;
  background: transparent;
  color: #2a6b4f;
  padding: 0;
  font-size: 13px;
  cursor: pointer;
  text-decoration: underline;
}
.detail {
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px dashed #c5ddd1;
}
.price-main b {
  font-size: 16px;
}
.deduct {
  color: #5a7a6a;
  font-size: 13px;
}
.composition {
  margin: 8px 0;
}
.comp-title {
  font-weight: 600;
  margin-bottom: 2px;
}
.composition ul {
  margin: 0;
  padding: 0;
  list-style: none;
}
.composition li {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 2px 0;
}
.comp-name {
  word-break: break-word;
}
.comp-amt {
  flex-shrink: 0;
  font-variant-numeric: tabular-nums;
}
.comp-total {
  margin-top: 4px;
  font-weight: 600;
  text-align: right;
}
.hit,
.spec {
  word-break: break-word;
}
.hint {
  margin: 8px 0 0;
  font-size: 12px;
  color: #6a8578;
  line-height: 1.5;
}
</style>
