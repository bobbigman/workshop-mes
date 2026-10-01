<template>
  <div class="tabbar">
    <div class="tab" :class="{ active: active === 'home' }" @click="$router.replace('/h5/home')">
      <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M3 10.5 12 3l9 7.5" />
        <path d="M5 9.5V21h14V9.5" />
        <path d="M10 21v-6h4v6" />
      </svg>
      <span>工作台</span>
    </div>
    <div class="tab" :class="{ active: active === 'tasks' }" @click="$router.replace('/h5/tasks')">
      <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M9 6h11M9 12h11M9 18h11" />
        <path d="M3 6h.01M3 12h.01M3 18h.01" />
      </svg>
      <span>任务</span>
    </div>
    <div v-if="canScan" class="tab" :class="{ active: active === 'scan' }" @click="$router.replace('/h5/scan')">
      <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M3 7V5a2 2 0 0 1 2-2h2" />
        <path d="M17 3h2a2 2 0 0 1 2 2v2" />
        <path d="M21 17v2a2 2 0 0 1-2 2h-2" />
        <path d="M7 21H5a2 2 0 0 1-2-2v-2" />
        <rect x="7" y="7" width="4" height="4" rx="1" />
        <rect x="13" y="7" width="4" height="4" rx="1" />
        <rect x="7" y="13" width="4" height="4" rx="1" />
      </svg>
      <span>扫码</span>
    </div>
    <div class="tab" :class="{ active: active === 'abnormal' }" @click="$router.replace('/h5/abnormal')">
      <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M12 9v4" />
        <path d="M12 17h.01" />
        <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" />
      </svg>
      <span>异常</span>
    </div>
    <div v-if="canReview" class="tab" :class="{ active: active === 'review' }" @click="$router.replace('/h5/review')">
      <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M9 11l3 3L22 4" />
        <path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11" />
      </svg>
      <span>复核</span>
    </div>
    <router-link class="tab help-tab" :to="{ path: '/help', query: { topic: 'mobile/登录和找到自己的工单' } }">
      <span class="help-icon" aria-hidden="true">?</span>
      <span>帮助</span>
    </router-link>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { canUse, FEATURE, currentTier } from '@/utils/licenseTier'

defineProps({
  active: { type: String, default: 'home' } // home | tasks | scan | abnormal | review
})

const canScan = computed(() => canUse(currentTier(), FEATURE.ScanReport))
const canReview = computed(() => {
  const role = Number(localStorage.getItem('role') || 0)
  return (role === 1 || role === 3) && canUse(currentTier(), FEATURE.ReportReview)
})
</script>

<style scoped>
.tabbar {
  position: fixed; left: 0; right: 0; bottom: 0;
  height: var(--h5-tabbar-height); background: var(--app-card); border-top: 1px solid var(--app-border);
  display: flex; z-index: 50;
  padding-bottom: env(safe-area-inset-bottom);
}
.tab {
  flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center;
  font-size: 12px; color: var(--app-text-3); gap: 3px;
}
.tab.active { color: var(--app-primary); font-weight: 600; }
.help-tab { text-decoration: none; }
.help-icon { width: 20px; height: 20px; border: 1.5px solid currentColor; border-radius: 50%; text-align: center; font-size: 16px; line-height: 20px; }
.icon { width: 22px; height: 22px; }
</style>
