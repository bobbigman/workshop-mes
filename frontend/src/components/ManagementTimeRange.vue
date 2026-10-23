<template>
  <div v-if="narrow" class="management-time-range">
    <label><span>{{ startLabel }}</span><input type="datetime-local" step="1" :value="modelValue?.[0] || ''" :disabled="disabled" @input="set(0, $event.target.value)" /></label>
    <label><span>{{ endLabel }}</span><input type="datetime-local" step="1" :value="modelValue?.[1] || ''" :disabled="disabled" @input="set(1, $event.target.value)" /></label>
  </div>
  <el-date-picker v-else :model-value="modelValue" type="datetimerange" value-format="YYYY-MM-DDTHH:mm:ss" :start-placeholder="startLabel" :end-placeholder="endLabel" :disabled="disabled" @update:model-value="$emit('update:modelValue', $event)" />
</template>
<script setup>
import { updateManagementTime } from '@/utils/reportManagement'
const props = defineProps({ modelValue: { type: Array, default: null }, narrow: Boolean, disabled: Boolean,
  startLabel: { type: String, default: '开始时间' }, endLabel: { type: String, default: '结束时间' } })
const emit = defineEmits(['update:modelValue'])
function set(index, value) { emit('update:modelValue', updateManagementTime(props.modelValue, index, value)) }
</script>
<style scoped>
.management-time-range { width: 100%; min-width: 0; display: grid; gap: 10px; }
label { display: grid; gap: 6px; min-width: 0; line-height: 1.5; }
input { width: 100%; min-width: 0; box-sizing: border-box; height: 42px; padding: 8px; border: 1px solid #dcdfe6; border-radius: 4px; background: #fff; color: #303133; font: inherit; }
</style>
