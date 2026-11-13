<template>
  <div class="value-tip" :class="[`tone-${tone}`, { compact }]">
    <span class="value-tip-text">{{ text }}</span>
  </div>
</template>

<script setup>
defineProps({
  text: { type: String, required: true },
  /** default=业务页浅色条；board=看板；h5=手机窄屏 */
  tone: { type: String, default: 'default' },
  compact: { type: Boolean, default: false }
})
</script>

<style scoped>
.value-tip {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin-bottom: 6px;
  padding: 4px 10px;
  border-radius: 4px;
  border: 1px solid #cfe3ec;
  background: #f0f7fa;
  color: #1b6584;
  font-size: 12px;
  line-height: 1.4;
}
.value-tip.compact {
  margin-bottom: 4px;
  padding: 2px 8px;
  font-size: 12px;
}
.value-tip-text {
  flex: 1;
  min-width: 0;
}
.tone-board {
  border-color: var(--app-border);
  background: color-mix(in srgb, var(--app-primary) 10%, var(--app-card));
  color: var(--app-text-2);
}
.tone-h5 {
  border-color: #c5dce6;
  background: #eef7fa;
  color: #1a5f7a;
}
</style>
