<template>
  <main class="demo">
    <header><div><span class="eyebrow">DEMO · 工序缺料</span><h1>看清缺口，补齐后再开工</h1></div><el-button @click="reset">重置样板</el-button></header>
    <el-alert title="样板演示 · 模拟数据 · 不影响生产 · 刷新后恢复" type="warning" :closable="false" show-icon />
    <section class="order"><strong>样板工单 DEMO-WO-001</strong><span>固定数量：100 件</span><span>各工序使用独立物料</span></section>
    <el-table :data="rows" row-key="code" highlight-current-row :current-row-key="selectedCode" @current-change="selectRow" border>
      <el-table-column prop="operation" label="工序" width="90" />
      <el-table-column label="物料" min-width="190"><template #default="{ row }">{{ row.code }} {{ row.material }}</template></el-table-column>
      <el-table-column prop="unit" label="单位" width="60" />
      <el-table-column prop="perUnit" label="单件用量" width="90" />
      <el-table-column label="损耗率" width="80"><template #default="{ row }">{{ row.lossPercent }}%</template></el-table-column>
      <el-table-column label="需求量" width="80"><template #default="{ row }">{{ required(row) }}</template></el-table-column>
      <el-table-column prop="available" label="工序可用" width="90" />
      <el-table-column prop="inTransit" label="在途量" width="80" />
      <el-table-column label="缺口" width="80"><template #default="{ row }"><strong :class="{ shortage: gap(row) > 0 }">{{ gap(row) }}</strong></template></el-table-column>
      <el-table-column label="状态" width="80"><template #default="{ row }"><el-tag :type="gap(row) > 0 ? 'danger' : 'success'">{{ gap(row) > 0 ? '缺料' : '齐套' }}</el-tag></template></el-table-column>
    </el-table>
    <p class="explanation">工序可用量是样板分配给该工序的量，不是仓库总库存。在途未到货，不能用于当前开工。</p>
    <section class="actions">
      <el-form label-width="100px"><el-form-item label="当前工序"><el-radio-group v-model="selectedCode" @change="clearFeedback"><el-radio-button v-for="row in rows" :key="row.code" :value="row.code">{{ row.operation }}</el-radio-button></el-radio-group></el-form-item></el-form>
      <p>{{ selected.operation }}：{{ selected.code }} {{ selected.material }}，需求 {{ required(selected) }} {{ selected.unit }}，当前缺 {{ gap(selected) }} {{ selected.unit }}。</p>
      <div class="buttons"><el-button type="primary" @click="check">模拟开工</el-button><el-button :disabled="gap(selected) === 0" @click="fill">模拟补齐 {{ gap(selected) }} {{ selected.unit }}</el-button></div>
      <el-alert :title="feedback.text" :type="feedback.type" :closable="false" show-icon class="feedback" role="status" />
      <p class="hint">补齐是人为模拟补料，不代表在途到货。这里只校验缺口，不生成真实开工、报工或停工记录；操作记录不保存。</p>
    </section>
  </main>
</template>

<script setup>
import { ref, computed } from 'vue'
const initialRows = [
  { operation: '裁切', code: 'DEMO-M01', material: '配件', unit: '个', perUnit: 2, lossPercent: 5, available: 160, inTransit: 100 },
  { operation: '装配', code: 'DEMO-M02', material: '紧固件', unit: '个', perUnit: 1, lossPercent: 0, available: 100, inTransit: 0 },
  { operation: '包装', code: 'DEMO-M03', material: '包装袋', unit: '个', perUnit: 1, lossPercent: 0, available: 80, inTransit: 0 }
]
const rows = ref(initialRows.map(row => ({ ...row })))
const selectedCode = ref('DEMO-M01')
const selected = computed(() => rows.value.find(row => row.code === selectedCode.value))
const feedback = ref({ type: 'info', text: '选择工序，点击“模拟开工”查看当前缺口。' })
const required = row => 100 * row.perUnit * (100 + row.lossPercent) / 100
const gap = row => Math.max(required(row) - row.available, 0)
function clearFeedback() { feedback.value = { type: 'info', text: '点击“模拟开工”校验当前工序。' } }
function selectRow(row) { if (row && row.code !== selectedCode.value) { selectedCode.value = row.code; clearFeedback() } }
function check() {
  const row = selected.value
  feedback.value = gap(row) > 0
    ? { type: 'error', text: `${row.operation}暂不能模拟开工：${row.code} ${row.material}缺 ${gap(row)} ${row.unit}；在途不抵扣当前缺口。` }
    : { type: 'success', text: `${row.operation}模拟校验通过，当前物料齐套。` }
}
function fill() {
  const row = selected.value, missing = gap(row)
  if (missing === 0) return
  row.available += missing
  feedback.value = { type: 'info', text: `已人为模拟补齐 ${row.material} ${missing} ${row.unit}，请再次校验。` }
}
function reset() {
  rows.value = initialRows.map(row => ({ ...row }))
  selectedCode.value = 'DEMO-M01'
  clearFeedback()
}
</script>

<style scoped>
:global(body) { margin: 0; background: #f3f5f8; color: #253449; font-family: "Microsoft YaHei", sans-serif; }
.demo { max-width: 1120px; margin: 40px auto; padding: 30px; background: white; border-radius: 14px; }
header { display: flex; justify-content: space-between; align-items: center; gap: 20px; margin-bottom: 24px; }
.eyebrow { color: #62768f; font-size: 13px; letter-spacing: 1px; }
h1 { font-size: 26px; margin: 10px 0 0; }
.order { display: flex; gap: 28px; flex-wrap: wrap; padding: 24px 0; font-size: 14px; }
.order span, .explanation, .hint { color: #64748b; }
.shortage { color: #c43f43; }
.explanation, .hint { font-size: 13px; line-height: 1.8; }
.actions { margin-top: 24px; padding: 22px; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 10px; }
.actions p { font-size: 14px; line-height: 1.8; }
.buttons { display: flex; gap: 10px; margin: 16px 0; }
.buttons .el-button { margin-left: 0; }
.feedback { margin-top: 18px; }
@media (max-width: 800px) { .demo { margin: 12px; padding: 18px; } h1 { font-size: 20px; } .order { gap: 12px; } }
</style>
