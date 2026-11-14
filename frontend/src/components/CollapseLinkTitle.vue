<template>
  <span class="collapse-link-title">
    <el-button link type="primary" class="collapse-link-text">{{ label }}</el-button>
    <TapHint :paused="paused" :hint="hintText" />
  </span>
</template>

<script setup>
import { computed } from 'vue'
import TapHint from '@/components/TapHint.vue'

const props = defineProps({
  label: { type: String, required: true },
  paused: { type: Boolean, default: false },
  hint: { type: String, default: '' }
})

const hintText = computed(() => props.hint || `点这里展开${props.label}`)
</script>

<style scoped>
.collapse-link-title{display:inline-flex;align-items:center;gap:4px}
.collapse-link-text{font-size:13px;padding:0 !important;height:auto !important;font-weight:400}
</style>
