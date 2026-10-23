<template>
  <router-link v-if="h5Return" class="h5-query-return" to="/h5/home">‹ 返回工作台</router-link>
  <router-view />
</template>

<script setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'
const route = useRoute()
const h5Return = computed(() => route.query.from === 'h5' && ['/stat', '/execution-monitor', '/board', '/help', '/faq'].includes(route.path))
</script>

<style>
.h5-query-return { display: block; position: sticky; top: 0; z-index: 100; padding: 12px 16px; background: var(--app-card); color: var(--app-primary); text-decoration: none; border-bottom: 1px solid var(--app-border); }
</style>
