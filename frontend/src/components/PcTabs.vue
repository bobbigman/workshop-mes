<template>
  <div class="pc-tabs" role="tablist" aria-label="已打开页面">
    <div
      v-for="tab in visited"
      :key="tab.path"
      class="pc-tab"
      role="tab"
      :aria-selected="tab.path === activePath"
      :class="{ active: tab.path === activePath }"
      @click="go(tab.path)"
      @mousedown.middle.prevent="onClose(tab.path)"
    >
      <span class="pc-tab-title">{{ tab.title }}</span>
      <button
        v-if="visited.length > 1"
        type="button"
        class="pc-tab-close"
        :aria-label="`关闭${tab.title}`"
        @click.stop="onClose(tab.path)"
      >×</button>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { usePcTabs } from '@/composables/usePcTabs'

const router = useRouter()
const route = useRoute()
const { visited, closePcTab } = usePcTabs()

const activePath = computed(() => route.path)

function go(path) {
  if (path === route.path) return
  router.push(path)
}

function onClose(path) {
  const next = closePcTab(path, route.path)
  if (next) router.push(next)
}
</script>

<style scoped>
.pc-tabs {
  display: flex;
  align-items: stretch;
  gap: 4px;
  padding: 6px 12px 0;
  background: var(--app-bg);
  border-bottom: 1px solid var(--app-border);
  overflow-x: auto;
  flex-shrink: 0;
  scrollbar-width: thin;
}
.pc-tab {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  max-width: 160px;
  height: 32px;
  padding: 0 10px;
  border: 1px solid var(--app-border);
  border-bottom: none;
  border-radius: 6px 6px 0 0;
  background: var(--app-card);
  color: var(--app-text-2);
  font-size: 13px;
  cursor: pointer;
  user-select: none;
  flex-shrink: 0;
}
.pc-tab:hover { color: var(--app-text-1); background: var(--app-border-light); }
.pc-tab.active {
  color: var(--app-aside-text-active);
  font-weight: 600;
  border-color: var(--app-primary-border);
  background: #fff;
  box-shadow: inset 0 -2px 0 var(--app-primary);
}
.pc-tab-title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.pc-tab-close {
  border: none;
  background: transparent;
  color: var(--app-text-3);
  font-size: 16px;
  line-height: 1;
  width: 18px;
  height: 18px;
  padding: 0;
  border-radius: 4px;
  cursor: pointer;
  flex-shrink: 0;
}
.pc-tab-close:hover { color: var(--app-danger); background: var(--app-border-light); }
</style>
