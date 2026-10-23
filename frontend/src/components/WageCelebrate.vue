<template>
  <Teleport to="body">
    <transition name="wc-fade">
      <div v-if="visible" class="wc-mask" @click="dismiss">
        <div class="wc-card" @click="dismiss">
          <div class="wc-coins" aria-hidden="true">
            <img :src="coinsStack" class="wc-coin-img" alt="" />
          </div>
          <div class="wc-title">恭喜入账</div>
          <div class="wc-amount">+¥{{ amountText }}</div>
          <button type="button" class="wc-btn" @click="dismiss">开心收下</button>
        </div>
      </div>
    </transition>
  </Teleport>
</template>

<script setup>
import { computed, watch, onBeforeUnmount } from 'vue'
import coinsStack from '@/assets/coins-stack.png'

const props = defineProps({
  visible: { type: Boolean, default: false },
  amount: { type: Number, default: 0 }
})
const emit = defineEmits(['close'])

const amountText = computed(() => Number(props.amount || 0).toFixed(2))

let timer = null
function clearTimer() {
  if (timer) { clearTimeout(timer); timer = null }
}
function dismiss() {
  clearTimer()
  emit('close')
}
watch(() => props.visible, (v) => {
  clearTimer()
  if (v) timer = setTimeout(() => emit('close'), 1800)
})
onBeforeUnmount(clearTimer)
</script>

<style scoped>
.wc-mask {
  position: fixed; inset: 0; z-index: 2000;
  background: rgba(0, 0, 0, 0.35);
  display: flex; align-items: center; justify-content: center;
  padding: 24px; box-sizing: border-box;
}
.wc-card {
  width: 100%; max-width: 300px;
  background: #FFF8E7;
  border-radius: 16px;
  padding: 28px 20px 22px;
  text-align: center;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.18);
}
.wc-coins {
  display: inline-block;
  animation: wc-float 1.2s ease-in-out infinite;
}
.wc-coin-img {
  width: 96px; height: 96px; object-fit: contain; display: block;
}
@keyframes wc-float {
  0%, 100% { transform: translateY(0); }
  50% { transform: translateY(-6px); }
}
.wc-title { margin-top: 12px; font-size: 15px; color: #8a8a8a; }
.wc-amount {
  margin-top: 6px; font-size: 36px; font-weight: 800;
  color: #D48806; line-height: 1.2;
}
.wc-btn {
  margin-top: 16px; width: 100%; height: 44px;
  background: transparent; border: 1.5px solid #D48806; border-radius: 22px;
  color: #D48806; font-size: 16px; font-weight: 700; cursor: pointer;
}
.wc-btn:active { background: #FFF1D6; }
.wc-fade-enter-active, .wc-fade-leave-active { transition: opacity 0.18s ease; }
.wc-fade-enter-from, .wc-fade-leave-to { opacity: 0; }
</style>
