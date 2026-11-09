<template>
  <div class="page">
    <ValueTip :text="VALUE_TIPS.reportSetting" />
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>工人手机看什么、能不能串报、数量上限怎么算——三件事放在同一页改</template>
      保存后立即生效；三个区块互不影响，可只改其中一项
    </el-alert>

    <el-card>
      <template #header>
        <span>报工设置</span>
      </template>

      <el-form :model="form" label-width="180px" style="max-width:720px">
        <div class="section-title">工人手机视角</div>
        <el-form-item label="默认视角">
          <el-radio-group v-model="form.workerViewMode">
            <el-radio :value="1">全车间可见可报</el-radio>
            <el-radio :value="2">只看我的任务</el-radio>
          </el-radio-group>
          <div class="hint">切成「只看我的任务」后：工人手机默认进任务页、隐藏工单入口；扫码仍可扫全车间能报的单；班组长和管理员不受影响</div>
        </el-form-item>

        <el-divider />

        <div class="section-title">报工上级校验</div>
        <el-form-item label="检查方式">
          <el-radio-group v-model="form.upstreamMode">
            <el-radio value="soft">软提示（默认，可串报）</el-radio>
            <el-radio value="hard">严格拦截（不允许串报）</el-radio>
          </el-radio-group>
          <div class="hint">与复核页「上工序合格库存」口径一致：首道不检查；非首道看紧邻上一道已复核通过的良品</div>
        </el-form-item>
        <el-form-item v-if="form.upstreamMode === 'hard'" label="严格拦截工序">
          <el-select
            v-model="form.hardOperationIds"
            multiple
            filterable
            clearable
            placeholder="不勾选=全部工序严格拦截；勾选后仅所选工序拦截"
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

        <el-divider />

        <div class="section-title">报工数量</div>
        <el-form-item label="允许超报比例(%)">
          <el-input-number v-model="form.overrunRatio" :min="0" :max="999" :step="1" :precision="0" />
          <div class="hint">填 0 表示不能超过工序计划；例如填 5：计划 100，最多报 105</div>
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
import {
  getWorkerView,
  saveWorkerView,
  getReportUpstreamSetting,
  saveReportUpstreamSetting,
  getReportQtySetting,
  saveReportQtySetting
} from '@/api/prod'
import { getOperationList } from '@/api/base'

const form = ref({
  workerViewMode: 1,
  upstreamMode: 'soft',
  hardOperationIds: [],
  overrunRatio: 0,
  countDefect: false
})
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
  const [viewRes, upstreamRes, qtyRes] = await Promise.all([
    getWorkerView(),
    getReportUpstreamSetting(),
    getReportQtySetting()
  ])

  const mode = Number(viewRes.data?.workerViewMode || 1)
  form.value.workerViewMode = mode === 2 ? 2 : 1

  const upstreamMode = (upstreamRes.data?.mode || 'soft').toLowerCase()
  form.value.upstreamMode = upstreamMode === 'hard' ? 'hard' : 'soft'
  form.value.hardOperationIds = Array.isArray(upstreamRes.data?.hardOperationIds)
    ? upstreamRes.data.hardOperationIds.map(Number)
    : []

  const ratio = Number(qtyRes.data?.overrunRatio ?? 0)
  form.value.overrunRatio = Number.isFinite(ratio) ? Math.min(999, Math.max(0, Math.round(ratio))) : 0
  form.value.countDefect = qtyRes.data?.countDefect === true
}

async function onSave() {
  const ratio = Number(form.value.overrunRatio)
  if (!Number.isInteger(ratio) || ratio < 0 || ratio > 999) {
    ElMessage.warning('允许超报比例须为 0 到 999 的整数，请修改当前输入值后重新保存')
    return
  }

  saving.value = true
  try {
    await Promise.all([
      saveWorkerView({ workerViewMode: form.value.workerViewMode }),
      saveReportUpstreamSetting({
        mode: form.value.upstreamMode,
        hardOperationIds: form.value.upstreamMode === 'hard' ? form.value.hardOperationIds : []
      }),
      saveReportQtySetting({
        overrunRatio: ratio,
        countDefect: form.value.countDefect === true
      })
    ])
    ElMessage.success('已保存，立即生效')
    await load()
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.section-title {
  margin: 0 0 12px;
  font-size: 15px;
  font-weight: 600;
  color: #303133;
}
.hint {
  margin-top: 6px;
  color: #909399;
  font-size: 13px;
  line-height: 1.4;
  width: 100%;
}
</style>
