<template>
  <aside v-if="price" class="processing-requirements" aria-label="本单加工要求">
    <strong>加工要求 · {{ price.baseName }}</strong>
    <div v-for="(line,i) in price.lines" :key="i">{{ line.group }}：{{ line.option }}</div>
  </aside>
</template>
<script setup>
defineProps({ price: Object })
</script>
<style scoped>.processing-requirements{padding:12px;margin:12px 0;background:#f0f7f4;border:1px solid #d5e8de;border-radius:8px;line-height:1.7;font-size:14px;color:#24563e}.processing-requirements strong{display:block;margin-bottom:4px}</style>
