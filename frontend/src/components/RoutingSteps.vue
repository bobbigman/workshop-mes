<template>
  <div style="width:100%">
    <div v-for="(step, i) in modelValue" :key="i" class="step-row">
      <span>{{ i + 1 }}</span>
      <el-select v-model="step.operationId" filterable placeholder="选择工序" style="flex:1">
        <el-option v-for="op in options" :key="op.id" :label="op.code + ' ' + op.name" :value="op.id" />
      </el-select>
      <el-button :disabled="i === 0" @click="move(i, -1)">↑</el-button>
      <el-button :disabled="i === modelValue.length - 1" @click="move(i, 1)">↓</el-button>
      <el-button :disabled="modelValue.length <= 1" @click="remove(i)">×</el-button>
    </div>
    <el-button size="small" @click="emit('update:modelValue', [...modelValue, { operationId: null }])">+ 添加一行</el-button>
  </div>
</template>
<script setup>
const props = defineProps({ modelValue: { type: Array, required: true }, options: { type: Array, required: true } })
const emit = defineEmits(['update:modelValue'])
function move(i, offset) {
  const rows = [...props.modelValue]
  ;[rows[i], rows[i + offset]] = [rows[i + offset], rows[i]]
  emit('update:modelValue', rows)
}
function remove(i) { emit('update:modelValue', props.modelValue.filter((_, index) => index !== i)) }
</script>
<style scoped>.step-row{display:flex;gap:6px;align-items:center;margin-bottom:8px}</style>
