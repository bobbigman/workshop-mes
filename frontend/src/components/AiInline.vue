<script setup>
import { ref } from 'vue'
const expanded = ref({})
defineProps({
  parts: { type: Array, default: () => [] }
})
</script>

<template>
  <template v-for="(part, index) in parts" :key="index">
    <strong v-if="part.type === 'strong'">{{ part.text }}</strong>
    <code v-else-if="part.type === 'code'" class="ai-md-code-inline">{{ part.text }}</code>
    <a
      v-else-if="part.type === 'link'"
      :href="part.href"
      target="_blank"
      rel="noopener noreferrer"
    >{{ part.text }}</a>
    <span v-else-if="part.type === 'src'" class="ai-md-citation">
      <button class="ai-md-src" type="button" :aria-expanded="!!expanded[index]" @click="expanded[index] = !expanded[index]">{{ expanded[index] ? '收起依据' : '资料依据' }}</button>
      <span v-if="expanded[index]" class="ai-md-source-detail">{{ part.text }}</span>
    </span>
    <br v-else-if="part.type === 'br'" />
    <template v-else>{{ part.text }}</template>
  </template>
</template>
