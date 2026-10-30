<template>
  <div class="h5-page">
    <div class="header">
      <button type="button" class="back" @click="router.back()">‹</button>
      <h2>报工复核</h2>
      <H5LogoutBtn />
    </div>

    <div class="filters">
      <input
        v-model="orderNo"
        class="search"
        type="search"
        placeholder="工单号"
        @keyup.enter="reload"
      />
      <input
        v-model="userName"
        class="search"
        type="search"
        placeholder="例：张三（输入姓名筛选）"
        @keyup.enter="reload"
      />
      <select v-model.number="reviewStatus" class="status" @change="reload">
        <option :value="0">待复核</option>
        <option :value="1">已通过</option>
        <option :value="2">已退回</option>
      </select>
      <button type="button" class="btn-sm" @click="reload">查询</button>
    </div>

    <div v-if="reviewStatus === 0 && pendingIds.length" class="batch-bar">
      <label class="check-all">
        <input type="checkbox" :checked="allSelected" @change="toggleAll" />
        全选本页
      </label>
      <button
        type="button"
        class="btn-sm success"
        :disabled="!selectedIds.length || batching"
        @click="onBatch"
      >
        {{ batching ? '处理中…' : `批量通过(${selectedIds.length})` }}
      </button>
    </div>

    <div v-if="loading" class="state">加载中…</div>
    <div v-else-if="loadError" class="state err">
      数据加载失败，请检查网络后刷新重试；反复失败找管理员处理。
      <button type="button" class="btn-sm" @click="reload">重试</button>
    </div>
    <div v-else-if="list.length === 0" class="state">暂时没有待复核的记录，可能报工都已复核完，或换个筛选条件看看。</div>

    <div
      v-for="row in list"
      :key="row.id"
      class="card"
      :class="{ abnormal: row.qtyAbnormal }"
    >
      <div class="row1">
        <label v-if="row.reviewStatus === 0" class="pick">
          <input
            type="checkbox"
            :checked="selectedIds.includes(row.id)"
            @change="toggleOne(row.id)"
          />
        </label>
        <span class="no">{{ row.orderNo }}</span>
        <span class="tag" :class="'r' + row.reviewStatus">{{ reviewLabel(row.reviewStatus) }}</span>
        <span
          v-if="row.qtyAbnormal"
          class="tag qty-abn"
          title="这道工序报的良品数超过了上道工序审核通过的数量，正常情况不应该这样。如果是返工或补料，可以点特批通过。"
        >数量异常</span>
      </div>
      <div class="product">{{ row.productName }}</div>
      <div class="meta">
        <span>{{ row.operationName }}</span>
        <span>{{ row.userName }}</span>
      </div>
      <div class="meta">
        <span>良品 {{ row.goodQty }} / 不良 {{ row.defectQty }}</span>
        <span class="time">{{ formatTime(row.reportTime) }}</span>
      </div>
      <div class="meta">
        <span>可报上限 {{ row.upstreamLimit == null ? '-' : row.upstreamLimit }}</span>
        <span v-if="row.qtyAbnormal">同工序占用 {{ row.sameOpOccupiedQty }}</span>
      </div>
      <div v-if="row.reviewStatus === 2 && row.rejectReason" class="reject">
        退回原因：{{ row.rejectReason }}
      </div>
      <div v-if="row.reviewStatus !== 2" class="actions">
        <button
          v-if="row.reviewStatus === 0"
          type="button"
          class="btn-act ok"
          :disabled="actingId === row.id"
          @click="onApprove(row)"
        >通过</button>
        <button
          type="button"
          class="btn-act no"
          :disabled="actingId === row.id"
          @click="openReject(row)"
        >退回</button>
      </div>
    </div>

    <div v-if="!loading && !loadError && total > list.length" class="more">
      <button type="button" class="btn-sm" :disabled="loadingMore" @click="loadMore">
        {{ loadingMore ? '加载中…' : '加载更多' }}
      </button>
    </div>

    <!-- 退回原因 -->
    <div v-if="rejectOpen" class="mask" @click.self="rejectOpen = false">
      <div class="dlg">
        <h3>退回报工</h3>
        <textarea
          v-model="rejectReason"
          rows="4"
          maxlength="256"
          placeholder="请填写为什么退回（必填）"
        />
        <div class="dlg-actions">
          <button type="button" class="btn-act" @click="rejectOpen = false">取消</button>
          <button type="button" class="btn-act no" :disabled="rejecting" @click="onReject">
            {{ rejecting ? '提交中…' : '确定退回' }}
          </button>
        </div>
      </div>
    </div>

    <!-- 特批通过 -->
    <div v-if="specialOpen" class="mask" @click.self="specialOpen = false">
      <div class="dlg">
        <h3>确认特批通过这道工序</h3>
        <p class="dlg-body">
          这道工序报的良品 {{ specialRow?.goodQty ?? 0 }} 件，比上一道审过的 {{ specialRow?.upstreamLimit ?? 0 }} 件还多，正常不该这样；如果是返工、补料等特殊情况，确认后才能通过。
        </p>
        <textarea
          v-model="specialRemark"
          rows="3"
          maxlength="200"
          placeholder="特殊情况备注（选填，仅当次参考不保存）"
        />
        <div class="dlg-actions">
          <button type="button" class="btn-act" @click="specialOpen = false">取消</button>
          <button type="button" class="btn-act warn" :disabled="specialLoading" @click="confirmSpecial">
            {{ specialLoading ? '提交中…' : '确认特批通过' }}
          </button>
        </div>
      </div>
    </div>

    <!-- 批量含异常 -->
    <div v-if="batchOpen" class="mask" @click.self="batchOpen = false">
      <div class="dlg">
        <h3>以下单据数量有异常</h3>
        <p class="dlg-body">
          你选的里面有 {{ batchAbnormalCount }} 条数量不对（共 {{ batchTotal }} 条），系统会自动跳过这些，只通过没问题的。
        </p>
        <div v-if="batchDetailOpen" class="detail">
          <div v-for="r in batchAbnormalRows" :key="r.id" class="detail-row">
            {{ r.orderNo }} / {{ r.operationName }} / {{ r.userName }}：良品{{ r.goodQty }}，上限{{ r.upstreamLimit ?? '-' }}
          </div>
        </div>
        <div class="dlg-actions col">
          <button type="button" class="btn-act" @click="batchDetailOpen = !batchDetailOpen">
            {{ batchDetailOpen ? '收起异常明细' : '查看异常明细' }}
          </button>
          <button type="button" class="btn-act ok" :disabled="batching" @click="confirmBatchOkOnly">
            {{ batching ? '处理中…' : '确认仅通过合规项' }}
          </button>
          <button type="button" class="btn-act" @click="batchOpen = false">取消批量操作</button>
        </div>
      </div>
    </div>

    <TabBar active="review" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { showToast, showSuccessToast, showConfirmDialog } from 'vant'
import 'vant/es/toast/style'
import 'vant/es/dialog/style'
import {
  getPendingReviews,
  approveReview,
  rejectReview,
  batchApproveReview
} from '@/api/review'
import TabBar from './TabBar.vue'
import H5LogoutBtn from '@/components/H5LogoutBtn.vue'

const router = useRouter()
const orderNo = ref('')
const userName = ref('')
const reviewStatus = ref(0)
const list = ref([])
const total = ref(0)
const page = ref(1)
const loading = ref(false)
const loadingMore = ref(false)
const loadError = ref(false)
const selectedIds = ref([])
const actingId = ref(null)
const batching = ref(false)
const rejectOpen = ref(false)
const rejectReason = ref('')
const rejectId = ref(null)
const rejecting = ref(false)

const specialOpen = ref(false)
const specialRow = ref(null)
const specialRemark = ref('')
const specialLoading = ref(false)

const batchOpen = ref(false)
const batchTotal = ref(0)
const batchAbnormalCount = ref(0)
const batchOkIds = ref([])
const batchAbnormalRows = ref([])
const batchDetailOpen = ref(false)

const pendingIds = computed(() =>
  list.value.filter(r => r.reviewStatus === 0).map(r => r.id)
)
const allSelected = computed(() =>
  pendingIds.value.length > 0 && pendingIds.value.every(id => selectedIds.value.includes(id))
)

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
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  const hh = String(d.getHours()).padStart(2, '0')
  const mi = String(d.getMinutes()).padStart(2, '0')
  return `${mm}-${dd} ${hh}:${mi}`
}

function toggleOne(id) {
  const i = selectedIds.value.indexOf(id)
  if (i >= 0) selectedIds.value = selectedIds.value.filter(x => x !== id)
  else selectedIds.value = selectedIds.value.concat(id)
}

function toggleAll(e) {
  if (e.target.checked) selectedIds.value = pendingIds.value.slice()
  else selectedIds.value = []
}

async function fetchPage(p, append) {
  const params = {
    page: p,
    pageSize: 20,
    reviewStatus: reviewStatus.value
  }
  const no = orderNo.value.trim()
  const un = userName.value.trim()
  if (no) params.orderNo = no
  if (un) params.userName = un
  const res = await getPendingReviews(params)
  const rows = res.data?.list || []
  total.value = res.data?.total || 0
  list.value = append ? list.value.concat(rows) : rows
  page.value = p
  if (!append) selectedIds.value = []
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

async function doApprove(id) {
  await approveReview(id)
  showSuccessToast('已通过')
  await reload()
}

async function onApprove(row) {
  if (row.qtyAbnormal) {
    specialRow.value = row
    specialRemark.value = ''
    specialOpen.value = true
    return
  }
  actingId.value = row.id
  try {
    await doApprove(row.id)
  } catch {
    // http 拦截器已提示
  } finally {
    actingId.value = null
  }
}

async function confirmSpecial() {
  if (!specialRow.value) return
  specialLoading.value = true
  try {
    await doApprove(specialRow.value.id)
    specialOpen.value = false
  } catch {
    // http 拦截器已提示
  } finally {
    specialLoading.value = false
  }
}

function openReject(row) {
  rejectId.value = row.id
  rejectReason.value = ''
  rejectOpen.value = true
}

async function onReject() {
  const reason = rejectReason.value.trim()
  if (!reason) {
    showToast('退回去的时候要写一下原因，告诉对方哪里有问题。')
    return
  }
  if (reason.length > 256) {
    showToast('退回原因写太多了，最多256个字，请精简一下。')
    return
  }
  rejecting.value = true
  try {
    await rejectReview(rejectId.value, { reason })
    showSuccessToast('已退回')
    rejectOpen.value = false
    await reload()
  } catch {
    // http 拦截器已提示
  } finally {
    rejecting.value = false
  }
}

async function onBatch() {
  const ids = selectedIds.value.slice()
  if (!ids.length) return
  const rows = list.value.filter(r => ids.includes(r.id))
  const abnormal = rows.filter(r => r.qtyAbnormal)
  const ok = rows.filter(r => !r.qtyAbnormal)
  if (!abnormal.length) {
    try {
      await showConfirmDialog({
        title: '批量通过',
        message: `确认通过选中的 ${ok.length} 条？`
      })
    } catch {
      return
    }
    batching.value = true
    try {
      const res = await batchApproveReview({ ids: ok.map(r => r.id) })
      showSuccessToast(`已通过 ${res.data?.approved ?? ok.length} 条`)
      await reload()
    } catch {
      // http 拦截器已提示
    } finally {
      batching.value = false
    }
    return
  }
  batchTotal.value = rows.length
  batchAbnormalCount.value = abnormal.length
  batchOkIds.value = ok.map(r => r.id)
  batchAbnormalRows.value = abnormal
  batchDetailOpen.value = false
  batchOpen.value = true
}

async function confirmBatchOkOnly() {
  const ids = batchOkIds.value.slice()
  if (!ids.length) {
    showToast('你选的这些都有数量异常，没法直接批量通过，需要逐条处理。')
    batchOpen.value = false
    return
  }
  batching.value = true
  try {
    const res = await batchApproveReview({ ids })
    showSuccessToast(`已通过 ${res.data?.approved ?? ids.length} 条（已跳过异常）`)
    batchOpen.value = false
    await reload()
  } catch {
    // http 拦截器已提示
  } finally {
    batching.value = false
  }
}

onMounted(() => {
  const role = Number(localStorage.getItem('role') || 0)
  if (role !== 1 && role !== 3) {
    showToast('你没有这道工序的复核权限。请联系班组长或管理员确认你所在部门是否有该工序的复核权限。')
    router.replace('/h5/tasks')
    return
  }
  reload()
})
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
.filters { display: flex; gap: 6px; margin-bottom: 10px; align-items: center; flex-wrap: wrap; }
.search, .status {
  height: var(--h5-control-height); border: 1px solid var(--app-border); border-radius: 8px;
  padding: 0 10px; font-size: 14px; background: var(--app-card); color: var(--app-text-1);
}
.search { flex: 1; min-width: 72px; }
.status { width: 96px; flex: none; }
.btn-sm {
  height: 36px; padding: 0 12px; border: none; border-radius: 8px;
  background: var(--app-primary); color: var(--app-card); font-size: 14px;
}
.btn-sm.success { background: var(--app-success, #16a34a); }
.btn-sm:disabled { opacity: .6; }
.batch-bar {
  display: flex; align-items: center; justify-content: space-between;
  margin-bottom: 10px; padding: 8px 10px;
  background: var(--app-card); border-radius: 8px;
}
.check-all { display: flex; align-items: center; gap: 6px; font-size: 14px; color: var(--app-text-2); }
.state { text-align: center; color: var(--app-text-3); padding: 32px 0; font-size: 14px; }
.state.err { display: flex; flex-direction: column; align-items: center; gap: 12px; color: var(--app-danger, #c00); }
.card {
  background: var(--app-card); border-radius: var(--h5-card-radius); padding: var(--h5-card-padding);
  margin-bottom: 10px; box-shadow: var(--app-shadow);
}
.card.abnormal { background: #fef9e7; }
.row1 { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; flex-wrap: wrap; }
.pick { display: flex; align-items: center; }
.no { font-weight: 700; font-size: 15px; color: var(--app-text-1); }
.tag { margin-left: auto; font-size: 12px; padding: 2px 8px; border-radius: 4px; }
.tag.r0 { background: color-mix(in srgb, var(--app-warning) 16%, var(--app-card)); color: var(--app-warning); }
.tag.r1 { background: color-mix(in srgb, var(--app-success) 14%, var(--app-card)); color: var(--app-success); }
.tag.r2 { background: color-mix(in srgb, #c00 12%, var(--app-card)); color: #c00; }
.tag.qty-abn {
  margin-left: 0;
  background: #e6a23c;
  color: #fff;
}
.product { font-size: 15px; font-weight: 600; color: var(--app-text-1); margin-bottom: 6px; }
.meta { display: flex; justify-content: space-between; font-size: 13px; color: var(--app-text-2); margin-top: 2px; gap: 8px; }
.time { font-size: 12px; color: var(--app-text-3); }
.reject { margin-top: 8px; font-size: 13px; color: #c00; }
.actions { display: flex; gap: 8px; margin-top: 12px; }
.btn-act {
  flex: 1; height: 36px; border: 1px solid var(--app-border); border-radius: 8px;
  background: var(--app-card); font-size: 14px; color: var(--app-text-1);
}
.btn-act.ok { border-color: var(--app-success, #16a34a); color: var(--app-success, #16a34a); font-weight: 600; }
.btn-act.no { border-color: #c00; color: #c00; font-weight: 600; }
.btn-act.warn { border-color: #e6a23c; color: #e6a23c; font-weight: 600; }
.btn-act:disabled { opacity: .6; }
.more { text-align: center; margin: 12px 0; }
.mask {
  position: fixed; inset: 0; background: rgba(0,0,0,.45); z-index: 100;
  display: flex; align-items: flex-end; justify-content: center;
}
.dlg {
  width: 100%; max-width: 480px; background: var(--app-card);
  border-radius: 12px 12px 0 0; padding: 16px;
  padding-bottom: calc(16px + env(safe-area-inset-bottom));
  box-sizing: border-box;
  max-height: 85vh; overflow-y: auto;
}
.dlg h3 { margin: 0 0 12px; font-size: 16px; color: var(--app-text-1); }
.dlg-body { margin: 0 0 12px; font-size: 14px; line-height: 1.6; color: var(--app-text-2); }
.dlg textarea {
  width: 100%; box-sizing: border-box; border: 1px solid var(--app-border);
  border-radius: 8px; padding: 10px; font-size: 14px; resize: vertical;
  background: var(--app-bg); color: var(--app-text-1);
}
.dlg-actions { display: flex; gap: 8px; margin-top: 12px; }
.dlg-actions.col { flex-direction: column; }
.detail {
  margin-bottom: 8px; padding: 8px 10px; border-radius: 8px;
  background: var(--app-bg); font-size: 13px; color: var(--app-text-2);
  max-height: 160px; overflow-y: auto;
}
.detail-row { margin-bottom: 6px; }
.detail-row:last-child { margin-bottom: 0; }
</style>
