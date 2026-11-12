<template>
  <div v-loading="loading">
    <PriceChangeDialog v-model="adjustVisible" :table-id="adjustId" @done="adjusted" />
    <div class="toolbar sticky-toolbar">
      <el-button v-if="readonly && canEdit && canEditCreator" type="primary" @click="readonly=false">编辑</el-button>
      <el-button v-if="!readonly" type="primary" :loading="saving" @click="save">保存</el-button>
      <el-button v-if="id&&form.status===1&&canEditCreator" type="warning" :disabled="saving||dirty()" @click="adjustId=id;adjustVisible=true">调整工价</el-button>
      <el-button v-if="id&&form.status===1&&canEditCreator" type="danger" plain :disabled="saving||dirty()" @click="setStatus(2)">停用</el-button>
      <el-button v-if="id&&form.status===2&&canEditCreator" type="success" :disabled="saving||dirty()" @click="setStatus(1)">启用</el-button>
      <el-button :disabled="saving" @click="back">退出</el-button>
      <span class="mode-hint">{{readonly?'浏览':id?'编辑':'新增'}}</span>
    </div>
    <el-alert v-if="readonly && canEdit && !canEditCreator" class="browse-notice" title="这张价目表不是你创建的，不能改。请联系创建人或管理员处理。" type="info" show-icon :closable="false" />
    <el-alert v-else-if="readonly && canEdit" class="browse-notice" title="当前是浏览模式，可复制内容；要修改请点左上角「编辑」" type="info" show-icon :closable="false" />
    <el-alert v-else-if="readonly && form.status===1" class="browse-notice" title="启用中不能直接编辑。改结构请先停用或复制；只改工价请点「调整工价」" type="info" show-icon :closable="false" />
    <el-alert title="系统优先使用产品专属价目表；没有有效的专属表时才用通用表；专属表里缺少的规格不会自动从通用表补" type="info" :closable="false" />
    <el-form v-if="readonly" label-width="110px" class="header-form browse-content">
      <el-form-item v-for="item in browseHeader" :key="item.label" :label="item.label" :class="{wide:item.label==='备注'}"><span class="copyable">{{item.value??'—'}}</span></el-form-item>
    </el-form>
    <el-form v-else :disabled="saving" label-width="110px" class="header-form">
      <el-form-item label="单据编号" required><el-input v-model="form.code" :disabled="!!id" maxlength="64" /></el-form-item>
      <el-form-item label="名称" required><el-input v-model="form.name" maxlength="128" /></el-form-item>
      <el-form-item label="价目表类型" required><el-select v-model="form.operationId" :disabled="!!form.activatedAt" filterable @change="operationChanged"><el-option v-for="o in operations" :key="o.id" :value="o.id" :label="o.name" /></el-select><span v-if="form.activatedAt" class="hint">价目表首次启用后，所属工序不可更改；如需更换工序，请新建一张价目表</span></el-form-item>
      <el-form-item label="工价计算方式" required><el-select v-model="form.pricingType" :disabled="!!form.activatedAt" @change="pricingTypeChanged"><el-option v-for="(v,k) in pricingTypes" :key="Number(k)" :value="Number(k)" :label="v" /></el-select><span v-if="form.activatedAt" class="hint">价目表首次启用后，工价计算方式不可更改；如需更换，请新建一张价目表</span></el-form-item>
      <el-form-item label="产品"><el-select v-model="form.productId" filterable clearable placeholder="通用表（不限产品）"><el-option v-for="p in products" :key="p.id" :value="p.id" :label="`${p.code} ${p.name}`" /></el-select></el-form-item>
      <el-form-item label="生效日期" required><el-date-picker v-model="form.effectiveFrom" value-format="YYYY-MM-DD" /></el-form-item>
      <el-form-item label="失效日期"><el-date-picker v-model="form.effectiveTo" value-format="YYYY-MM-DD" clearable /></el-form-item>
      <el-form-item v-if="showPriceUnitFields" label="计价单位" required>
        <el-select v-model="form.priceUnit" filterable :disabled="form.pricingType!==1" placeholder="请选择单位">
          <el-option v-for="name in priceUnitOptions" :key="name" :value="name" :label="name" />
        </el-select>
        <span v-if="form.pricingType!==1" class="hint">计时固定为「小时」、单笔固定为「笔」</span>
      </el-form-item>
      <el-form-item label="报工单位" required><el-select v-model="form.reportUnitId" @change="syncSingleUom"><el-option v-for="u in units" :key="u.id" :value="u.id" :label="u.name" /></el-select></el-form-item>
      <el-form-item v-if="showPriceUnitFields" label="折算比例" required><el-input-number v-model="form.unitsPerPriceUnit" :precision="4" :min="0.0001" :max="1000000" /><span class="hint">每计价单位对应报工数</span></el-form-item>
      <el-form-item label="状态"><el-tag>{{statuses[form.status]}}</el-tag></el-form-item>
      <el-form-item label="备注" class="wide"><el-input v-model="form.remark" maxlength="1000" placeholder="例：本表适用于某产品线所有规格，按计件计价" /></el-form-item>
    </el-form>
    <el-form :disabled="readonly||saving">
      <p class="hint">附加价按行上取价条件命中几条加几条；同一属性字段上多行条件重叠时保存会提示改条件。计时/固定不支持附加价。</p>
      <div v-if="!readonly" class="toolbar body-tools">
        <el-dropdown split-button trigger="click" @click="addRow(false)" @command="cmd=>addRow(cmd==='insert')">
          新增行
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item command="insert">插入行</el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
        <el-button :disabled="!selected" @click="duplicate">复制选中行</el-button><el-button type="danger" plain :disabled="!selected" @click="removeRow">删除行</el-button>
        <span class="hint">点击行号或规格可编辑价格性质、包含项和确认信息；已启用的价目表如需改金额或不良扣价，请点“调整工价”</span>
      </div>
      <mes-table table-key="views/operation-price-list/form:1" :data="form.details.rows" border stripe row-key="rowId" highlight-current-row @current-change="r=>selected=r" class="price-table">
        <el-table-column label="行号" width="65"><template #default="{row,$index}"><span v-if="readonly" class="copyable">{{$index+1}}</span><el-button v-else link type="primary" @click="editRow(row)">{{$index+1}}</el-button></template></el-table-column>
        <el-table-column label="品名规格" min-width="175"><template #default="{row}"><span v-if="readonly" class="copyable">{{row.label||'暂无规格'}}</span><el-button v-else link type="primary" @click="editRow(row)">{{row.label||'暂无规格，点击补充'}}</el-button><div class="hint">{{kinds[row.kind]}} · {{row.confirmed&&row.amount!=null?'已确认':'待确认 / 未定价'}}</div></template></el-table-column>
        <el-table-column label="取价条件" min-width="230"><template #default="{row}"><span v-if="readonly" class="copyable">{{summary(row.conditions)}}</span><el-button v-else link type="primary" @click="editConditions(row)">{{summary(row.conditions)}}</el-button></template></el-table-column>
        <el-table-column label="属性区间" min-width="155"><template #default="{row}">{{summary(row.conditions.filter(c=>c.operator==='range'))==='不限'?'—':summary(row.conditions.filter(c=>c.operator==='range'))}}</template></el-table-column>
        <el-table-column label="单价" width="145"><template #default="{row}"><span v-if="readonly" class="copyable">{{row.amount??'未定价'}}</span><el-input-number v-else v-model="row.amount" :disabled="priceFrozen(row.rowId)" :controls="false" :precision="4" :min="0" :max="1000000" style="width:125px" /></template></el-table-column>
        <el-table-column label="单位" width="80"><template #default>{{form.priceUnit||'未设'}}</template></el-table-column>
      </mes-table>
    </el-form>
    <p class="hint">不完整的草稿可以先保存；完整草稿保存后自动启用，启用失败不影响原来的状态；已停用的表保存后仍保持停用；未确认的行请打开行详情补齐信息；原始分档价只作备注，不参与自动取价</p>
    <div v-if="id" class="price-sys-meta">
      创建人：{{ form.createdByName || '—' }}
      · 创建时间：{{ fmtTime(form.createdAt) }}
      · 启用人：{{ form.activatedByName || '—' }}
      · 启用时间：{{ fmtTime(form.activatedAt) }}
      · 停用人：{{ form.deactivatedByName || '—' }}
      · 停用时间：{{ fmtTime(form.deactivatedAt) }}
      · 修改人：{{ form.updatedByName || '—' }}
      · 修改时间：{{ fmtTime(form.updatedAt) }}
    </div>
    <h3>取价试算</h3>
    <p class="hint">试算基于已保存的有效价目表，当前未保存的修改不参与试算</p>
    <div class="toolbar"><el-select v-model="previewProduct" filterable placeholder="选择一个产品进行试算" style="width:300px"><el-option v-for="p in products" :key="p.id" :value="p.id" :label="`${p.code} ${p.name}`" /></el-select><el-button :disabled="!id||dirty()" :loading="previewing" @click="preview">试算</el-button></div>
    <el-form label-width="130px"><el-form-item v-for="f in craftFields" :key="f.id" :label="f.fieldName"><el-select v-if="f.fieldType==='single'" v-model="previewAttributes[f.id]" clearable><el-option v-for="v in f.options" :key="v" :value="v" :label="v" /></el-select><el-input v-else v-model="previewAttributes[f.id]" placeholder="填入实际值，不清楚的字段留空即可" /></el-form-item></el-form>
    <el-alert v-if="previewError" :title="previewError" type="error" :closable="false" />
    <template v-if="quote"><p><strong>{{quote.baseName}}：{{quote.unitPrice}}元/{{quote.priceUnit}}</strong>；每{{quote.priceUnit}}对应{{quote.unitsPerPriceUnit}}个报工单位</p><mes-table table-key="views/operation-price-list/form:2" :data="quote.attributeEvidence?.rows" border><el-table-column prop="rowId" label="价格行" /><el-table-column prop="reason" label="结果及原因" /><el-table-column prop="amount" label="原价" /><el-table-column prop="source" label="数据来源" /></mes-table></template>
    <el-dialog v-model="conditionDialog" title="取价条件（全部同时满足）" width="850px" :close-on-click-modal="false">
      <el-checkbox v-model="unlimited" :disabled="readonly">不限制属性条件（任何属性都匹配）</el-checkbox>
      <AttributeConditions v-if="!unlimited" v-model="conditionDraft" :fields="fields" :disabled="readonly" />
      <template #footer><el-button @click="conditionDialog=false">取消</el-button><el-button v-if="!readonly" type="primary" @click="commitConditions">保存条件</el-button></template>
    </el-dialog>
    <el-dialog v-model="rowDialog" title="行详情" width="650px" :close-on-click-modal="false">
      <el-form v-if="rowDraft" :disabled="readonly" label-width="115px">
        <el-form-item label="行标识">{{rowDraft.rowId}}</el-form-item><el-form-item label="品名规格"><el-input v-model="rowDraft.label" maxlength="256" /></el-form-item>
        <el-form-item label="价格性质"><el-select v-model="rowDraft.kind"><el-option v-for="(v,k) in kinds" :key="k" :value="k" :label="v" /></el-select></el-form-item>
        <el-form-item v-if="rowDraft.kind!=='extra'" label="不良扣价"><el-input-number v-model="rowDraft.deductPrice" :disabled="priceFrozen(rowDraft.rowId)" :min="0" :precision="4" :max="1000000" /></el-form-item>
        <el-form-item v-if="rowDraft.kind!=='extra'" label="已包含附加项"><el-select v-model="rowDraft.includedRowIds" multiple><el-option v-for="r in form.details.rows.filter(r=>r.kind==='extra'&&r.rowId!==rowDraft.rowId)" :key="r.rowId" :value="r.rowId" :label="r.label||r.rowId" /></el-select></el-form-item>
        <el-form-item label="已确认"><el-checkbox v-model="rowDraft.confirmed">我已确认该行的价格和收费口径无误</el-checkbox></el-form-item>
        <el-form-item label="来源" required><el-input v-model="rowDraft.source" placeholder="例：Excel报价表Sheet1第3行，或手工录入依据" /></el-form-item><el-form-item label="原始备注"><el-input v-model="rowDraft.note" type="textarea" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="rowDialog=false">取消</el-button><el-button v-if="!readonly" type="primary" @click="commitRow">保存行详情</el-button></template>
    </el-dialog>
  </div>
</template>
<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import PriceChangeDialog from './PriceChangeDialog.vue'
import { getOperationList, getUnitList, getProductList } from '@/api/base'
import { getCustomFields, getReportQtySetting, getDocumentPermission } from '@/api/prod'
import { getPriceList, savePriceList, previewAttributePrice, changePriceListStatus } from '@/api/operationPriceList'
import AttributeConditions from '@/components/AttributeConditions.vue'
import { setPcTabTitle, closePcTab } from '@/composables/usePcTabs'
import { canEditByCreator } from '@/utils/documentPermission'

const route = useRoute()
const router = useRouter()
const role = Number(localStorage.getItem('role') || 0)
const statuses = { 0: '草稿', 1: '启用', 2: '停用' }
const kinds = { base: '基础价', extra: '附加价', final: '最终价' }
const pricingTypes = { 1: '计件', 2: '计时', 3: '单笔固定' }
const operations = ref([]), units = ref([]), products = ref([]), allFields = ref([])
const enableMultiUom = ref(false)
const onlyCreatorEditPriceList = ref(false)
const showPriceUnitFields = computed(() => form.value.pricingType !== 1 || enableMultiUom.value)
const readonly = ref(false), loading = ref(false), saving = ref(false), id = ref(null)
const form = ref({ details: { rows: [] } })
const baseline = ref(''), previewProduct = ref(null), previewAttributes = ref({}), previewing = ref(false), previewError = ref(''), quote = ref(null)
const adjustVisible = ref(false), adjustId = ref(null)
const skipLeaveGuard = ref(false)
/** 清单 #23：启用中禁止普通编辑；草稿/停用可改 */
const canEdit = computed(() => form.value.status !== 1)
/** 清单 #24：开关开时非管理员仅创建人可改 */
const canEditCreator = computed(() =>
  canEditByCreator(onlyCreatorEditPriceList.value, form.value?.createdBy, role)
)
const fmtTime = v => v ? String(v).replace('T', ' ').slice(0, 19) : '—'
const priceUnitOptions = computed(() => {
  const names = units.value.map(u => u.name).filter(Boolean)
  const fixed = form.value.pricingType === 2 ? '小时' : form.value.pricingType === 3 ? '笔' : ''
  const cur = (form.value.priceUnit || '').trim()
  const out = []
  for (const n of [fixed, cur, ...names]) { if (n && !out.includes(n)) out.push(n) }
  return out
})
const selected = ref(null), conditionDialog = ref(false), conditionDraft = ref([]), conditionRow = ref(null), unlimited = ref(false)
const rowDialog = ref(false), rowDraft = ref(null), rowOriginal = ref(null)
const fields = computed(() => allFields.value.filter(f => f.target === 'product' || f.operationId === form.value.operationId))
const craftFields = computed(() => fields.value.filter(f => f.target === 'work_order_task'))
const day = s => s?.slice(0, 10)
const clone = v => JSON.parse(JSON.stringify(v))
const dirty = () => !readonly.value && JSON.stringify(form.value) !== baseline.value
const priceFrozen = rowId => !!form.value.activatedAt && !!baseline.value && JSON.parse(baseline.value).details.rows.some(r => r.rowId === rowId)
const opName = oid => operations.value.find(o => o.id === oid)?.name || `未知工序（编号 ${oid}）`
const productName = pid => pid ? ((p => p ? `${p.code} ${p.name}` : `未知产品（编号 ${pid}）`)(products.value.find(p => p.id === pid))) : '通用表（不限产品）'
const browseHeader = computed(() => {
  const rows = [
    { label: '单据编号', value: form.value.code }, { label: '名称', value: form.value.name },
    { label: '价目表类型', value: opName(form.value.operationId) }, { label: '计价方式', value: pricingTypes[form.value.pricingType] },
    { label: '产品', value: productName(form.value.productId) }, { label: '生效日期', value: form.value.effectiveFrom },
    { label: '失效日期', value: form.value.effectiveTo || '长期' }
  ]
  if (showPriceUnitFields.value) rows.push({ label: '计价单位', value: form.value.priceUnit })
  rows.push({ label: '报工单位', value: units.value.find(u => u.id === form.value.reportUnitId)?.name || form.value.reportUnitId })
  if (showPriceUnitFields.value) rows.push({ label: '折算比例', value: form.value.unitsPerPriceUnit })
  rows.push({ label: '状态', value: statuses[form.value.status] }, { label: '备注', value: form.value.remark || '—' })
  return rows
})
function syncSingleUom() {
  if (enableMultiUom.value || form.value.pricingType !== 1) return
  const name = units.value.find(u => u.id === form.value.reportUnitId)?.name || ''
  form.value.priceUnit = name
  form.value.unitsPerPriceUnit = 1
}
function stripLegacyGroups(details) {
  if (!details) return
  details.groups = []
  for (const r of details.rows || []) r.groupKey = null
}
function refreshTabTitle() {
  const code = String(form.value.code || '').trim()
  setPcTabTitle(route.path, code || (id.value ? '价目表' : '新建价目表'))
}
function emptyForm() {
  return {
    code: '', name: '', operationId: null, pricingType: 1, productId: null, remark: '', activatedAt: null, status: 0,
    effectiveFrom: new Date().toLocaleDateString('sv-SE'), effectiveTo: null, priceUnit: '', reportUnitId: null,
    unitsPerPriceUnit: 1, expectedVersion: '', details: { schemaVersion: 2, requiredFieldIds: [], groups: [], rows: [] }
  }
}
function start(read) {
  readonly.value = read
  selected.value = null
  quote.value = null
  previewError.value = ''
  previewAttributes.value = {}
  stripLegacyGroups(form.value.details)
  baseline.value = JSON.stringify(form.value)
  refreshTabTitle()
}
async function loadBill() {
  loading.value = true
  try {
    const paramId = route.params.id
    const isNew = route.name === 'OperationPriceListNew' || paramId === 'new'
    const wantReadonly = route.query.mode === 'browse'
    if (isNew) {
      id.value = null
      form.value = emptyForm()
      const copyFrom = Number(route.query.copyFrom)
      if (Number.isFinite(copyFrom) && copyFrom > 0) {
        const r = await getPriceList(copyFrom)
        form.value = {
          ...r.data,
          code: '',
          name: `${r.data.name || ''}（复制）`,
          status: 0,
          activatedAt: null,
          activatedBy: null,
          activatedByName: '',
          deactivatedAt: null,
          deactivatedBy: null,
          deactivatedByName: '',
          createdBy: null,
          createdByName: '',
          updatedBy: null,
          updatedByName: '',
          createdAt: null,
          updatedAt: null,
          expectedVersion: '',
          effectiveFrom: day(r.data.effectiveFrom),
          effectiveTo: day(r.data.effectiveTo)
        }
        stripLegacyGroups(form.value.details)
      }
      start(false)
      return
    }
    const key = Number(paramId)
    if (!Number.isFinite(key) || key <= 0) {
      ElMessage.error('价目表链接无效，请从列表重新打开')
      leaveToList()
      return
    }
    const r = await getPriceList(key)
    id.value = key
    form.value = { ...r.data, effectiveFrom: day(r.data.effectiveFrom), effectiveTo: day(r.data.effectiveTo), expectedVersion: r.data.version }
    stripLegacyGroups(form.value.details)
    // 启用中强制浏览；停用/草稿可进编辑（除非显式 browse）；清单 #24 非创建人强制浏览
    const creatorOk = canEditByCreator(onlyCreatorEditPriceList.value, form.value.createdBy, role)
    start(wantReadonly || form.value.status === 1 || !creatorOk)
  } finally {
    loading.value = false
  }
}
async function adjusted() {
  if (id.value) await loadBill()
}
function leaveToList() {
  skipLeaveGuard.value = true
  const next = closePcTab(route.path, route.path)
  router.push(next || '/operation-price-list')
}
async function confirmLeave() {
  if (skipLeaveGuard.value) return true
  if (!dirty()) return true
  try {
    await ElMessageBox.confirm('有未保存的修改，离开后这些修改会丢失。确认离开吗？', '提示')
    return true
  } catch (action) {
    if (action === 'cancel' || action === 'close') return false
    throw action
  }
}
async function back() {
  if (saving.value) return
  if (await confirmLeave()) leaveToList()
}
onBeforeRouteLeave(async () => saving.value ? false : await confirmLeave())
watch([() => form.value.operationId, previewProduct, previewAttributes], () => { quote.value = null; previewError.value = '' }, { deep: true })
watch(() => form.value.code, () => refreshTabTitle())
watch(
  () => ({ name: route.name, fullPath: route.fullPath }),
  (cur, prev) => {
    if (!prev) return
    if (cur.name !== 'OperationPriceListBill' && cur.name !== 'OperationPriceListNew') return
    if (cur.fullPath === prev.fullPath) return
    loadBill()
  }
)
function operationChanged() {
  form.value.details.groups = []
  form.value.details.requiredFieldIds = []
  for (const r of form.value.details.rows) { r.conditions = []; r.groupKey = null; r.includedRowIds = []; r.confirmed = false }
  previewAttributes.value = {}
  ElMessage.info('工序已更换，之前的取价条件已清空，请在下方重新设置')
}
function pricingTypeChanged() {
  if (form.value.pricingType === 2) { form.value.priceUnit = '小时'; form.value.unitsPerPriceUnit = 1 }
  else if (form.value.pricingType === 3) { form.value.priceUnit = '笔'; form.value.unitsPerPriceUnit = 1 }
  else { if (form.value.priceUnit === '小时' || form.value.priceUnit === '笔') form.value.priceUnit = ''; syncSingleUom() }
  if (form.value.pricingType !== 1) {
    form.value.details.groups = []
    for (const r of form.value.details.rows) { r.kind = 'base'; r.groupKey = null; r.includedRowIds = [] }
  }
  ElMessage.info('工价计算方式已切换；计时和单笔固定只允许一个主价行，不支持附加行，请检查并调整价格行')
}
async function save() {
  saving.value = true
  try {
    syncSingleUom()
    stripLegacyGroups(form.value.details)
    const payload = clone(form.value)
    payload.productId = payload.productId || null
    const r = await savePriceList(id.value, payload)
    const saved = (await getPriceList(r.data)).data
    ElMessage.success(`已保存，当前状态：${statuses[saved.status]}`)
    leaveToList()
  } finally {
    saving.value = false
  }
}
async function setStatus(status) {
  saving.value = true
  try {
    await changePriceListStatus(id.value, status, form.value.version)
    await loadBill()
    ElMessage.success(status === 1 ? '已启用' : '已停用')
  } finally {
    saving.value = false
  }
}
function addRow(insert) {
  const r = { rowId: crypto.randomUUID(), label: '', kind: 'base', conditions: [], amount: null, deductPrice: 0, groupKey: null, includedRowIds: [], confirmed: false, source: '', note: '' }
  const i = selected.value ? form.value.details.rows.indexOf(selected.value) : -1
  form.value.details.rows.splice(insert && i >= 0 ? i : form.value.details.rows.length, 0, r)
  selected.value = r
}
function duplicate() {
  const r = clone(selected.value)
  r.rowId = crypto.randomUUID()
  r.groupKey = null
  form.value.details.rows.push(r)
  selected.value = r
}
function removeRow() {
  const row = selected.value
  if (form.value.details.rows.some(r => r.includedRowIds.includes(row.rowId))) {
    ElMessage.error('该行被其他行的“已包含附加项”引用，无法删除；请先打开引用它的行，取消勾选后再删除')
    return
  }
  form.value.details.rows.splice(form.value.details.rows.indexOf(row), 1)
  selected.value = null
}
function editRow(row) { rowOriginal.value = row; rowDraft.value = clone(row); rowDialog.value = true }
function commitRow() {
  const r = rowDraft.value
  r.groupKey = null
  if (r.kind === 'extra') { r.deductPrice = 0; r.includedRowIds = [] }
  if (r.kind !== 'extra' && form.value.details.rows.some(x => x.includedRowIds.includes(r.rowId))) {
    ElMessage.error('该行已被其他行作为附加项引用，不能改为非附加价类型；请先在引用它的行取消勾选，再修改价格性质')
    return
  }
  Object.assign(rowOriginal.value, r)
  rowDialog.value = false
}
function editConditions(row) {
  conditionRow.value = row
  conditionDraft.value = clone(row.conditions)
  unlimited.value = !row.conditions.length
  conditionDialog.value = true
}
function commitConditions() {
  if (!unlimited.value && (!conditionDraft.value.length || conditionDraft.value.some(c => !c.fieldId || c.operator === 'eq' && (c.value == null || c.value === '') || c.operator === 'range' && c.min == null && c.max == null))) {
    ElMessage.error('请至少添加一个取价条件并填写完整；如果确实不需要条件限制，请勾选上方的“不限制属性条件（任何属性都匹配）”')
    return
  }
  conditionRow.value.conditions = unlimited.value ? [] : clone(conditionDraft.value)
  conditionDialog.value = false
}
function summary(cs) {
  return cs.map(c => `${fields.value.find(f => f.id === c.fieldId)?.fieldName || '未知字段'} ${c.operator === 'eq' ? '=' + c.value : `${c.includeMin ? '[' : '('}${c.min ?? '不限'},${c.max ?? '不限'}${c.includeMax ? ']' : ')'}`}`).join(' · ') || '不限'
}
async function preview() {
  quote.value = null
  previewError.value = ''
  if (!previewProduct.value || !form.value.operationId) {
    ElMessage.error('请先在上方试算区域选择一个产品和工序，再进行试算')
    return
  }
  previewing.value = true
  try {
    quote.value = (await previewAttributePrice({ productId: previewProduct.value, operationId: form.value.operationId, operationAttributes: previewAttributes.value })).data
  } catch (e) {
    previewError.value = e.message || '试算失败，请检查价目表是否已保存并启用，以及试算产品的属性是否填写完整'
  } finally {
    previewing.value = false
  }
}
onMounted(async () => {
  loading.value = true
  try {
    const [o, u, p, f, c, q, perm] = await Promise.all([
      getOperationList({ pageSize: 1000 }), getUnitList(), getProductList({ pageSize: 1000 }),
      getCustomFields('product'), getCustomFields('work_order_task'), getReportQtySetting(),
      getDocumentPermission().catch(() => ({ data: null }))
    ])
    operations.value = o.data.list
    units.value = u.data
    products.value = p.data.list
    allFields.value = [...f.data, ...c.data]
    enableMultiUom.value = q.data?.enableMultiUom === true
    onlyCreatorEditPriceList.value = !!perm.data?.onlyCreatorEditPriceList
    await loadBill()
  } finally {
    loading.value = false
  }
})
</script>
<style scoped>
.browse-notice{margin-bottom:12px}.copyable,.browse-content{user-select:text;cursor:text;overflow-wrap:anywhere;white-space:pre-wrap}.price-table :deep(.cell){user-select:text}
.toolbar{display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin:16px 0}.mode-hint{margin-left:4px;font-size:14px;color:#606266}.header-form{display:grid;grid-template-columns:repeat(3,minmax(230px,1fr));margin-top:20px;gap:0 16px}.wide{grid-column:span 2}.hint{font-size:12px;color:#606266;line-height:1.8}.header-form .el-select{width:100%}.body-tools{padding-top:12px}
.price-sys-meta{margin-top:14px;padding-top:10px;border-top:1px solid var(--el-border-color-lighter,#ebeef5);font-size:12px;color:#909399;line-height:1.6}
@media(max-width:1100px){.header-form{grid-template-columns:repeat(2,minmax(230px,1fr))}}
</style>
