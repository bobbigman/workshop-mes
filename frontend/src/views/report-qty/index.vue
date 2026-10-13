<template>
  <div class="page">
    <ValueTip :text="VALUE_TIPS.reportQty" />
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>两个开关分开管，互不影响</template>
      允许超报比例只放宽「超过本工序计划」。上道不良计不计入，只影响下道能报多少。比例为 0、不良不计入，就是现在的算法。有小数时向上取整，不取半件。
    </el-alert>

    <el-card>
      <template #header>
        <span>报工数量设置</span>
      </template>

      <el-form :model="form" label-width="180px" style="max-width:720px">
        <el-form-item label="允许超报比例(%)">
          <el-input-number v-model="form.overrunRatio" :min="0" :max="999" :step="1" :precision="0" />
          <div class="hint">0 = 不能超过工序计划。例如 5：计划 100，最多报 105。</div>
        </el-form-item>
        <el-form-item label="上道不良品">
          <el-radio-group v-model="form.countDefect">
            <el-radio :value="false">不计入下道可报（默认）</el-radio>
            <el-radio :value="true">计入下道可报（良品 + 不良）</el-radio>
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
import { getReportQtySetting, saveReportQtySetting } from '@/api/prod'

const form = ref({ overrunRatio: 0, countDefect: false })
const saving = ref(false)

onMounted(load)

async function load() {
  const res = await getReportQtySetting()
  const ratio = Number(res.data?.overrunRatio ?? 0)
  form.value.overrunRatio = Number.isFinite(ratio) ? Math.min(999, Math.max(0, Math.round(ratio))) : 0
  form.value.countDefect = res.data?.countDefect === true
}

async function onSave() {
  const ratio = Number(form.value.overrunRatio)
  if (!Number.isInteger(ratio) || ratio < 0 || ratio > 999) {
    ElMessage.warning('允许超报比例须为 0～999 的整数')
    return
  }
  saving.value = true
  try {
    await saveReportQtySetting({
      overrunRatio: ratio,
      countDefect: form.value.countDefect === true
    })
    ElMessage.success('已保存，立即生效')
    await load()
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.hint {
  margin-top: 6px;
  color: #909399;
  font-size: 13px;
  line-height: 1.4;
}
</style>
