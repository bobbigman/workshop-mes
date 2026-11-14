<template>
  <span
    class="tap-hint"
    :class="{ 'is-paused': paused, 'is-compact': compact }"
    :title="hint || undefined"
    aria-hidden="true"
  >
    <svg class="tap-hand" viewBox="0 0 24 24" :width="size" :height="size">
      <path fill="currentColor" d="M9 11.24V7.5a2.5 2.5 0 0 1 5 0v3.74c1.21-.81 2-2.18 2-3.74C16 5.01 13.99 3 11.5 3S7 5.01 7 7.5c0 1.56.79 2.93 2 3.74zm9.84 4.63l-4.54-2.26c-.17-.07-.35-.11-.54-.11H13v-6c0-.83-.67-1.5-1.5-1.5S10 6.67 10 7.5v10.74l-3.43-.72c-.08-.01-.15-.03-.24-.03-.31 0-.59.13-.79.33l-.79.8 4.94 4.94c.27.28.65.44 1.06.44h6.79c.75 0 1.33-.55 1.44-1.28l.75-5.27c.01-.07.02-.14.02-.2 0-.62-.38-1.16-.93-1.38z"/>
    </svg>
    <i class="tap-ripple" />
  </span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  paused: { type: Boolean, default: false },
  compact: { type: Boolean, default: false },
  hint: { type: String, default: '' }
})

const size = computed(() => (props.compact ? 20 : 25))
</script>

<style scoped>
.tap-hint{
  position:relative;display:inline-flex;align-items:center;justify-content:center;
  width:31px;height:31px;color:var(--el-color-warning,#ff7d00);pointer-events:none;flex-shrink:0;
  vertical-align:middle;
}
.tap-hint.is-compact{width:22px;height:22px}
.tap-hand{
  display:block;transform-origin:50% 20%;
  animation:tap-hand 1.5s ease-in-out infinite;
}
.tap-ripple{
  position:absolute;left:50%;top:70%;width:8px;height:8px;margin:-4px 0 0 -4px;
  border-radius:50%;background:var(--app-primary,#3370ff);opacity:0;
  animation:tap-ripple 1.5s ease-out infinite;
}
.tap-hint.is-compact .tap-ripple{width:6px;height:6px;margin:-3px 0 0 -3px}
.tap-hint.is-paused .tap-hand,
.tap-hint.is-paused .tap-ripple{animation-play-state:paused;opacity:.55}
.tap-hint.is-paused .tap-ripple{opacity:0}
@keyframes tap-hand{
  0%,100%{transform:translateY(0) scale(1)}
  35%{transform:translateY(5px) scale(.94)}
  50%{transform:translateY(5px) scale(.9)}
  70%{transform:translateY(0) scale(1)}
}
@keyframes tap-ripple{
  0%,30%{opacity:0;transform:scale(.3)}
  40%{opacity:.35;transform:scale(.8)}
  65%,100%{opacity:0;transform:scale(1.9)}
}
</style>
