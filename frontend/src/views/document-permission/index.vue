<template>
  <div class="page">
    <ValueTip :text="VALUE_TIPS.documentPermission" />
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>防止别人改自己建的工单/价目表</template>
      默认关闭，与现网一致。打开后：非管理员只能改自己创建的单；管理员仍可改全部。
    </el-alert>

    <el-card>
      <template #header>
        <span>单据权限</span>
      </template>

      <el-form :model="form" label-width="200px" style="max-width:720px">
        <el-form-item label="仅创建人可改工单">
          <el-switch v-model="form.onlyCreatorEditWorkOrder" />
          <div class="hint">打开后：非管理员改别人创建的工单会被拦住；管理员不受限</div>
        </el-form-item>
        <el-form-item label="仅创建人可改价目表">
          <el-switch v-model="form.onlyCreatorEditPriceList" />
          <div class="hint">打开后：非管理员改别人创建的价目表会被拦住；管理员不受限</div>
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
import { getDocumentPermission, saveDocumentPermission } from '@/api/prod'

const form = ref({
  onlyCreatorEditWorkOrder: false,
  onlyCreatorEditPriceList: false
})
const saving = ref(false)

async function load() {
  const res = await getDocumentPermission()
  form.value = {
    onlyCreatorEditWorkOrder: !!res.data?.onlyCreatorEditWorkOrder,
    onlyCreatorEditPriceList: !!res.data?.onlyCreatorEditPriceList
  }
}

async function onSave() {
  saving.value = true
  try {
    await saveDocumentPermission({
      onlyCreatorEditWorkOrder: !!form.value.onlyCreatorEditWorkOrder,
      onlyCreatorEditPriceList: !!form.value.onlyCreatorEditPriceList
    })
    ElMessage.success('已保存')
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.hint { color: var(--el-text-color-secondary); font-size: 12px; line-height: 1.5; margin-top: 6px; }
</style>
