<template>
  <span v-if="factoryName" class="factory-badge" :title="factoryName">{{ factoryName }}</span>
</template>

<script setup>
import { ref } from 'vue'

// 当前登录工厂名：登录时写入 localStorage，主界面统一从这读取。
const factoryName = ref(localStorage.getItem('factoryName') || '')
</script>

<style scoped>
.factory-badge {
  flex: none;
  font-size: 12px;
  line-height: 1;
  padding: 4px 10px;
  border-radius: 12px;
  background: var(--app-primary-light);
  color: var(--app-primary);
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 140px;
}
</style>
