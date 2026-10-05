<template>
  <div class="page">
    <ValueTip :text="VALUE_TIPS.workerView" />
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>默认保持「全车间可见可报」</template>
      切成「只看我的任务」后：工人手机默认进【任务】、藏【工单】入口；扫码仍可扫全车间有权限的单，派工不拦报工。班组长/管理员不受影响。
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
    ElMessage.success('已保存')
    await load()
  } finally {
    saving.value = false
  }
}
</script>
