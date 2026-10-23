<template>
  <main class="h5-help-page worker-chat" :style="{ height: height + 'px' }">
    <header class="h5-help-header"><router-link to="/h5/home">‹ 返回工作台</router-link><h1>AI操作帮助</h1><van-button size="small" :disabled="busy || ending" @click="newChat">新对话</van-button></header>
    <p v-if="statusLoading" role="status">正在检查AI状态…</p>
    <p v-else-if="statusError" class="help-error" role="alert">AI状态加载失败 <van-button size="small" @click="loadStatus">重试</van-button></p>
    <p v-else-if="!ready" role="status">{{ status.enabled ? 'AI尚未配置，请联系管理员' : 'AI操作帮助已关闭' }}</p>
    <template v-else>
      <div class="role-tabs" aria-label="选择提问角色">
        <button v-for="r in ROLES" :key="r.value" type="button" class="role-tab" :class="{ active: selectedRole === r.value }" :disabled="busy" @click="onRoleClick(r.value)">{{ r.label }}</button>
      </div>
      <section v-if="questionsOpen" class="question-panel" aria-label="常用问题">
        <div class="question-panel-heading"><span>想了解什么？</span><button type="button" class="panel-toggle" @click="questionsOpen = false">收起</button></div>
        <div class="question-list">
          <button v-for="(q, index) in examples" :key="q" type="button" class="question-item" :disabled="busy || !ready" @click="useExample(q)"><span class="question-number">{{ String(index + 1).padStart(2, '0') }}</span><span class="question-text">{{ q }}</span></button>
        </div>
      </section>
    </template>
    <div ref="chat" class="worker-messages" role="log" aria-live="polite">
      <article v-for="(m, i) in messages" :key="i" class="worker-message" :class="m.role">
        <b>{{ m.role === 'user' ? '我' : '操作帮助' }}</b><div class="worker-answer">{{ m.text }}</div>
        <template v-for="s in m.sources || []" :key="s.sourceId">
          <div v-if="s.title" class="help-link worker-source">依据：{{ s.title }}<template v-if="s.chapter"> · {{ s.chapter }}</template></div>
        </template>
      </article>
    </div>
    <p v-if="error" class="help-error" role="alert">{{ error }}</p>
    <form class="worker-composer" @submit.prevent="send">
      <label class="worker-input-label" for="worker-question">操作问题</label>
      <van-field id="worker-question" v-model="draft" placeholder="例如：今天要交货的工单有哪几张？" type="textarea" rows="2" maxlength="500" :disabled="busy || ending" />
      <van-button v-if="busy" native-type="button" @click="cancel">取消本次</van-button>
      <van-button v-else type="primary" native-type="submit" :disabled="!ready || !draft.trim() || ending">发送</van-button>
    </form>
  </main>
</template>
<script setup>
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { Button as VanButton, Field as VanField } from 'vant'
import 'vant/lib/index.css'
import '@/styles/h5Help.css'
import { getAiStatus, sendAiMessage, deleteAiConversation } from '@/api/aiAssistant'

/** 与电脑端 AI 助手（ai-assistant/index.vue）同源的角色与常见问题。 */
const ROLES = [
  { value: 'boss', label: '老板' },
  { value: 'supervisor', label: '车间主管' },
  { value: 'finance', label: '财务' },
  { value: 'worker', label: '车间师傅' },
  { value: 'engineer', label: 'AI' }
]
const QUESTIONS = {
  boss: [
    '今天要交货的工单有哪几张？',
    '明天就要交的货，进度到哪了？',
    '现在有哪几张单已经逾期了？逾期多久？',
    '客户催货时，执行中的单里哪几张剩得最多？',
    '刚下的单怎么到现在还没人动？',
    '现在车间一共在做多少张单？',
    '临期 3 天内要交的单有哪些？',
    '被取消的工单有哪些？',
    '客户在微信上突然问起某单，我在手机上怎么一秒查到？',
    '做完的工单系统会自动结束吗？'
  ],
  supervisor: [
    '今天到期的单有几张在赶？',
    '逾期最久的是哪张？卡在哪道工序？',
    '现在有几条设备异常还没处理？',
    '拖最久的那条异常是谁报的、谁在修？',
    '工人不会用手机扫码报工怎么办？',
    '报工被退回了，工人来找我闹怎么办？',
    '待复核的报工我该怎么处理？',
    '工人说自己报错数了，能自己改吗？',
    '班组长在手机上派工，工人那边能看到吗？',
    '超报被系统硬拦了，我怎么放行？'
  ],
  finance: [
    '工单计划数和完成数对不上，差在哪？',
    '不良品会不会被算进完成数里？',
    '客户投诉了，这活是谁做的、什么时候做的？',
    '返工重做的件，算不算产量？',
    '两个人合作做一批，数量怎么分？',
    '临时工和正式工，系统里怎么区分标签？',
    '交期被改过，我在哪看记录？',
    '报工记录能导出 Excel 带去对账吗？',
    '被退回的报工，会不会被算进完成数里？',
    '月底对账时，良品+不良合计怎么对得上计划数？'
  ],
  worker: [
    '工人怎么用手机扫码报工？',
    'PDA 扫码枪怎么用？',
    '扫了二维码打不开页面怎么办？',
    '报错数量了，我自己能改吗？',
    '不良品怎么报？必须填什么？',
    '一道活分三次做，能分三次报吗？',
    '班组长派给我的活，手机上在哪看？',
    '没智能手机的工人怎么报工？',
    '设备坏了我要怎么上报？',
    '怎么看这道工序的作业指导书？'
  ],
  engineer: [
    '销售报价的时候，AI 能帮着预估报价吗？',
    '销售报价的时候，AI 能帮着算算料钱吗？',
    '排工单的时候，AI 能帮着看看料齐不齐、缺不缺吗？',
    'AI 能帮着按轻重缓急把工单排个先后顺序吗？',
    '车间干的活，能跟财务的账串起来吗？',
    '工人干完活，财务不用再对着 Excel 抄一遍吧？',
    '车间出问题，能自动发消息到班组长手机上吗？',
    'AI 除了查工单，还能帮我们干点啥？',
    '除了金蝶，还能连别的软件吗？',
    '以后想加新功能，留路子了吗？'
  ]
}

const selectedRole = ref('boss')
const questionsOpen = ref(true)
const examples = computed(() => QUESTIONS[selectedRole.value])
const mode = computed(() => selectedRole.value === 'boss' ? 'progress' : 'support')
let previousMode = mode.value

const status = ref({ enabled: false, configured: false, model: null }), statusLoading = ref(true), statusError = ref(false)
const messages = ref([]), draft = ref(''), busy = ref(false), ending = ref(false), error = ref(''), chat = ref(null)
const height = ref(window.visualViewport?.height || window.innerHeight)
const ready = computed(() => !statusLoading.value && !statusError.value && status.value.enabled && status.value.configured)
let conversationId = '', controller, disposed = false
const identity = localStorage.getItem('token')

function checkIdentity() {
  if (identity === localStorage.getItem('token')) return
  cancel(); conversationId = ''; messages.value = []; draft.value = ''
  error.value = '登录身份已变化，请返回工作台重新打开帮助'
}
function resize() { height.value = window.visualViewport?.height || window.innerHeight }
function onRoleClick(value) {
  if (busy.value) return
  if (selectedRole.value === value) { questionsOpen.value = !questionsOpen.value; return }
  selectedRole.value = value
  if (mode.value !== previousMode) newChat()
  previousMode = mode.value
  questionsOpen.value = true
}
function useExample(q) {
  if (!ready.value || busy.value) return
  questionsOpen.value = false
  draft.value = q
  send()
}
async function loadStatus() {
  statusLoading.value = true; statusError.value = false
  try { status.value = (await getAiStatus()).data }
  catch (err) { statusError.value = true; console.error('[AI操作帮助] 读取状态失败', err) }
  finally { statusLoading.value = false }
}
async function send() {
  if (!ready.value || busy.value || ending.value || !draft.value.trim()) return
  if (identity !== localStorage.getItem('token')) { checkIdentity(); return }
  const question = draft.value.trim()
  busy.value = true; error.value = ''; controller = new AbortController()
  messages.value.push({ role: 'user', text: question })
  try {
    const res = await sendAiMessage({ conversationId: conversationId || undefined, mode: mode.value, message: question }, controller.signal)
    if (disposed || identity !== localStorage.getItem('token')) return
    conversationId = res.data.conversationId
    messages.value.push({ role: 'assistant', text: res.data.answer, sources: res.data.sources || [] })
    draft.value = ''
    await nextTick(); if (chat.value) chat.value.scrollTop = chat.value.scrollHeight
  } catch (err) {
    if (disposed) return
    messages.value.pop()
    error.value = controller.signal.aborted ? '本次已取消，问题已保留，可重试' : '回答失败，问题已保留。可重试或新建对话'
    console.error('[AI操作帮助] 发送未完成', err)
  } finally { busy.value = false }
}
function cancel() { controller?.abort() }
async function newChat() {
  if (busy.value || ending.value) return
  ending.value = true; error.value = ''
  try {
    if (conversationId) await deleteAiConversation(conversationId)
    conversationId = ''; messages.value = []; draft.value = ''
  } catch (err) { error.value = '结束对话失败，请重试'; console.error('[AI操作帮助] 结束对话失败', err) }
  finally { ending.value = false }
}
onMounted(() => { loadStatus(); window.visualViewport?.addEventListener('resize', resize); window.addEventListener('resize', resize); window.addEventListener('storage', checkIdentity) })
onUnmounted(() => { disposed = true; cancel(); window.visualViewport?.removeEventListener('resize', resize); window.removeEventListener('resize', resize); window.removeEventListener('storage', checkIdentity) })
</script>
<style scoped>
.worker-chat { display: flex; flex-direction: column; min-height: 0; padding-bottom: max(12px, env(safe-area-inset-bottom)); }
.h5-help-header { margin-bottom: 0; flex: none; }
.role-tabs { display: flex; flex-wrap: wrap; gap: 6px; padding: 6px 0; flex: none; }
.role-tab { flex: 1 1 auto; min-width: 56px; border: 1px solid #e5e8ed; border-radius: 16px; padding: 6px 10px; background: #fff; color: #667085; font: inherit; font-size: 13px; cursor: pointer; }
.role-tab.active { background: #edf5ff; color: #1685e6; border-color: #b7d9fb; font-weight: 600; }
.role-tab:disabled { cursor: not-allowed; opacity: .55; }
.question-panel { flex: none; margin: 4px 0 8px; padding: 10px 12px; background: #fff; border: 1px solid #e9ecf0; border-radius: 10px; max-height: 26vh; overflow: auto; }
.question-panel-heading { display: flex; justify-content: space-between; align-items: center; color: #7b8593; font-size: 12px; margin-bottom: 6px; }
.panel-toggle { border: 0; background: transparent; color: #1685e6; font-size: 12px; cursor: pointer; padding: 0; }
.question-list { display: flex; flex-direction: column; gap: 4px; }
.question-item { display: flex; gap: 8px; text-align: left; border: 0; border-radius: 7px; padding: 8px 10px; background: #f7f9fc; color: #34445e; font: inherit; font-size: 13px; line-height: 1.5; cursor: pointer; }
.question-item:disabled { cursor: not-allowed; opacity: .55; }
.question-number { color: #a0aaba; font-size: 11px; flex: none; padding-top: 1px; }
.question-text { min-width: 0; }
.worker-messages { flex: 1; min-height: 0; overflow-y: auto; overscroll-behavior: contain; }
.worker-message { background: var(--app-card); border-radius: 8px; padding: 12px; margin: 10px 0; overflow-wrap: anywhere; }
.worker-message.user { background: var(--app-primary-light); }
.worker-message b { font-size: 13px; }
.worker-answer { white-space: pre-wrap; line-height: 1.65; margin-top: 6px; font-size: 15px; }
.worker-source { display: block; font-size: 13px; }
.worker-input-label { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
.worker-composer { display: flex; align-items: end; gap: 8px; flex: none; padding-top: 10px; }
.worker-composer .van-field { flex: 1; min-width: 0; border-radius: 8px; }
.worker-composer .van-button { flex: none; }
</style>
