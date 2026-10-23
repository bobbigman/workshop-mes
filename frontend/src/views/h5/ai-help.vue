<template>
  <main class="h5-help-page worker-chat" :style="{ height: height + 'px' }">
    <header class="h5-help-header"><router-link to="/h5/home">‹ 返回工作台</router-link><h1>AI操作帮助</h1><van-button size="small" :disabled="busy || ending" @click="newChat">新对话</van-button></header>
    <p class="help-note">询问怎么使用系统；实际数量和工资请到对应页面查看。</p>
    <p v-if="statusLoading" role="status">正在检查AI状态…</p>
    <p v-else-if="statusError" class="help-error" role="alert">AI状态加载失败 <van-button size="small" @click="loadStatus">重试</van-button></p>
    <p v-else-if="!ready" role="status">{{ status.enabled ? 'AI尚未配置，请联系管理员' : 'AI操作帮助已关闭' }}</p>
    <router-link class="help-link" to="/h5/help">查看操作手册和常见问题 ›</router-link>
    <div ref="chat" class="worker-messages" role="log" aria-live="polite">
      <div v-if="!messages.length" class="worker-examples">
        <van-button v-for="q in examples" :key="q" size="small" :disabled="!ready || busy" @click="draft = q">{{ q }}</van-button>
      </div>
      <article v-for="(m, i) in messages" :key="i" class="worker-message" :class="m.role">
        <b>{{ m.role === 'user' ? '我' : '操作帮助' }}</b><div class="worker-answer">{{ m.text }}</div>
        <template v-for="s in m.sources || []" :key="s.sourceId">
          <router-link v-if="sourceTopic(s)" class="help-link worker-source" :to="{ path: '/help', query: { topic: sourceTopic(s), from: 'h5' } }">依据：{{ s.chapter || s.title }} ›</router-link>
        </template>
      </article>
    </div>
    <p v-if="error" class="help-error" role="alert">{{ error }}</p>
    <form class="worker-composer" @submit.prevent="send">
      <label class="worker-input-label" for="worker-question">操作问题</label>
      <van-field id="worker-question" v-model="draft" placeholder="例如：报错数量怎么办？" type="textarea" rows="2" maxlength="500" :disabled="busy || ending" />
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
import { getWorkerHelpStatus, sendWorkerHelpMessage, deleteWorkerHelpConversation } from '@/api/workerHelp'
import { chapters } from '@/help/manual'

const examples = ['怎么扫码报工？', '在哪里查我的报工？', '报错数量怎么办？', '为什么找不到任务？']
const status = ref({ enabled: false, configured: false }), statusLoading = ref(true), statusError = ref(false)
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
function sourceTopic(source) {
  const group = source.sourceId?.startsWith('worker:mobile#') ? 'mobile' : source.sourceId?.startsWith('worker:faq#') ? 'faq' : null
  return group ? chapters.find(c => c.group === group && c.title === source.chapter)?.id : null
}
async function loadStatus() {
  statusLoading.value = true; statusError.value = false
  try { status.value = (await getWorkerHelpStatus()).data }
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
    const res = await sendWorkerHelpMessage({ message: question, conversationId: conversationId || null }, controller.signal)
    if (disposed || identity !== localStorage.getItem('token')) return
    conversationId = res.data.conversationId
    messages.value.push({ role: 'assistant', text: res.data.answer, sources: res.data.sources })
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
    if (conversationId) await deleteWorkerHelpConversation(conversationId)
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
.help-note { margin: 8px 0; }
.worker-messages { flex: 1; min-height: 0; overflow-y: auto; overscroll-behavior: contain; }
.worker-examples { display: flex; flex-wrap: wrap; gap: 8px; padding: 16px 0; }
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
