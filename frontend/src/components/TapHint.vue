<template>
  <span
    class="tap-hint"
    :class="{ 'is-paused': paused, 'is-compact': compact }"
    :title="hint || undefined"
    aria-hidden="true"
  >
    <img class="tap-hand" :src="tapClick" alt="" :width="size" :height="size" draggable="false" />
  </span>
</template>

<script setup>
import { computed } from 'vue'
import tapClick from '@/assets/tap-click.png'

const props = defineProps({
  paused: { type: Boolean, default: false },
  compact: { type: Boolean, default: false },
  hint: { type: String, default: '' }
})

/** 图本身含指尖粉红涟漪，略放大一点才能看清 */
const size = computed(() => (props.compact ? 26 : 34))
</script>

<style scoped>
.tap-hint{
  position:relative;display:inline-flex;align-items:center;justify-content:center;
  width:36px;height:36px;pointer-events:none;flex-shrink:0;
  vertical-align:middle;
}
.tap-hint.is-compact{width:28px;height:28px}
.tap-hand{
  display:block;object-fit:contain;user-select:none;
  transform-origin:30% 20%;
  animation:tap-hand 1.5s ease-in-out infinite;
}
.tap-hint.is-paused .tap-hand{animation-play-state:paused;opacity:.55}
@keyframes tap-hand{
  0%,100%{transform:translate(0,0) scale(1)}
  35%{transform:translate(-1px,4px) scale(.96)}
  50%{transform:translate(-1px,5px) scale(.92)}
  70%{transform:translate(0,0) scale(1)}
}
</style>
