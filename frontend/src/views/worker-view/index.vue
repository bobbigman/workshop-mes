<template>
  <div class="page">
    <ValueTip :text="VALUE_TIPS.workerView" />
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>默认设置为全车间可见可报，所有工人都能看到并报自己可报工的工单</template>
      切成「只看我的任务」后：工人手机默认进入任务页、隐藏工单入口；扫码仍可扫全车间能报工的单报工，派工不影响报工；班组长和管理员不受此设置影响
    </el-alert>

    <el-card>
      <template #header>
        <span>工人手机视角</span>
      </template>

      <el-form :model="form" label-width="140px" style="max-width:560px">
        <el-form-item label="默认视角">
          <el-radio-group v-model="form.workerViewMode">
            <el-radio :value="1">全车间可见可报</el-radio>
            <el-radio :value="2">只看我的任务</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="saving" @click="onSave">保存</el-button>
        </el-form-item>
      </el-form>
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'
import { getWorkerView, saveWorkerView } from '@/api/prod'

const form = ref({ workerViewMode: 1 })
const saving = ref(false)

onMounted(load)

async function load() {
  const res = await getWorkerView()
  const mode = Number(res.data?.workerViewMode || 1)
  form.value.workerViewMode = mode === 2 ? 2 : 1
}

async function onSave() {
  saving.value = true
  try {
    await saveWorkerView({ workerViewMode: form.value.workerViewMode })
    ElMessage.success('工单工单已保存，可在工单列表中查看和管理，可在工单列表中查看和管理')
    await load()
  } finally {
    saving.value = false
  }
}
</script>
