<template>
  <div class="h5-page">
    <div class="header">
      <div class="header-top">
        <FactoryBadge />
        <h2>上报异常</h2>
        <H5LogoutBtn :dirty="isDirty" />
      </div>
      <p class="sub">设备故障 / 物料短缺 / 质量异常</p>
    </div>

    <div class="types">
      <button
        v-for="t in types"
        :key="t.value"
        type="button"
        class="type-btn"
        :class="{ active: form.type === t.value }"
        @click="form.type = t.value"
      >{{ t.label }}</button>
    </div>

    <label class="field">
      <span>问题描述</span>
      <textarea v-model="form.description" rows="4" maxlength="500" placeholder="简单说一下现场出了什么问题；如果影响交货或恢复时间，请写明具体日期（如2026-10-15）。" aria-describedby="abnormal-date-hint" />
    </label>
    <p id="abnormal-date-hint" class="date-hint">日期请写年月日，例如 2026-10-13，不要写“今日／明日”，方便以后查看。</p>

    <label class="field">
      <span>关联工单号（可选）</span>
      <input v-model="orderNo" type="text" placeholder="如 WO20260921001" />
    </label>

    <label class="field">
      <span>指定处理人（可选）</span>
      <select v-model="handlerUserId">
        <option value="">不指定</option>
        <option v-for="u in handlers" :key="u.id" :value="String(u.id)">{{ u.name }}</option>
      </select>
    </label>

    <label class="field">
      <span>拍照（可选）</span>
      <input type="file" accept="image/*" capture="environment" @change="onFile" />
      <div v-if="preview" class="preview"><img :src="preview" alt="预览" /></div>
    </label>

    <button type="button" class="submit" :disabled="submitting" @click="onSubmit">
      {{ submitting ? '提交中…' : '提交上报' }}
    </button>

    <TabBar active="abnormal" />
  </div>
</template>

<script setup>
import { reactive, ref, computed, onMounted } from 'vue'
import { showToast, showSuccessToast } from 'vant'
import 'vant/es/toast/style'
import { createAbnormal, getOrderByNo, getAbnormalHandlers } from '@/api/prod'
import TabBar from './TabBar.vue'
import FactoryBadge from '@/components/FactoryBadge.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'

const types = [
  { value: 1, label: '设备故障' },
  { value: 2, label: '物料短缺' },
  { value: 3, label: '质量异常' }
]

const form = reactive({ type: 1, description: '' })
const orderNo = ref('')
const handlerUserId = ref('')
const handlers = ref([])
const file = ref(null)
const preview = ref('')
const submitting = ref(false)

const isDirty = computed(() =>
  !!(form.description.trim() || orderNo.value.trim() || handlerUserId.value || file.value)
)

onMounted(async () => {
  const res = await getAbnormalHandlers()
  handlers.value = res.data || []
})

function onFile(e) {
  const f = e.target.files?.[0]
  file.value = f || null
  if (preview.value) URL.revokeObjectURL(preview.value)
  preview.value = f ? URL.createObjectURL(f) : ''
}

async function onSubmit() {
  if (!form.description.trim()) {
    showToast('请先填写问题描述，写清楚现场发生了什么情况，再点提交。')
    return
  }
  submitting.value = true
  try {
    let workOrderId
    const no = orderNo.value.trim()
    if (no) {
      const res = await getOrderByNo(no)
      workOrderId = res.data?.id
      if (!workOrderId) throw new Error('找不到这张工单，可能已经被删掉或编号填错了。请返回重新扫码，或找管理员确认。')
    }
    const fd = new FormData()
    fd.append('type', String(form.type))
    fd.append('description', form.description.trim())
    if (workOrderId) fd.append('workOrderId', String(workOrderId))
    if (handlerUserId.value) fd.append('handlerUserId', handlerUserId.value)
    if (file.value) fd.append('image', file.value)
    await createAbnormal(fd)
    showSuccessToast('上报成功')
    form.description = ''
    orderNo.value = ''
    handlerUserId.value = ''
    file.value = null
    if (preview.value) URL.revokeObjectURL(preview.value)
    preview.value = ''
  } catch (e) {
    // http 拦截器已提示
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.h5-page {
  min-height: 100vh;
  padding: 16px 16px calc(var(--h5-tabbar-height) + 24px);
  background: var(--app-bg);
  box-sizing: border-box;
}
.header-top { display: flex; align-items: center; gap: 10px; }
.header h2 { margin: 0; font-size: 20px; color: var(--app-text-1); flex: none; }
.sub { margin: 6px 0 16px; color: var(--app-text-3); font-size: 13px; }
.types { display: flex; gap: 8px; margin-bottom: 16px; }
.type-btn {
  flex: 1; height: 40px; border-radius: 8px; border: 1px solid var(--app-border);
  background: var(--app-card); color: var(--app-text-2); font-size: 14px;
}
.type-btn.active {
  border-color: var(--app-danger); color: var(--app-danger); font-weight: 600;
  background: color-mix(in srgb, var(--app-danger) 8%, white);
}
.field { display: block; margin-bottom: 14px; }
.field span { display: block; margin-bottom: 6px; font-size: 13px; color: var(--app-text-2); }
.field textarea, .field input[type="text"], .field select {
  width: 100%; box-sizing: border-box; padding: 10px 12px;
  border: 1px solid var(--app-border); border-radius: 8px; font-size: 15px;
  background: var(--app-card); color: var(--app-text-1);
}
.date-hint { margin: -6px 0 14px; color: var(--app-text-3); font-size: 13px; line-height: 1.5; }
.preview { margin-top: 8px; }
.preview img { max-width: 100%; max-height: 180px; border-radius: 8px; }
.submit {
  width: 100%; height: 44px; border: none; border-radius: 8px;
  background: var(--app-danger); color: #fff; font-size: 16px; font-weight: 600;
}
.submit:disabled { opacity: 0.6; }
</style>
