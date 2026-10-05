<template>
  <el-card class="message-rules">
    <template #header>消息规则与发送记录</template>
    <el-alert type="info" :closable="false" show-icon>
      报工和工单变化可自动提醒。通常在每分钟任务的下一轮发送；交期与事件共用每日额度，测试消息不计额度。
      不良率按每条新增报工记录计算，严格大于阈值才提醒。多人报工会分别提醒。
    </el-alert>
    <el-alert v-if="rules.some(row => row.targetChannel !== 'group')" type="warning" :closable="false" show-icon style="margin-top:12px">
      旧会话规则已停用。可查看原配置，请按原模板和成员重新建立群机器人规则；历史记录保留。
    </el-alert>
    <el-alert v-if="rules.some(row => row.targetChannel === 'group' && !row.webhookKey && !defaultWebhookConfigured)" type="warning" :closable="false" show-icon style="margin-top:12px">
      部分群规则未配置发送目标，请填写本规则 Webhook 或保存全局默认 Webhook。
    </el-alert>
    <el-tabs v-model="tab">
      <el-tab-pane label="消息规则" name="rules">
        <div class="toolbar">
          <el-button type="primary" @click="openRule()">新建规则</el-button>
          <el-button :loading="loading" @click="loadRules">刷新</el-button>
        </div>
        <el-table :data="rules" v-loading="loading" empty-text="暂无规则，新建后只匹配后续业务事件">
          <el-table-column prop="name" label="规则名称" min-width="130" />
          <el-table-column label="事件" min-width="110"><template #default="{ row }">{{ events[row.eventType] }}</template></el-table-column>
          <el-table-column label="通道" width="100"><template #default="{ row }">{{ row.targetChannel === 'group' ? '群消息' : '旧规则（停用）' }}</template></el-table-column>
          <el-table-column label="条件" min-width="150"><template #default="{ row }">{{ conditionText(row) }}</template></el-table-column>
          <el-table-column label="接收人/@人" min-width="160" show-overflow-tooltip><template #default="{ row }">{{ row.toUser || (row.targetChannel === 'group' ? '只发群，不 @' : '原默认接收人') }}</template></el-table-column>
          <el-table-column label="状态" width="80"><template #default="{ row }"><el-tag :type="row.enabled ? 'success' : 'info'">{{ row.targetChannel !== 'group' ? '已停用' : row.enabled ? '启用' : '停用' }}</el-tag></template></el-table-column>
          <el-table-column label="操作" width="150"><template #default="{ row }">
            <el-button link type="primary" @click="openRule(row)">{{ row.targetChannel === 'group' ? '编辑' : '查看' }}</el-button>
            <el-button link :disabled="saving || row.targetChannel !== 'group'" @click="toggleRule(row)">{{ row.enabled ? '停用' : '启用' }}</el-button>
          </template></el-table-column>
        </el-table>
      </el-tab-pane>
      <el-tab-pane label="发送记录" name="deliveries">
        <div class="toolbar"><el-button :loading="loadingDeliveries" @click="loadDeliveries">刷新记录</el-button></div>
        <el-table :data="deliveries" v-loading="loadingDeliveries" empty-text="暂无事件发送记录">
          <el-table-column prop="ruleName" label="规则" min-width="110" />
          <el-table-column label="事件" min-width="110"><template #default="{ row }">{{ events[row.eventType] }}</template></el-table-column>
          <el-table-column label="发生时间" min-width="160"><template #default="{ row }">{{ formatTime(row.occurredAt) }}</template></el-table-column>
          <el-table-column prop="toUser" label="接收人" min-width="120" show-overflow-tooltip />
          <el-table-column label="状态" min-width="110"><template #default="{ row }">{{ statuses[row.status] || row.status }}</template></el-table-column>
          <el-table-column prop="error" label="失败原因" min-width="150" show-overflow-tooltip />
          <el-table-column label="操作" width="170"><template #default="{ row }">
            <el-button link type="primary" @click="showDetail(row)">详情</el-button>
            <el-button v-if="['failed', 'partial'].includes(row.status) && rules.some(rule => rule.id === row.ruleId && rule.targetChannel === 'group')" link type="primary" :disabled="acting" @click="retry(row)">重试</el-button>
            <el-button v-if="row.status === 'unknown'" link type="warning" @click="openVerify(row)">核实</el-button>
          </template></el-table-column>
        </el-table>
        <el-pagination v-model:current-page="page" :page-size="20" :total="total" layout="prev, pager, next, total" @current-change="loadDeliveries" />
      </el-tab-pane>
    </el-tabs>
    <el-dialog v-model="ruleDialog" :title="ruleId ? '编辑规则' : '新建规则'" width="640px" :close-on-click-modal="false">
      <el-alert v-if="legacyRule" type="warning" :closable="false">旧规则仅供查看，请关闭后新建群机器人规则。</el-alert>
      <el-form label-width="110px" :disabled="legacyRule">
        <el-form-item label="规则名称" required><el-input v-model="form.name" maxlength="64" show-word-limit /></el-form-item>
        <el-form-item label="事件" required><el-select v-model="form.eventType" @change="changeEvent"><el-option v-for="(label, key) in events" :key="key" :label="label" :value="key" /></el-select></el-form-item>
        <el-form-item label="发送到">群机器人 Webhook</el-form-item>
        <el-form-item v-if="!legacyRule" label="本规则 Webhook"><el-input v-model="form.webhookKey" type="password" show-password placeholder="留空用全厂默认；******** 表示不修改；清空恢复默认" clearable /></el-form-item>
        <el-form-item v-if="form.eventType === 'report_created'" label="条件">
          <el-select v-model="form.conditionType" @change="changeCondition"><el-option label="每次提醒" value="always" /><el-option label="本次不良率超过阈值" value="defect_rate" /></el-select>
        </el-form-item>
        <el-form-item v-if="form.eventType === 'abnormal_reported'" label="异常类型">
          <el-select v-model="form.conditionType">
            <el-option label="每次异常" value="always" />
            <el-option label="仅设备故障（@机修填接收人）" value="abnormal_device" />
            <el-option label="仅物料短缺（@仓库填接收人）" value="abnormal_material" />
            <el-option label="仅质量异常（@质检填接收人）" value="abnormal_quality" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="form.conditionType === 'defect_rate'" label="不良率阈值"><el-input-number v-model="form.threshold" :min="0" :max="100" :precision="2" /> %</el-form-item>
        <el-form-item label="群内 @人"><el-input v-model="form.toUser" maxlength="512" placeholder="选填企微 UserId，逗号分隔；支持 @all；留空只发群，不继承默认成员" /></el-form-item>
        <el-form-item label="消息模板" required><el-input v-model="form.template" type="textarea" :rows="4" maxlength="1000" show-word-limit /></el-form-item>
        <el-form-item label="可用占位符"><div class="tokens"><el-button v-for="token in placeholders" :key="token" size="small" @click="form.template += `{${token}}`">{{ tokenText(token) }}</el-button></div></el-form-item>
        <el-form-item label="预览（示例）"><pre class="preview">{{ preview }}</pre></el-form-item>
        <el-form-item label="启用"><el-switch v-model="form.enabled" /><span class="hint">须有本规则或全局默认 Webhook；总开关关闭时不会发送</span></el-form-item>
      </el-form>
      <el-alert type="info" :closable="false">编辑只影响后续事件；停用会取消尚未发送项，正在发送的消息可能已被企微接受。</el-alert>
      <template #footer><el-button @click="ruleDialog = false">取消</el-button><el-button type="primary" :loading="saving" :disabled="legacyRule" @click="saveRule">保存</el-button></template>
    </el-dialog>
    <el-dialog v-model="detailDialog" title="发送详情" width="760px">
      <template v-if="detail">
        <pre class="preview">{{ detail.content }}</pre>
        <p>状态：{{ statuses[detail.status] }}；累计尝试：{{ detail.attempts }}；失败接收人：{{ detail.failedUsers || '无' }}</p>
        <p v-if="detail.handledAt">管理员 #{{ detail.handledBy }} 于 {{ formatTime(detail.handledAt) }}：{{ detail.handlingNote }}</p>
        <pre v-if="detail.error" class="preview">{{ detail.error }}</pre>
        <el-table :data="attempts" empty-text="尚未尝试发送">
          <el-table-column label="时间" min-width="160"><template #default="{ row }">{{ formatTime(row.createdAt) }}</template></el-table-column>
          <el-table-column prop="toUser" label="本次接收人" min-width="140" show-overflow-tooltip />
          <el-table-column label="结果" width="110"><template #default="{ row }">{{ statuses[row.outcome] || row.outcome }}</template></el-table-column>
          <el-table-column label="说明" min-width="140" show-overflow-tooltip><template #default="{ row }">{{ row.handlingNote ? `管理员 #${row.handledBy}：${row.handlingNote}` : row.error }}</template></el-table-column>
        </el-table>
      </template>
    </el-dialog>
    <el-dialog v-model="verifyDialog" title="核实未知发送结果" width="540px" :close-on-click-modal="false">
      <p>请与接收人核实。这条消息可能已送达，直接重发可能产生重复通知。</p>
      <p v-if="!verifyCanRequeue">旧会话通道已停止，仅可确认已发送；未收到的记录保持未知，不重新排队。</p>
      <el-radio-group v-model="verification.sent"><el-radio :value="true">确认已发送</el-radio><el-radio v-if="verifyCanRequeue" :value="false">确认未发送，重新排队</el-radio></el-radio-group>
      <el-input v-model="verification.note" type="textarea" :rows="3" maxlength="256" placeholder="填写核实说明（必填）" style="margin-top:16px" />
      <template #footer><el-button @click="verifyDialog = false">取消</el-button><el-button type="primary" :loading="acting" @click="verify">保存核实结果</el-button></template>
    </el-dialog>
  </el-card>
</template>

<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getWechatRules, createWechatRule, updateWechatRule, getWechatDeliveries, getWechatAttempts, retryWechatDelivery, verifyWechatDelivery } from '@/api/wechat-events'

defineProps({ defaultWebhookConfigured: { type: Boolean, default: false } })
const legacyRule = computed(() => !!ruleId.value && form.value.targetChannel !== 'group')

const events = { report_created: '报工成功', order_started: '工单开始', order_completed: '工单结束', abnormal_reported: '异常上报' }
const statuses = { pending: '待发', processing: '处理中', success: '已接受', retry: '待重试', failed: '失败', partial: '部分成功', unknown: '结果未知', cancelled: '已取消', confirmed: '人工确认已发送', requeued: '管理员重新排队' }
const templates = {
  report_created: '【小蜜蜂报工】报工成功\n工单：{工单号} 产品：{产品编号} {产品名称}\n工序：{工序} 良品：{良品数} 不良：{不良数} 不良率：{不良率}\n时间：{事件时间}',
  order_started: '【小蜜蜂报工】工单开始\n工单：{工单号} 产品：{产品编号} {产品名称}\n时间：{事件时间}',
  order_completed: '【小蜜蜂报工】工单结束\n工单：{工单号} 产品：{产品编号} {产品名称}\n时间：{事件时间}',
  abnormal_reported: '【小蜜蜂报工】异常上报\n类型：{异常类型}\n描述：{问题描述}\n工单：{工单号} 产品：{产品编号} {产品名称}\n上报人：{上报人}\n时间：{事件时间}'
}
const kinds = { abnormal_device: '仅设备故障', abnormal_material: '仅物料短缺', abnormal_quality: '仅质量异常' }
const conditionText = row => row.conditionType === 'defect_rate' ? `本次不良率 > ${row.threshold}%` : (kinds[row.conditionType] || '每次提醒')
const tab = ref('rules'), rules = ref([]), deliveries = ref([]), attempts = ref([])
const page = ref(1), total = ref(0), loading = ref(false), loadingDeliveries = ref(false), saving = ref(false), acting = ref(false)
const ruleDialog = ref(false), ruleId = ref(null), form = ref({})
const detailDialog = ref(false), detail = ref(null), verifyDialog = ref(false), verifyId = ref(null), verification = ref({ sent: true, note: '' })
const verifyCanRequeue = computed(() => rules.value.some(rule => rule.id === deliveries.value.find(row => row.id === verifyId.value)?.ruleId && rule.targetChannel === 'group'))
const placeholders = computed(() => ['工单号', '产品编号', '产品名称', '事件时间', ...(form.value.eventType === 'report_created' ? ['工序', '良品数', '不良数', '不良率'] : []), ...(form.value.eventType === 'abnormal_reported' ? ['异常类型', '问题描述', '上报人'] : [])])
const sample = { 工单号: 'WO-示例', 产品编号: 'P-001', 产品名称: '示例产品', 事件时间: '2026-10-02 09:00:00', 工序: '裁切', 良品数: '89', 不良数: '11', 不良率: '11.00%', 异常类型: '物料短缺', 问题描述: '缺料暂停', 上报人: '张三' }
const preview = computed(() => (form.value.template || '').replace(/\{([^{}]+)\}/g, (_, key) => sample[key] ?? `【未知：${key}】`))
const tokenText = token => `{${token}}`
const formatTime = value => value ? value.replace('T', ' ').slice(0, 19) : '—'
onMounted(loadRules)
watch(tab, value => { if (value === 'deliveries') loadDeliveries() })
async function loadRules() { loading.value = true; try { const res = await getWechatRules(); rules.value = res.data || [] } finally { loading.value = false } }
async function loadDeliveries() { loadingDeliveries.value = true; try { const res = await getWechatDeliveries({ page: page.value, pageSize: 20 }); deliveries.value = res.data?.list || []; total.value = res.data?.total || 0 } finally { loadingDeliveries.value = false } }
function openRule(row) {
  ruleId.value = row?.id || null
  form.value = row ? { name: row.name, eventType: row.eventType, conditionType: row.conditionType, threshold: row.threshold, targetChannel: row.targetChannel || 'session', webhookKey: row.webhookKey || '', toUser: row.toUser || '', template: row.template, enabled: row.enabled }
    : { name: '', eventType: 'abnormal_reported', conditionType: 'abnormal_material', threshold: null, targetChannel: 'group', webhookKey: '', toUser: '', template: templates.abnormal_reported, enabled: false }
  ruleDialog.value = true
}
function changeCondition() { form.value.threshold = form.value.conditionType === 'defect_rate' ? 10 : null }
function changeEvent() { form.value.conditionType = 'always'; form.value.threshold = null; form.value.template = templates[form.value.eventType] }
async function saveRule() {
  if (legacyRule.value) return
  form.value.targetChannel = 'group'
  if (!form.value.name.trim() || !form.value.template.trim()) return ElMessage.warning('请填写规则名称和消息模板')
  if ([...form.value.template.matchAll(/\{([^{}]+)\}/g)].some(m => !placeholders.value.includes(m[1]))) return ElMessage.warning('模板含该事件不支持的占位符')
  saving.value = true
  try { if (ruleId.value) await updateWechatRule(ruleId.value, { ...form.value }); else await createWechatRule({ ...form.value }); ruleDialog.value = false; ElMessage.success('规则已保存'); await loadRules() } finally { saving.value = false }
}
async function toggleRule(row) {
  if (row.enabled) {
    try { await ElMessageBox.confirm('停用会取消尚未发送项；重新启用只匹配新事件。', '停用规则', { type: 'warning' }) }
    catch (error) { if (error === 'cancel' || error === 'close') return; throw error }
  }
  saving.value = true
  try { await updateWechatRule(row.id, { ...row, enabled: !row.enabled }); await loadRules() } finally { saving.value = false }
}
async function showDetail(row) { const res = await getWechatAttempts(row.id); attempts.value = res.data || []; detail.value = row; detailDialog.value = true }
async function retry(row) {
  acting.value = true
  try { await retryWechatDelivery(row.id); ElMessage.success('已重新排队'); await loadDeliveries() } finally { acting.value = false }
}
function openVerify(row) { verifyId.value = row.id; verification.value = { sent: true, note: '' }; verifyDialog.value = true }
async function verify() {
  if (!verification.value.note.trim()) return ElMessage.warning('请填写核实说明')
  acting.value = true
  try { await verifyWechatDelivery(verifyId.value, { ...verification.value }); verifyDialog.value = false; ElMessage.success('核实结果已保存'); await loadDeliveries() } finally { acting.value = false }
}
</script>

<style scoped>
.message-rules { margin-top: 20px; }
.toolbar { display: flex; gap: 8px; margin: 12px 0; }
.tokens { display: flex; flex-wrap: wrap; gap: 6px; }
.tokens .el-button { margin-left: 0; }
.preview { white-space: pre-wrap; overflow-wrap: anywhere; background: #f5f7fa; padding: 12px; width: 100%; margin: 0 0 12px; line-height: 1.6; }
.hint { color: #909399; margin-left: 10px; font-size: 12px; }
.el-pagination { margin-top: 16px; }
</style>
