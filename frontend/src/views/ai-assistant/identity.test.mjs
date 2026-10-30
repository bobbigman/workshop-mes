import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { parse } from '@vue/compiler-sfc'
import { createAiIdentityBoundary } from '../../utils/aiIdentityBoundary.js'

const script = parse(fs.readFileSync(new URL('./index.vue', import.meta.url), 'utf8')).descriptor.scriptSetup.content
const body = script.replace(/^import[\s\S]*?from\s+['"][^'"]+['"]\s*$/gm, '')
function setup() {
  const storage = new Map([['token', 'A'], ['role', '1'], ['userId', '1'], ['factoryCode', 'A']])
  const pending = [], mounted = [], unmounted = [], activated = [], events = new Map()
  const request = () => new Promise((resolve, reject) => pending.push({ resolve, reject }))
  const context = vm.createContext({
    ref: value => ({ value }), computed: fn => ({ get value() { return fn() } }), inject: () => null,
    onMounted: fn => mounted.push(fn), onUnmounted: fn => unmounted.push(fn), onActivated: fn => activated.push(fn),
    nextTick: async () => {}, createAiIdentityBoundary, AbortController,
    useRouter: () => ({ replace() {} }), ElMessage: { error() {}, warning() {} },
    localStorage: { getItem: key => storage.get(key) ?? null },
    window: { matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
      addEventListener: (key, fn) => events.set(key, fn), removeEventListener: key => events.delete(key), setInterval: () => 1, clearInterval() {} },
    getAiStatus: request, sendAiMessage: request, searchAiWorkOrders: request, searchAiReportEvidence: request,
    getOrder: request, deleteAiConversation: async () => {},
    VALUE_TIPS: {}, presentAiAnswer: x => x, visibleAiSources: x => x,
    hasAnswerTable: () => false, formatAiBusinessDate: x => x,
  })
  vm.runInContext(body + `\nglobalThis.page = { onSend, onPage, onEvPage, openOrder, onNewChat, input, messages, busy, status, conversationId, detail, detailDlg, boundary };`, context)
  context.page.status.value = { enabled: true, configured: true }
  return { page: context.page, storage, pending, mounted, activated, unmounted, events }
}

test('实际PC页面：换厂后迟到消息不落新会话，旧finally不覆盖新请求', async () => {
  const s = setup(); s.page.input.value = '查A厂'; const old = s.page.onSend()
  await new Promise(resolve => setImmediate(resolve))
  assert.equal(s.pending.length, 1)
  s.storage.set('token', 'B'); s.storage.set('factoryCode', 'B'); s.page.boundary.check()
  assert.equal(s.page.messages.value.length, 0)
  s.page.status.value = { enabled: true, configured: true }; s.page.input.value = '查B厂'
  const fresh = s.page.onSend(); await new Promise(resolve => setImmediate(resolve))
  s.pending[0].resolve({ data: { conversationId: 'A', answer: 'A厂秘密' } }); await old
  assert.equal(s.page.busy.value, true)
  assert.equal(s.page.messages.value.some(m => m.text === 'A厂秘密'), false)
  s.pending[1].resolve({ data: { conversationId: 'B', answer: 'B厂结果' } }); await fresh
  assert.equal(s.page.conversationId.value, 'B'); assert.equal(s.page.busy.value, false)
})

test('实际PC页面：分页和详情迟到结果丢弃，缓存激活清空，卸载取消', async () => {
  const s = setup(); s.page.conversationId.value = 'A'
  const message = { workOrders: { pageSize: 20, filters: {} } }
  const page = s.page.onPage(message, 2)
  s.storage.set('token', 'B'); s.activated.forEach(fn => fn())
  s.pending[0].resolve({ data: { secret: 'A厂分页' } }); await page
  assert.equal(message.workOrders.secret, undefined)
  const detail = s.page.openOrder(1); s.storage.set('token', 'C')
  s.pending[1].resolve({ data: { secret: 'B厂详情' } }); await detail
  assert.equal(s.page.detail.value, null); assert.equal(s.page.detailDlg.value, false)
  s.unmounted.forEach(fn => fn())
  assert.equal(s.page.boundary.current(s.page.boundary.begin()), false)
})

test('实际PC页面：旧错误不能进入新身份，清空对话使进行中的结果失效', async () => {
  const s = setup(); s.page.input.value = '查进度'; const old = s.page.onSend()
  await new Promise(resolve => setImmediate(resolve)); await s.page.onNewChat()
  s.pending[0].reject(new Error('旧错误')); await old
  assert.equal(s.page.messages.value.length, 0); assert.equal(s.page.busy.value, false)
})

test('实际PC页面：聊天期间打开详情，不丢弃聊天或留下忙碌状态', async () => {
  const s = setup(); s.page.input.value = '查进度'; const chat = s.page.onSend()
  await new Promise(resolve => setImmediate(resolve))
  const detail = s.page.openOrder(1)
  s.pending[1].resolve({ data: { id: 1 } }); await detail
  assert.equal(s.page.busy.value, true)
  s.pending[0].resolve({ data: { conversationId: 'A', answer: 'A厂结果' } }); await chat
  assert.equal(s.page.busy.value, false)
  assert.equal(s.page.messages.value.some(m => m.text === 'A厂结果'), true)
})
