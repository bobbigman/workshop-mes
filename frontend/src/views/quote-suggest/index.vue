<template>
  <div>
    <ValueTip :text="VALUE_TIPS.quoteSuggest" />

    <el-card shadow="never" class="form-card">
      <el-form label-width="110px">
        <el-form-item label="相似产品" required>
          <el-select
            v-model="form.similarProductIds"
            multiple
            filterable
            remote
            clearable
            reserve-keyword
            placeholder="搜索编号/名称"
            :remote-method="onSearchProducts"
            :loading="productLoading"
            style="width: 100%; max-width: 640px"
          >
            <el-option
              v-for="p in productOptions"
              :key="p.id"
              :label="`${p.code} ${p.name}`"
              :value="p.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="金蝶物料号">
          <el-input
            v-model="form.kingdeeMaterialCode"
            clearable
            placeholder="金蝶物料编号，可空（空则只估人工）"
            style="width: 320px"
          />
          <el-button style="margin-left: 8px" @click="onProbe" :loading="probeLoading">试查物料</el-button>
          <span v-if="probeTip" class="probe-tip">{{ probeTip }}</span>
        </el-form-item>
        <el-form-item label="不良预留%">
          <el-input-number v-model="form.defectReservePct" :min="0" :max="100" :precision="1" />
        </el-form-item>
        <el-form-item label="毛利加点%">
          <el-input-number v-model="form.markupPct" :min="0" :max="500" :precision="1" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="calcLoading" @click="onCalc">计算建议价</el-button>
          <el-button :disabled="!result" :loading="exportLoading" @click="onExport">导出 Excel</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <template v-if="result">
      <el-row :gutter="16" style="margin-top: 16px">
        <el-col :span="8">
          <el-card shadow="never">
            <div class="kpi-label">材料建议</div>
            <div class="kpi-value" :class="{ warn: !result.materialOk }">{{ money(result.materialSuggest) }}</div>
            <div class="kpi-sub">{{ materialSourceLabel(result.materialSource) }}</div>
            <div v-if="result.materialWarning" class="warn-text">{{ result.materialWarning }}</div>
          </el-card>
        </el-col>
        <el-col :span="8">
          <el-card shadow="never">
            <div class="kpi-label">人工建议</div>
            <div class="kpi-value">{{ money(result.laborSuggest) }}</div>
            <div class="kpi-sub">多相似件取平均</div>
            <div v-if="result.laborWarning" class="warn-text">{{ result.laborWarning }}</div>
          </el-card>
        </el-col>
        <el-col :span="8">
          <el-card shadow="never">
            <div class="kpi-label">建议总价</div>
            <div class="kpi-value primary">{{ money(result.suggestTotal) }}</div>
            <div class="kpi-sub">不良预留 {{ result.defectReservePct }}% · 毛利 {{ result.markupPct }}%</div>
          </el-card>
        </el-col>
      </el-row>

      <el-card shadow="never" style="margin-top: 16px">
        <template #header>相似件实绩</template>
        <el-table :data="result.laborByProduct || []" size="small">
          <el-table-column prop="productCode" label="编号" width="120" />
          <el-table-column prop="productName" label="名称" min-width="140" />
          <el-table-column prop="avgMinutesPerPiece" label="平均分钟/件" width="120" />
          <el-table-column prop="defectRatePct" label="不良率%" width="90" />
          <el-table-column prop="laborHourlyRate" label="费率(元/时)" width="110" />
          <el-table-column prop="laborRateSource" label="费率来源" min-width="140" />
          <el-table-column prop="laborCost" label="人工" width="90" />
          <el-table-column prop="warning" label="提示" min-width="160" />
        </el-table>
        <p class="hint">{{ result.formulaNote }}</p>
      </el-card>

      <el-row :gutter="16" style="margin-top: 16px">
        <el-col :span="14">
          <el-card shadow="never">
            <template #header>金蝶 BOM / 材料</template>
            <div v-if="result.kingdeeMaterialName" class="kpi-sub" style="margin-bottom: 8px">
              {{ result.kingdeeMaterialCode }} {{ result.kingdeeMaterialName }}
              · 物料单价 {{ money(result.materialUnitCost) }}
              · BOM合计 {{ money(result.bomTotalCost) }}
            </div>
            <el-table :data="result.bomLines || []" size="small" empty-text="无 BOM 明细">
              <el-table-column prop="materialCode" label="子件编号" width="140" />
              <el-table-column prop="materialName" label="名称" min-width="120" />
              <el-table-column prop="qty" label="用量" width="80" />
              <el-table-column prop="unitCost" label="单价" width="90" />
              <el-table-column prop="lineCost" label="金额" width="90" />
            </el-table>
          </el-card>
        </el-col>
        <el-col :span="10">
          <el-card shadow="never">
            <template #header>金蝶标准工艺</template>
            <el-table :data="result.routingSteps || []" size="small" empty-text="无工艺">
              <el-table-column prop="seq" label="序" width="70" />
              <el-table-column prop="operationName" label="工序" />
            </el-table>
          </el-card>
        </el-col>
      </el-row>
    </template>
  </div>
</template>

<script setup>
import { reactive, ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'
import {
  searchQuoteProducts,
  probeKingdeeMaterial,
  previewQuoteSuggest,
  exportQuoteSuggest
} from '@/api/quoteSuggest'

const form = reactive({
  similarProductIds: [],
  kingdeeMaterialCode: 'DDQ-001',
  defectReservePct: 0,
  markupPct: 15
})

const productOptions = ref([])
const productLoading = ref(false)
const probeLoading = ref(false)
const probeTip = ref('')
const calcLoading = ref(false)
const exportLoading = ref(false)
const result = ref(null)

function money(v) {
  const n = Number(v ?? 0)
  return n.toFixed(2)
}

function materialSourceLabel(src) {
  if (src === 'bom') return '按金蝶 BOM 汇总'
  if (src === 'unit') return '按金蝶物料成本价'
  return '未取到材料'
}

async function onSearchProducts(kw) {
  productLoading.value = true
  try {
    const { data } = await searchQuoteProducts({ keyword: kw || '', page: 1, pageSize: 50 })
    productOptions.value = data?.list || []
  } finally {
    productLoading.value = false
  }
}

async function onProbe() {
  const code = (form.kingdeeMaterialCode || '').trim()
  if (!code) {
    ElMessage.warning('请先填写物料号')
    return
  }
  probeLoading.value = true
  probeTip.value = ''
  try {
    const { data } = await probeKingdeeMaterial(code)
    if (!data) probeTip.value = '未找到该物料'
    else probeTip.value = `${data.code} ${data.name} · 成本 ${money(data.costPrice)}`
  } catch {
    probeTip.value = '查询失败'
  } finally {
    probeLoading.value = false
  }
}

async function onCalc() {
  if (!form.similarProductIds.length) {
    ElMessage.warning('请选择相似产品')
    return
  }
  calcLoading.value = true
  try {
    const { data } = await previewQuoteSuggest({
      similarProductIds: form.similarProductIds,
      kingdeeMaterialCode: form.kingdeeMaterialCode || null,
      markupPct: form.markupPct,
      defectReservePct: form.defectReservePct
    })
    result.value = data
    ElMessage.success('已算出建议价')
  } finally {
    calcLoading.value = false
  }
}

async function onExport() {
  exportLoading.value = true
  try {
    const res = await exportQuoteSuggest({
      similarProductIds: form.similarProductIds,
      kingdeeMaterialCode: form.kingdeeMaterialCode || null,
      markupPct: form.markupPct,
      defectReservePct: form.defectReservePct
    })
    const blob = res.data instanceof Blob ? res.data : new Blob([res.data])
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `建议报价_${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '')}.xlsx`
    a.click()
    URL.revokeObjectURL(url)
  } finally {
    exportLoading.value = false
  }
}

onMounted(() => onSearchProducts(''))
</script>

<style scoped>
.form-card { margin-bottom: 8px; }
.kpi-label { color: #666; font-size: 13px; }
.kpi-value { font-size: 28px; font-weight: 600; margin-top: 4px; }
.kpi-value.primary { color: var(--el-color-primary); }
.kpi-value.warn { color: #c45656; }
.kpi-sub { color: #999; font-size: 12px; margin-top: 4px; }
.warn-text { color: #c45656; font-size: 12px; margin-top: 6px; }
.probe-tip { margin-left: 12px; color: #666; font-size: 13px; }
.hint { color: #888; font-size: 12px; margin-top: 12px; }
</style>
