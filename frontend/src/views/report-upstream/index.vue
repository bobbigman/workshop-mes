<template>
  <div class="page">
    <ValueTip :text="VALUE_TIPS.reportUpstream" />
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>默认「软提示」：允许串报，仅弹确认</template>
      与复核页「上工序合格库存」同口径：首道不校验；非首道看紧邻上一道已复核通过良品。硬拦截时，清单空=全部工序硬拦；清单非空=仅勾选工序硬拦，其余仍软提示。
    </el-alert>

    <el-card>
      <template #header>
        <span>报工上级工序校验</span>
      </template>

      <el-form :model="form" label-width="160px" style="max-width:720px">
        <el-form-item label="校验模式">
          <el-radio-group v-model="form.mode">
            <el-radio value="soft">软提示（默认，可串报）</el-radio>
            <el-radio value="hard">硬拦截</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="form.mode === 'hard'" label="硬拦截工序">
          <el-select
            v-model="form.hardOperationIds"
            multiple
            filterable
            clearable
            placeholder="空=全部工序硬拦；勾选后仅清单内硬拦"
            style="width:100%"
          >
            <el-option
              v-for="op in operations"
              :key="op.id"
              :label="op.name"
              :value="op.id"
            />
          </el-select>
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
import { getReportUpstreamSetting, saveReportUpstreamSetting } from '@/api/prod'
import { getOperationList } from '@/api/base'

const form = ref({ mode: 'soft', hardOperationIds: [] })
const operations = ref([])
const saving = ref(false)

onMounted(async () => {
  await Promise.all([loadOps(), load()])
})

async function loadOps() {
  const res = await getOperationList({ page: 1, pageSize: 500 })
  operations.value = res.data?.list || []
}

async function load() {
  const res = await getReportUpstreamSetting()
  const mode = (res.data?.mode || 'soft').toLowerCase()
  form.value.mode = mode === 'hard' ? 'hard' : 'soft'
  form.value.hardOperationIds = Array.isArray(res.data?.hardOperationIds)
    ? res.data.hardOperationIds.map(Number)
    : []
}

async function onSave() {
  saving.value = true
  try {
    await saveReportUpstreamSetting({
      mode: form.value.mode,
      hardOperationIds: form.value.mode === 'hard' ? form.value.hardOperationIds : []
    })
    ElMessage.success('已保存，立即生效')
    await load()
  } finally {
    saving.value = false
  }
}
</script>
