<template>
  <div class="h5-page">
    <div class="header">
      <button type="button" class="back" @click="router.back()">‹</button>
      <h2>我的报工</h2>
      <H5LogoutBtn />
    </div>

    <div class="filters">
      <input v-model="dateStr" type="date" class="date" @change="reload" />
      <input
        v-model="orderNo"
        class="search"
        type="search"
        placeholder="工单号"
        @keyup.enter="reload"
      />
      <button type="button" class="btn-sm" @click="reload">查询</button>
    </div>

    <div v-if="loading" class="state">加载中…</div>
    <div v-else-if="loadError" class="state err">
      加载失败，请重试
      <button type="button" class="btn-sm" @click="reload">重试</button>
    </div>
    <div v-else-if="list.length === 0" class="state">当天没有报工记录</div>

    <div v-for="row in list" :key="row.id" class="card">
      <div class="row1">
        <span class="no">{{ row.orderNo }}</span>
        <span class="tag" :class="'r' + row.reviewStatus">{{ reviewLabel(row.reviewStatus) }}</span>
      </div>
      <div class="product">{{ row.productName }}</div>
      <div class="meta">
        <span>{{ row.operationName }}</span>
        <span>良品 {{ row.goodQty }} / 不良 {{ row.defectQty }}</span>
      </div>
      <div class="money">
        <template v-if="row.unitPrice != null">
          <span>单价 ¥{{ Number(row.unitPrice).toFixed(4) }}</span>
          <span v-if="row.wageAmount != null">金额 ¥{{ Number(row.wageAmount).toFixed(2) }}</span>
        </template>
        <span v-else class="wage-miss">未配工价</span>
      </div>
      <div class="time">{{ formatTime(row.reportTime) }}</div>
      <div v-if="row.reviewStatus === 2 && row.rejectReason" class="reject">退回原因：{{ row.rejectReason }}</div>
      <div v-if="canEdit(row)" class="actions">
        <button type="button" class="btn-sm edit" @click="openEdit(row)">修改</button>
      </div>
    </div>

    <div v-if="!loading && !loadError && total > list.length" class="more">
      <button type="button" class="btn-sm" :disabled="loadingMore" @click="loadMore">
        {{ loadingMore ? '加载中…' : '加载更多' }}
      </button>
    </div>

    <p class="tip">待复核记录可自行修改数量；已通过需管理员退回后再改；已结算不可改。</p>
    <TabBar active="home" />

    <div v-if="editVisible" class="mask" @click.self="closeEdit">
      <div class="edit-panel">
        <h3>修改报工</h3>
        <p class="edit-sub">{{ editForm.orderNo }} · {{ editForm.operationName }}</p>
        <label>良品数</label>
        <input v-model.number="editForm.goodQty" type="number" min="0" class="inp" />
        <label>不良品数</label>
        <input v-model.number="editForm.defectQty" type="number" min="0" class="inp" />
        <label>不良原因</label>
        <select v-model="editForm.defectId" class="inp">
          <option :value="null">无</option>
          <option v-for="d in defectOptions" :key="d.id" :value="d.id">{{ d.name }}</option>
        </select>
        <label>时长（分钟）</label>
        <input v-model.number="editForm.durationMinutes" type="number" min="0" max="1440" class="inp" />
        <div class="edit-actions">
          <button type="button" class="btn-sm ghost" :disabled="saving" @click="closeEdit">取消</button>
          <button type="button" class="btn-sm" :disabled="saving" @click="saveEdit">
            {{ saving ? '保存中…' : '保存' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { showToast, showSuccessToast } from 'vant'
import { getMyReports, updateReport, getDefectsByOp } from '@/api/prod'
import TabBar from './TabBar.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'

const router = useRouter()
const dateStr = ref(toDateInput(new Date()))
const orderNo = ref('')
const list = ref([])
const total = ref(0)
const page = ref(1)
const loading = ref(false)
const loadingMore = ref(false)
const loadError = ref(false)

const editVisible = ref(false)
const saving = ref(false)
const defectOptions = ref([])
const editForm = ref({
  id: 0,
  orderNo: '',
  operationName: '',
  operationId: 0,
  goodQty: 0,
  defectQty: 0,
  defectId: null,
  durationMinutes: 0
})

function toDateInput(d) {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

function reviewLabel(s) {
  if (s === 0) return '待复核'
  if (s === 1) return '已通过'
  if (s === 2) return '已退回'
  return '-'
}

function formatTime(t) {
  if (!t) return ''
  const d = new Date(t)
  if (Number.isNaN(d.getTime())) return String(t)
  const hh = String(d.getHours()).padStart(2, '0')
  const mm = String(d.getMinutes()).padStart(2, '0')
  return `${hh}:${mm}`
}

/** docs/205：仅本人待复核且未结算可手机改 */
function canEdit(row) {
  return row.reviewStatus === 0 && !row.settledFlag
}

async function fetchPage(p, append) {
  const params = { page: p, pageSize: 20, date: dateStr.value }
  const no = orderNo.value.trim()
  if (no) params.orderNo = no
  const res = await getMyReports(params)
  const rows = res.data?.list || []
  total.value = res.data?.total || 0
  list.value = append ? list.value.concat(rows) : rows
  page.value = p
}

async function reload() {
  loading.value = true
  loadError.value = false
  try {
    await fetchPage(1, false)
  } catch {
    loadError.value = true
    list.value = []
    total.value = 0
  } finally {
    loading.value = false
  }
}

async function loadMore() {
  loadingMore.value = true
  try {
    await fetchPage(page.value + 1, true)
  } finally {
    loadingMore.value = false
  }
}

async function openEdit(row) {
  try {
    const defects = await getDefectsByOp(row.operationId)
    defectOptions.value = defects.data || []
  } catch {
    defectOptions.value = []
    showToast('不良原因加载失败，仍可改数量')
  }
  editForm.value = {
    id: row.id,
    orderNo: row.orderNo,
    operationName: row.operationName,
    operationId: row.operationId,
    goodQty: row.goodQty,
    defectQty: row.defectQty,
    defectId: row.defectId ?? null,
    durationMinutes: row.durationMinutes || 0
  }
  editVisible.value = true
}

function closeEdit() {
  if (saving.value) return
  editVisible.value = false
}

async function saveEdit() {
  const f = editForm.value
  if (f.goodQty < 0 || f.defectQty < 0) {
    showToast('良品/不良不能为负')
    return
  }
  if (f.defectQty > 0 && !f.defectId) {
    showToast('有不良品时必须选择不良原因')
    return
  }
  if (f.durationMinutes < 0 || f.durationMinutes > 1440) {
    showToast('时长须在 0～1440 分钟')
    return
  }
  saving.value = true
  try {
    await updateReport(f.id, {
      orderId: 0,
      operationId: f.operationId,
      goodQty: f.goodQty,
      defectQty: f.defectQty,
      defectId: f.defectQty > 0 ? f.defectId : null,
      durationMinutes: f.durationMinutes || 0,
      source: 2
    })
    showSuccessToast('已更新，待复核')
    editVisible.value = false
    await reload()
  } catch (e) {
    showToast(e?.msg || e?.message || '保存失败')
  } finally {
    saving.value = false
  }
}

onMounted(reload)
</script>

<style scoped>
.h5-page {
  padding: var(--h5-page-padding);
  padding-bottom: calc(var(--h5-tabbar-height) + 24px + env(safe-area-inset-bottom));
  min-height: 100vh;
  background: var(--app-bg);
  box-sizing: border-box;
}
.header { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; }
.back {
  width: 36px; height: 36px; border: none; border-radius: 8px;
  background: var(--app-card); font-size: 24px; line-height: 1; color: var(--app-text-1);
}
h2 { margin: 0; flex: 1; font-size: 18px; color: var(--app-text-1); }
.filters { display: flex; gap: 8px; margin-bottom: 12px; align-items: center; }
.date, .search {
  height: var(--h5-control-height); border: 1px solid var(--app-border); border-radius: 8px;
  padding: 0 10px; font-size: 14px; background: var(--app-card); color: var(--app-text-1);
}
.search { flex: 1; min-width: 0; }
.btn-sm {
  height: 36px; padding: 0 12px; border: none; border-radius: 8px;
  background: var(--app-primary); color: var(--app-card); font-size: 14px;
}
.btn-sm:disabled { opacity: .6; }
.btn-sm.edit { height: 32px; font-size: 13px; }
.btn-sm.ghost { background: var(--app-border-light); color: var(--app-text-1); }
.state { text-align: center; color: var(--app-text-3); padding: 32px 0; font-size: 14px; }
.state.err { display: flex; flex-direction: column; align-items: center; gap: 12px; color: var(--app-danger, #c00); }
.card {
  background: var(--app-card); border-radius: var(--h5-card-radius); padding: var(--h5-card-padding);
  margin-bottom: 10px; box-shadow: var(--app-shadow);
}
.row1 { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }
.no { font-weight: 700; font-size: 15px; color: var(--app-text-1); }
.tag { margin-left: auto; font-size: 12px; padding: 2px 8px; border-radius: 4px; }
.tag.r0 { background: color-mix(in srgb, var(--app-warning) 16%, var(--app-card)); color: var(--app-warning); }
.tag.r1 { background: color-mix(in srgb, var(--app-success) 14%, var(--app-card)); color: var(--app-success); }
.tag.r2 { background: color-mix(in srgb, #c00 12%, var(--app-card)); color: #c00; }
.product { font-size: 15px; font-weight: 600; color: var(--app-text-1); margin-bottom: 6px; }
.meta { display: flex; justify-content: space-between; font-size: 13px; color: var(--app-text-2); }
.money { display: flex; justify-content: space-between; margin-top: 6px; font-size: 13px; color: var(--app-text-2); }
.wage-miss { color: #c00; font-weight: 600; }
.time { margin-top: 6px; font-size: 12px; color: var(--app-text-3); }
.reject { margin-top: 8px; font-size: 13px; color: #c00; }
.actions { margin-top: 10px; display: flex; justify-content: flex-end; }
.more { text-align: center; margin: 12px 0; }
.tip { margin: 16px 0 0; font-size: 12px; color: var(--app-text-4); text-align: center; }
.mask {
  position: fixed; inset: 0; background: rgba(0,0,0,.45); z-index: 100;
  display: flex; align-items: flex-end; justify-content: center;
}
.edit-panel {
  width: 100%; max-width: 480px; background: var(--app-card);
  border-radius: 16px 16px 0 0; padding: 16px 16px calc(16px + env(safe-area-inset-bottom));
  box-sizing: border-box;
}
.edit-panel h3 { margin: 0 0 4px; font-size: 17px; color: var(--app-text-1); }
.edit-sub { margin: 0 0 12px; font-size: 13px; color: var(--app-text-3); }
.edit-panel label { display: block; margin: 8px 0 4px; font-size: 13px; color: var(--app-text-2); }
.inp {
  width: 100%; height: 40px; border: 1px solid var(--app-border); border-radius: 8px;
  padding: 0 10px; font-size: 15px; background: var(--app-bg); color: var(--app-text-1);
  box-sizing: border-box;
}
.edit-actions { display: flex; gap: 8px; margin-top: 16px; }
.edit-actions .btn-sm { flex: 1; height: 40px; }
</style>
