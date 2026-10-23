<template>
  <main class="pricing-demo">
    <div class="demo-heading">
      <div><h2>合片 · 加工选项演示</h2><p>先维护工序选项，再试选本单加工要求。</p></div>
      <div><el-button @click="$router.push('/order')">返回工单</el-button><el-button @click="reset">重置演示</el-button></div>
    </div>
    <el-alert title="交互小样：不保存工单，不参与报工和工资计算；刷新或离开页面后恢复初始数据。" type="warning" :closable="false" show-icon />

    <el-tabs v-model="tab" class="demo-tabs">
      <el-tab-pane label="1. 工序设置" name="settings">
        <el-card shadow="never">
          <h3>合片 / 丝印 <el-tag>单选</el-tag></h3>
          <p class="muted">下面维护各档附加工价。工单只能选择一档，“不丝印”明确按零加价。</p>
          <el-table :data="silkOptions">
            <el-table-column prop="label" label="选项" min-width="250" />
            <el-table-column label="加价（元/打）" width="220">
              <template #default="{ row }">
                <span v-if="row.id === 'none'">0.00</span>
                <el-input-number v-else v-model="row.price" :min="0" :max="9999" :precision="2" :step="0.05" :aria-label="row.label + '加价'" />
              </template>
            </el-table-column>
          </el-table>
          <div class="extra-setting"><span>双层伞面叠片（另组选项）</span><el-input-number v-model="stackPrice" :min="0" :max="9999" :precision="2" :step="0.1" aria-label="叠片加价" /><span>元/打</span></div>
          <el-alert title="演示假设：叠片可另行选择；其适用规格与叠加条件待客户确认。2—4片丝印的打包范围也待确认。" type="info" :closable="false" />
          <el-button type="primary" class="next-button" @click="tab = 'order'">去工单试选</el-button>
        </el-card>
      </el-tab-pane>
      <el-tab-pane label="2. 工单试选" name="order">
        <div class="demo-grid">
          <el-card shadow="never">
            <h3>演示工单 <el-tag type="info">合片工序</el-tag></h3>
            <el-form label-position="top">
              <el-form-item label="基础工价案例">
                <el-select v-model="caseId" aria-label="基础工价案例" @change="changeCase">
                  <el-option v-for="item in cases" :key="item.id" :value="item.id" :label="`${item.label} · ${money(item.cents)}元/打`" />
                </el-select>
              </el-form-item>
              <el-form-item label="演示数量（打）">
                <el-input-number v-model="quantity" :min="1" :max="100000" :precision="0" aria-label="演示数量（打）" />
              </el-form-item>
              <el-form-item label="丝印要求（必选一项）" required>
                <el-radio-group v-model="silkId" class="silk-options" aria-label="丝印要求">
                  <el-radio v-for="item in silkOptions" :key="item.id" :value="item.id" border>
                    <span>{{ item.label }}</span>
                    <strong>{{ currentCase.silkIncluded && item.id !== 'none' ? '已包含，不另加价' : `+${money(toCents(item.price))}元/打` }}</strong>
                  </el-radio>
                </el-radio-group>
              </el-form-item>
              <el-alert v-if="currentCase.silkIncluded" title="此案例原价已含丝印。各档均不另加价仅为演示假设，正式适用范围待确认。" type="info" :closable="false" />
              <el-form-item label="其他加工（跨组叠加演示）" class="other-options">
                <el-checkbox v-model="stackSelected">双层伞面叠片（+{{ money(toCents(stackPrice)) }}元/打）</el-checkbox>
              </el-form-item>
              <el-checkbox v-model="stackIncluded">模拟：此基础价已包含叠片</el-checkbox>
              <p class="muted">叠片是否适用当前案例、能否与丝印叠加，待客户确认。</p>
            </el-form>
          </el-card>

          <el-card shadow="never" class="price-summary">
            <h3>本单计价预览</h3>
            <div v-for="line in lines" :key="line.key" class="price-line">
              <div>{{ line.label }}<small v-if="line.note">{{ line.note }}</small></div>
              <strong>{{ line.cents === null ? '待选择' : `${line.key === 'base' ? '' : '+'}${money(line.cents)}` }}</strong>
            </div>
            <div class="total-line"><span>合片单价</span><strong>{{ valid ? money(unitCents) : '待完善' }}<small v-if="valid"> 元/打</small></strong></div>
            <p class="muted">{{ quantity || '—' }}打 × {{ valid ? money(unitCents) : '—' }}元/打</p>
            <div class="estimate"><span>本工序估算加工费</span><strong>{{ valid ? `¥ ${money(unitCents * quantity)}` : '—' }}</strong></div>
            <p class="muted">所有价格按“打”演示，不换算为把数，不代表已结算工资。</p>
            <el-button type="primary" :disabled="!valid" @click="confirmed = true">确认演示选择</el-button>
            <p v-if="!valid" class="pending">请选择丝印要求，并填写有效价格和数量。</p>
            <el-alert v-if="confirmed" class="confirmation" title="已确认本页演示选择，未保存任何工单或工资数据。" type="success" :closable="false" show-icon />
          </el-card>
        </div>
      </el-tab-pane>
    </el-tabs>
    <el-collapse class="questions">
      <el-collapse-item title="客户确认时，请重点核对这些规则" name="rules">
        <ul>
          <li>2—4片丝印与剪线头、对线、折叠标，是一套加工要求还是独立收费项目？</li>
          <li>已含丝印的成套价格适用哪些档位？其他“已含”项目如何确定？</li>
          <li>叠片等项目的适用规格、组合限制；同一行列举多项加工是加一次还是分别加？</li>
          <li>正式报工用打还是把？单位换算、改价与历史重算规则如何确定？</li>
        </ul>
      </el-collapse-item>
    </el-collapse>
    <p class="source">示例来源：2026生产部加工单价表 / 合片，第6、12—14、22、38行。仅采用代表性价格；组合关系待确认。</p>
  </main>
</template>

<script setup>
import { computed, ref, watch } from 'vue'

const initialOptions = () => [
  { id: 'none', label: '不丝印', price: 0 },
  { id: 'one', label: '1片丝印', price: 0.25 },
  { id: 'two-four', label: '2片—4片丝印、剪线头、对线、折叠标', price: 0.5 },
  { id: 'five-eight', label: '5片—8片丝印', price: 1.65 }
]
const cases = [
  { id: 'ordinary', label: '25寸8K', cents: 500, silkIncluded: false },
  { id: 'included', label: '27寸以下大小片方形（含丝印）', cents: 770, silkIncluded: true }
]
const tab = ref('order')
const caseId = ref('ordinary')
const silkOptions = ref(initialOptions())
const silkId = ref('')
const stackPrice = ref(1.6)
const stackSelected = ref(false)
const stackIncluded = ref(false)
const quantity = ref(10)
const confirmed = ref(false)
const currentCase = computed(() => cases.find(item => item.id === caseId.value))
const selectedSilk = computed(() => silkOptions.value.find(item => item.id === silkId.value))
const toCents = value => Math.round(Number(value) * 100)
const money = cents => (cents / 100).toFixed(2)
const validPrice = value => typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 9999
const valid = computed(() => !!selectedSilk.value && silkOptions.value.every(item => validPrice(item.price)) && validPrice(stackPrice.value) && Number.isInteger(quantity.value) && quantity.value >= 1 && quantity.value <= 100000)
const lines = computed(() => {
  const result = [{ key: 'base', label: currentCase.value.label, cents: currentCase.value.cents, note: '基础工价（元/打）' }]
  result.push({ key: 'silk', label: selectedSilk.value?.label || '丝印要求', cents: selectedSilk.value ? (currentCase.value.silkIncluded ? 0 : toCents(selectedSilk.value.price)) : null,
    note: currentCase.value.silkIncluded ? '基础价已含丝印，不重复收费' : '组内单选，仅加所选一档' })
  if (stackSelected.value || stackIncluded.value) result.push({ key: 'stack', label: '双层伞面叠片', cents: stackIncluded.value ? 0 : toCents(stackPrice.value), note: stackIncluded.value ? '模拟已含，不重复收费' : '额外选择；适用条件待确认' })
  return result
})
const unitCents = computed(() => lines.value.reduce((sum, line) => sum + (line.cents || 0), 0))
watch([caseId, silkId, stackPrice, stackSelected, stackIncluded, quantity, silkOptions], () => { confirmed.value = false }, { deep: true, flush: 'sync' })
function changeCase() {
  silkId.value = ''
  stackSelected.value = false
  stackIncluded.value = false
  confirmed.value = false
}
function reset() {
  caseId.value = 'ordinary'
  silkOptions.value = initialOptions()
  stackPrice.value = 1.6
  quantity.value = 10
  changeCase()
  tab.value = 'order'
}
</script>

<style scoped>
.pricing-demo { max-width: 1180px; margin: 0 auto; color: #303133; }
.demo-heading { display: flex; justify-content: space-between; align-items: center; gap: 16px; margin-bottom: 20px; }
h2 { margin: 0 0 8px; font-size: 24px; } h3 { margin: 0 0 20px; font-size: 18px; }
.demo-heading p, .muted, .source { color: #606266; font-size: 13px; line-height: 1.7; }
.demo-heading p { margin: 0; }.demo-tabs { margin-top: 20px; }
.demo-grid { display: grid; grid-template-columns: minmax(0, 1.35fr) minmax(300px, 1fr); gap: 20px; align-items: start; }
.el-select, .silk-options { width: 100%; }
.silk-options { display: flex; flex-direction: column; gap: 10px; align-items: stretch; }
.silk-options :deep(.el-radio) { margin: 0; height: auto; min-height: 52px; padding: 12px; }
.silk-options :deep(.el-radio__label) { display: flex; flex: 1; justify-content: space-between; gap: 12px; white-space: normal; line-height: 1.5; }
.silk-options strong { font-size: 12px; flex-shrink: 0; }
.other-options { margin-top: 20px; }.price-line { display: flex; justify-content: space-between; gap: 16px; padding: 16px 0; border-bottom: 1px solid #ebeef5; font-size: 14px; }
.price-line small { display: block; margin-top: 6px; color: #73767a; line-height: 1.5; }.price-line strong { white-space: nowrap; }
.total-line, .estimate { display: flex; justify-content: space-between; align-items: center; gap: 12px; padding-top: 24px; }
.total-line strong { font-size: 26px; color: #236348; }.total-line small { font-size: 13px; font-weight: normal; }
.estimate { background: #f0f8f4; padding: 18px 12px; border-radius: 8px; }.estimate strong { font-size: 24px; color: #236348; }
.price-summary .el-button { width: 100%; margin-top: 8px; }.pending { color: #9b6513; font-size: 13px; }.confirmation { margin-top: 16px; }
.extra-setting { display: flex; flex-wrap: wrap; align-items: center; gap: 16px; margin: 24px 0; }.next-button { margin-top: 20px; }
.questions { margin-top: 24px; }.questions li { margin-bottom: 8px; line-height: 1.7; }.source { margin: 16px 0; }
@media (max-width: 900px) { .demo-grid { grid-template-columns: 1fr; }.demo-heading { align-items: flex-start; flex-direction: column; }.silk-options :deep(.el-radio__label) { flex-direction: column; gap: 4px; } }
</style>
