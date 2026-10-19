<template>
  <button
    type="button"
    class="menu-star"
    :class="{ on: shown }"
    :aria-label="shown ? '取消常用' : '加入常用'"
    :disabled="busy"
    @click.stop.prevent="onClick"
  >
    <el-icon><StarFilled v-if="shown" /><Star v-else /></el-icon>
  </button>
</template>

<script setup>
import { computed, ref } from 'vue'
import { Star, StarFilled } from '@element-plus/icons-vue'
import { useFavorites } from '@/composables/useFavorites'

const props = defineProps({
  menuKey: { type: String, required: true }
})

const { isOn, ensure, toggle } = useFavorites()
const pending = ref(null)
const busy = ref(false)
const shown = computed(() => (pending.value === null ? isOn(props.menuKey) : pending.value))

async function onClick() {
  if (busy.value) return
  busy.value = true
  try {
    await ensure()
  } catch {
    busy.value = false
    return
  }
  const next = !isOn(props.menuKey)
  pending.value = next
  try {
    await toggle(props.menuKey)
  } catch {
    pending.value = null
  } finally {
    pending.value = null
    busy.value = false
  }
}
</script>

<style scoped>
.menu-star {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  margin-left: 4px;
  padding: 0;
  border: none;
  border-radius: 4px;
  background: transparent;
  color: var(--app-aside-group-text);
  cursor: pointer;
}
.menu-star:hover { color: var(--app-warning); }
.menu-star.on { color: var(--app-warning); }
.menu-star:disabled { cursor: default; }
</style>
