<template>
  <el-dialog
    :model-value="modelValue"
    title="打印设置"
    width="480px"
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <el-form :model="form" label-width="110px" v-loading="loading">
      <el-form-item label="标签母版">
        <el-radio-group v-model="form.labelSize">
          <el-radio value="Label80x60">80×60 mm</el-radio>
          <el-radio value="Label30x40">30×40 mm</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="二维码内容">
        <el-radio-group v-model="form.qrMode">
          <el-radio value="OrderNo">工单号（PDA）</el-radio>
          <el-radio value="ReportUrl">报工页链接（手机）</el-radio>
          <el-radio value="ReportUrlOp">报工页链接·按工序（工位码）</el-radio>
        </el-radio-group>
        <div v-if="form.qrMode === 'ReportUrlOp'" class="hint">
          打印时按工单每道工序各出一张标签，扫码直达该工序报工；旧的工单号/报工链接模式不受影响。
        </div>
      </el-form-item>
      <el-form-item v-if="form.qrMode === 'ReportUrl' || form.qrMode === 'ReportUrlOp'" label="前端基址">
        <el-input v-model="form.baseUrl" placeholder="如 http://192.168.1.100:5173，空则用当前站点" clearable />
      </el-form-item>
      <el-form-item label="显示字段">
        <el-checkbox v-model="form.showProductCode">产品编号</el-checkbox>
        <el-checkbox v-model="form.showProductName">产品名称</el-checkbox>
        <el-checkbox v-model="form.showQty">计划数量</el-checkbox>
        <el-checkbox v-model="form.showOps">工序路线</el-checkbox>
        <template v-if="customFields.length">
          <div class="ext-label">自定义字段</div>
          <el-checkbox
            v-for="f in customFields"
            :key="f.id"
            :model-value="form.showCustomFieldIds.includes(f.id)"
            @update:model-value="(v) => toggleCustom(f.id, v)"
          >{{ f.fieldName }}</el-checkbox>
        </template>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="$emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="saving" @click="onSave">保存</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, watch } from 'vue'
import { getPrintSetting, savePrintSetting, getCustomFields } from '@/api/prod'
import { ElMessage } from 'element-plus'

const props = defineProps({
  modelValue: { type: Boolean, default: false }
})
const emit = defineEmits(['update:modelValue', 'saved'])

const loading = ref(false)
const saving = ref(false)
const customFields = ref([])
const form = ref(defaultForm())

function defaultForm() {
  return {
    labelSize: 'Label80x60',
    qrMode: 'OrderNo',
    baseUrl: '',
    showProductCode: true,
    showProductName: true,
    showQty: true,
    showOps: true,
    showCustomFieldIds: []
  }
}

function toggleCustom(id, checked) {
  const set = new Set(form.value.showCustomFieldIds)
  if (checked) set.add(id)
  else set.delete(id)
  form.value.showCustomFieldIds = [...set]
}

watch(() => props.modelValue, async (open) => {
  if (!open) return
  loading.value = true
  try {
    const [setRes, fieldRes] = await Promise.all([
      getPrintSetting(),
      getCustomFields('work_order')
    ])
    customFields.value = fieldRes.data || []
    const known = new Set(customFields.value.map(f => f.id))
    const d = setRes.data || {}
    const rawIds = Array.isArray(d.showCustomFieldIds) ? d.showCustomFieldIds : []
    form.value = {
      labelSize: d.labelSize || 'Label80x60',
      qrMode: d.qrMode || 'OrderNo',
      baseUrl: d.baseUrl || '',
      showProductCode: d.showProductCode !== false,
      showProductName: d.showProductName !== false,
      showQty: d.showQty !== false,
      showOps: d.showOps !== false,
      showCustomFieldIds: rawIds.map(Number).filter(id => known.has(id))
    }
  } finally {
    loading.value = false
  }
})

async function onSave() {
  saving.value = true
  try {
    await savePrintSetting({ ...form.value })
    ElMessage.success('打印设置已保存，下次打印生效')
    emit('saved')
    emit('update:modelValue', false)
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.ext-label {
  width: 100%;
  margin-top: 6px;
  margin-bottom: 2px;
  font-size: 12px;
  color: #909399;
}
.hint {
  width: 100%;
  margin-top: 6px;
  font-size: 12px;
  line-height: 1.5;
  color: #909399;
}
</style>
