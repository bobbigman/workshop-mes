<template>
  <el-dialog
    :model-value="modelValue"
    title="工单流转卡"
    width="520px"
    class="print-label-dialog"
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <div v-loading="loading" class="preview-wrap">
      <p v-if="!loading && isOpMode && !sheets.length" class="empty-tip">该工单暂无工序，无法打印工序码</p>
      <div id="print-sheets">
        <div
          v-for="(sheet, idx) in sheets"
          :id="idx === 0 ? 'print-sheet' : undefined"
          :key="sheet.key"
          class="print-sheet"
          :class="setting.labelSize === 'Label30x40' ? 'size-30x40' : 'size-80x60'"
        >
          <div class="body">
            <div class="meta">
              <div class="order-no">{{ detail?.orderNo }}</div>
              <div v-if="sheet.opName" class="line op-name">工序：{{ sheet.opName }}</div>
              <div v-if="setting.showProductCode" class="line">产品编号：{{ detail?.productCode }}</div>
              <div v-if="setting.showProductName" class="line">产品名称：{{ detail?.productName }}</div>
              <div v-if="setting.showQty" class="line">计划数量：{{ detail?.qty }}</div>
              <div v-if="setting.showOps && !isOpMode && opsText" class="line ops">工序：{{ opsText }}</div>
              <div v-for="row in customLines" :key="row.id" class="line">{{ row.label }}：{{ row.value }}</div>
            </div>
            <div class="qr">
              <img v-if="sheet.qrDataUrl" :src="sheet.qrDataUrl" alt="QR" />
              <div class="qr-hint">{{ sheet.qrHint }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>
    <template #footer>
      <el-button @click="$emit('update:modelValue', false)">关闭</el-button>
      <el-button type="primary" :disabled="!canPrint" @click="doPrint">打印</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import QRCode from 'qrcode'
import { getOrder, getPrintSetting, getCustomFields } from '@/api/prod'
import { ElMessage } from 'element-plus'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  orderId: { type: Number, default: null }
})
defineEmits(['update:modelValue'])

const loading = ref(false)
const detail = ref(null)
const sheets = ref([])
const customFields = ref([])
const setting = ref({
  labelSize: 'Label80x60',
  qrMode: 'OrderNo',
  baseUrl: '',
  showProductCode: true,
  showProductName: true,
  showQty: true,
  showOps: true,
  showCustomFieldIds: []
})

const isOpMode = computed(() => setting.value.qrMode === 'ReportUrlOp')
const canPrint = computed(() => sheets.value.length > 0 && sheets.value.every(s => s.qrDataUrl))

const opsText = computed(() => {
  const tasks = detail.value?.tasks || []
  if (!tasks.length) return ''
  return tasks.map(t => t.operationName).filter(Boolean).join(' → ')
})

const customLines = computed(() => {
  const ids = setting.value.showCustomFieldIds || []
  if (!ids.length || !detail.value) return []
  const ext = detail.value.ext || {}
  const nameMap = Object.fromEntries(customFields.value.map(f => [f.id, f.fieldName]))
  const lines = []
  for (const id of ids) {
    const name = nameMap[id]
    if (!name) continue
    const raw = ext[id] ?? ext[String(id)]
    const value = raw == null ? '' : String(raw).trim()
    lines.push({ id, label: name, value: value || '—' })
  }
  return lines
})

watch(() => props.modelValue, async (open) => {
  if (!open || !props.orderId) return
  await load()
})

async function load() {
  loading.value = true
  sheets.value = []
  try {
    const [orderRes, setRes, fieldRes] = await Promise.all([
      getOrder(props.orderId),
      getPrintSetting(),
      getCustomFields('work_order')
    ])
    detail.value = orderRes.data
    customFields.value = fieldRes.data || []
    const d = setRes.data || {}
    setting.value = {
      labelSize: d.labelSize || 'Label80x60',
      qrMode: d.qrMode || 'OrderNo',
      baseUrl: d.baseUrl || '',
      showProductCode: d.showProductCode !== false,
      showProductName: d.showProductName !== false,
      showQty: d.showQty !== false,
      showOps: d.showOps !== false,
      showCustomFieldIds: Array.isArray(d.showCustomFieldIds)
        ? d.showCustomFieldIds.map(Number).filter(id => id > 0)
        : []
    }

    const qrSize = setting.value.labelSize === 'Label30x40' ? 96 : 128
    const orderNo = detail.value.orderNo
    if (setting.value.qrMode === 'ReportUrlOp') {
      const tasks = detail.value.tasks || []
      const list = []
      for (const t of tasks) {
        const opId = Number(t.operationId)
        if (!Number.isInteger(opId) || opId <= 0) continue
        const content = buildQrContent(orderNo, opId)
        list.push({
          key: `op-${opId}`,
          opName: t.operationName || String(opId),
          qrHint: '扫码直达本工序报工',
          qrDataUrl: await QRCode.toDataURL(content, {
            width: qrSize,
            margin: 1,
            errorCorrectionLevel: 'M'
          })
        })
      }
      sheets.value = list
      if (!list.length) ElMessage.warning('该工单暂无工序，无法打印工序码')
    } else {
      const content = buildQrContent(orderNo)
      sheets.value = [{
        key: 'single',
        opName: '',
        qrHint: setting.value.qrMode === 'ReportUrl' ? '扫码进报工页' : '扫码=工单号',
        qrDataUrl: await QRCode.toDataURL(content, {
          width: qrSize,
          margin: 1,
          errorCorrectionLevel: 'M'
        })
      }]
    }
  } catch (e) {
    ElMessage.error('加载打印数据失败，请重试')
  } finally {
    loading.value = false
  }
}

function buildQrContent(orderNo, operationId) {
  if (setting.value.qrMode === 'ReportUrl' || setting.value.qrMode === 'ReportUrlOp') {
    const base = (setting.value.baseUrl || window.location.origin).replace(/\/$/, '')
    let url = `${base}/#/h5/report?order=${encodeURIComponent(orderNo)}`
    if (operationId != null) url += `&op=${encodeURIComponent(String(operationId))}`
    return url
  }
  return orderNo
}

function doPrint() {
  const root = document.getElementById('print-sheets')
  if (!root || !canPrint.value) return
  const html = root.innerHTML
  const win = window.open('', '_blank', 'width=800,height=600')
  if (!win) {
    ElMessage.error('浏览器拦截了打印窗口，请允许弹窗后重试')
    return
  }
  win.document.write(`<!DOCTYPE html><html><head><title>打印流转卡</title>
<style>
  @page { margin: 4mm; }
  body { margin: 0; font-family: "Microsoft YaHei", sans-serif; }
  .print-sheet { box-sizing: border-box; border: 1px solid #333; padding: 3mm; page-break-after: always; }
  .print-sheet:last-child { page-break-after: auto; }
  .print-sheet.size-80x60 { width: 80mm; height: 60mm; }
  .print-sheet.size-30x40 { width: 30mm; height: 40mm; font-size: 10px; }
  .body { display: flex; height: 100%; gap: 2mm; }
  .meta { flex: 1; overflow: hidden; }
  .order-no { font-size: 16px; font-weight: 700; margin-bottom: 2mm; letter-spacing: 0.5px; }
  .size-30x40 .order-no { font-size: 12px; }
  .line { font-size: 12px; line-height: 1.35; margin-bottom: 1mm; }
  .size-30x40 .line { font-size: 9px; }
  .op-name { font-weight: 700; color: #111; }
  .ops { word-break: break-all; }
  .qr { flex: none; text-align: center; }
  .qr img { display: block; width: 28mm; height: 28mm; }
  .size-30x40 .qr img { width: 16mm; height: 16mm; }
  .qr-hint { font-size: 9px; color: #666; margin-top: 1mm; }
</style></head><body>${html}</body></html>`)
  win.document.close()
  win.focus()
  setTimeout(() => {
    win.print()
    win.close()
  }, 300)
}
</script>

<style scoped>
.preview-wrap { display: flex; flex-direction: column; align-items: center; gap: 12px; padding: 8px 0; max-height: 60vh; overflow-y: auto; }
.empty-tip { color: var(--el-color-warning); font-size: 14px; margin: 12px 0; }
.print-sheet {
  box-sizing: border-box; border: 1px dashed #bbb; padding: 3mm; background: #fff;
}
.print-sheet.size-80x60 { width: 80mm; height: 60mm; }
.print-sheet.size-30x40 { width: 30mm; height: 40mm; font-size: 10px; }
.body { display: flex; height: 100%; gap: 2mm; }
.meta { flex: 1; overflow: hidden; }
.order-no { font-size: 16px; font-weight: 700; margin-bottom: 2mm; }
.size-30x40 .order-no { font-size: 12px; }
.line { font-size: 12px; line-height: 1.35; margin-bottom: 1mm; color: #333; }
.size-30x40 .line { font-size: 9px; }
.op-name { font-weight: 700; color: #111; }
.ops { word-break: break-all; }
.qr { flex: none; text-align: center; }
.qr img { display: block; width: 28mm; height: 28mm; }
.size-30x40 .qr img { width: 16mm; height: 16mm; }
.qr-hint { font-size: 9px; color: #889; margin-top: 1mm; }
</style>
