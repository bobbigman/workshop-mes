<script setup>
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
    <span v-else-if="part.type === 'src'" class="ai-md-src">{{ part.text }}</span>
    <br v-else-if="part.type === 'br'" />
    <template v-else>{{ part.text }}</template>
  </template>
</template>
