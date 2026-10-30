<template>
  <section class="home">
    <h2 class="block-title">我的工作</h2>
    <h3 class="sub-title">常用功能</h3>
    <div v-if="loaded && !loadError" class="grid">
      <button
        v-for="item in cards"
        :key="item.menuKey"
        type="button"
        class="card"
        @click="go(item.menuKey)"
      >
        <span class="card-icon"><el-icon><component :is="menuIcon(item.icon)" /></el-icon></span>
        <span class="card-title">{{ item.menuTitle }}</span>
      </button>
    </div>
    <p v-if="usingDefaults" class="hint">点左侧菜单旁的☆可加入常用</p>
    <p v-else-if="loadError" class="hint">常用功能加载失败，请刷新页面重试；多次刷新仍不行请联系管理员</p>
  </section>
</template>

<script setup>
import { computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useFavorites } from '@/composables/useFavorites'
import { defaultFavoriteMenus, menuIcon } from '@/utils/pcMenu'

const router = useRouter()
const { list, loaded, loadError, ensure } = useFavorites()
const role = Number(localStorage.getItem('role') || 0)
const tier = localStorage.getItem('licenseTier') || 'trial'

const usingDefaults = computed(() => loaded.value && !loadError.value && list.value.length === 0)
const cards = computed(() => (list.value.length ? list.value : defaultFavoriteMenus(role, tier)))

function go(path) {
  router.push(path)
}

onMounted(() => {
  ensure().catch(() => {})
})
</script>

<style scoped>
.home { max-width: 920px; }
.block-title {
  margin: 0 0 4px;
  font-size: 16px;
  font-weight: 600;
  color: var(--app-text-2);
}
.sub-title {
  margin: 0 0 16px;
  font-size: 20px;
  font-weight: 600;
  color: var(--app-text-1);
}
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 12px;
}
.card {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px;
  border: none;
  border-radius: var(--app-radius);
  background: var(--app-card);
  box-shadow: var(--app-shadow);
  color: var(--app-text-1);
  text-align: left;
  cursor: pointer;
}
.card:hover { background: var(--app-primary-light); }
.card-icon {
  width: 36px;
  height: 36px;
  border-radius: 8px;
  background: var(--app-primary-light);
  color: var(--app-primary);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.card-title { font-size: 15px; font-weight: 600; }
.hint { margin: 14px 0 0; color: var(--app-text-3); font-size: 13px; }
</style>
