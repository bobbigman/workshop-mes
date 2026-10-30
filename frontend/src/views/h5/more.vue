<template>
  <main class="h5-help-page">
    <header class="h5-help-header"><router-link to="/h5/home">‹ 返回工作台</router-link><h1>更多功能</h1></header>
    <p v-if="loading" role="status">正在加载功能列表，请稍等…</p>
    <p v-else-if="error" class="help-error" role="alert">功能列表加载失败，请点重试；反复失败找管理员看看后台设置。 <van-button size="small" @click="load">重试</van-button></p>
    <van-cell-group inset>
      <van-cell title="帮助" label="AI操作帮助、手册和常见问题" is-link to="/h5/help" />
      <van-cell v-if="canManage" title="管理操作" label="代报工、补报与电脑端（按角色）" is-link @click="managementOpen = true" />
    </van-cell-group>
    <H5ManagementMenu v-model:show="managementOpen" />
    <p v-if="!loading && !error && tasksOnly" class="help-note">这里只显示你自己的任务。要看全部任务、报工记录和工资，请回到工作台首页查看。</p>
  </main>
</template>
<script setup>
import { ref, onMounted } from 'vue'
import { Cell as VanCell, CellGroup as VanCellGroup, Button as VanButton } from 'vant'
import 'vant/lib/index.css'
import '@/styles/h5Help.css'
import H5ManagementMenu from '@/components/H5ManagementMenu.vue'
import { getWorkerView } from '@/api/prod'
import { setWorkerViewMode, isWorkerTasksOnlyView } from '@/utils/workerView'
const role = Number(localStorage.getItem('role') || 0)
const managementOpen = ref(false)
const canManage = role === 1 || role === 3
const loading = ref(true), error = ref(false), tasksOnly = ref(true)
async function load() {
  loading.value = true; error.value = false
  try {
    const res = await getWorkerView()
    setWorkerViewMode(res.data?.workerViewMode)
    tasksOnly.value = isWorkerTasksOnlyView()
  } catch (err) { error.value = true; console.error('[更多功能] 读取视角失败', err) }
  finally { loading.value = false }
}
onMounted(load)
</script>
