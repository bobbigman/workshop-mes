import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { randomUUID } from 'node:crypto'
import { canOpenReportManagement, isReportManagementEntry, managementTimeError } from '../../utils/reportManagement.js'

const source = readFileSync(new URL('./index.vue', import.meta.url), 'utf8').match(/<script setup>([\s\S]*?)<\/script>/)[1].replace(/^import .*$/gm, '')
function fixture(role, query, overrides = {}) {
  const mounted = [], activated = [], calls = [], navigation = []
  const api = (name, data) => async (...args) => { calls.push(name); return { data } }
  const eventTarget = { addEventListener() {}, removeEventListener() {} }
  const ctx = {
    ref: value => ({ value }), computed: fn => ({ get value() { return fn() } }), watch() {},
    onMounted: fn => mounted.push(fn), onActivated: fn => activated.push(fn), onUnmounted() {},
    useRoute: () => ({ query }), useRouter: () => ({ replace: value => { navigation.push(value); for (const key of Object.keys(query)) delete query[key]; Object.assign(query, value.query || {}) } }),
    localStorage: { getItem: key => key === 'role' ? String(role) : '1' },
    window: { matchMedia: () => ({ matches: true, ...eventTarget }), visualViewport: { height: 640, ...eventTarget }, innerHeight: 640, ...eventTarget },
    canOpenReportManagement, isReportManagementEntry, managementTimeError, crypto: { randomUUID }, console: { error() {} },
    ElMessage: { error() {}, warning() {}, success() {} }, ElMessageBox: { confirm: async () => {} }, isUpstreamSoftConfirm: () => false,
    getReportList: api('reports', { list: [] }), getOrderList: api('orders', { list: [] }),
    getPendingWorkSessions: api('pending', []), reportPendingWorkSession: api('savePending', null),
    getOrderByNo: api('orderDetail', { id: 1, status: 1, tasks: [] }), ...overrides
  }
  runInNewContext(source + '\nthis.form = { createDlg, createForm, orderDetail, resetCreate, proxyDlg, batchDlg, pendingDlg, pendingEditDlg, pendingSaving, pendingTimes, batchForm, batchDetail, openPending, selectPending, savePending, loadBatchOrder, onProxyClosed, onBatchClosed, returnFromManagement };', ctx)
  const activate = async () => { for (const fn of activated) await fn(); await Promise.resolve() }
  return { form: ctx.form, calls, navigation, activate, mount: async () => { for (const fn of mounted) await fn(); await activate() } }
}

test('工单首次进入及缓存恢复均直接打开新增，带入当前工单并加载详情', async () => {
  const query = { order: 'CSYC-GM20260901' }
  const orders = []
  const f = fixture(1, query, { getOrderByNo: async no => { orders.push(no); return { data: { orderNo: no, status: 1, tasks: [] } } } })
  await f.mount()
  assert.equal(f.form.createDlg.value, true)
  assert.equal(f.form.createForm.value.orderNo, 'CSYC-GM20260901')
  assert.equal(f.form.orderDetail.value.orderNo, 'CSYC-GM20260901')
  f.form.createDlg.value = false; f.form.resetCreate()
  await f.activate()
  assert.equal(f.form.createDlg.value, false)
  for (const no of ['CSYC-GM20260902', 'CSYC-GM20260901']) {
    query.order = no
    await f.activate()
    assert.equal(f.form.createDlg.value, true)
    assert.equal(f.form.createForm.value.orderNo, no)
    assert.equal(f.form.orderDetail.value.orderNo, no)
    f.form.createDlg.value = false; f.form.resetCreate()
  }
  assert.deepEqual(orders, ['CSYC-GM20260901', 'CSYC-GM20260902', 'CSYC-GM20260901'])
})

test('历史报工列表加载失败也不阻止打开新增和带入工单', async () => {
  const f = fixture(1, { order: 'CSYC-GM20260901' }, { getReportList: async () => { throw Error('list unavailable') } })
  await assert.rejects(f.mount(), /list unavailable/)
  assert.equal(f.form.createDlg.value, true)
  assert.equal(f.form.createForm.value.orderNo, 'CSYC-GM20260901')
  assert.ok(f.calls.includes('orderDetail'))
})

test('工作台直达只开对应表单，不读取报工管理列表；关闭回工作台', async () => {
  for (const [role, action, field] of [[3, 'proxy', 'proxyDlg'], [1, 'proxy', 'proxyDlg'], [1, 'batch', 'batchDlg'], [1, 'pending', 'pendingDlg']]) {
    const f = fixture(role, { from: 'h5', action }); await f.mount()
    assert.equal(f.form[field].value, true); assert.ok(!f.calls.includes('reports'))
    if (action === 'proxy') f.form.onProxyClosed()
    else if (action === 'batch') f.form.onBatchClosed()
    else f.form.returnFromManagement(action)
    assert.deepEqual(f.navigation, ['/h5/home'])
  }
})
test('越权或未知action不打开表单，不请求管理数据', async () => {
  for (const [role, action] of [[2, 'proxy'], [2, 'pending'], [3, 'batch'], [3, 'pending'], [1, 'unknown']]) {
    const f = fixture(role, { from: 'h5', action }); await f.mount()
    assert.deepEqual(f.calls, []); assert.equal(f.form.proxyDlg.value || f.form.batchDlg.value || f.form.pendingDlg.value, false)
  }
})
test('普通PC入口仍加载列表，关闭不强制跳工作台', async () => {
  const f = fixture(1, {}); await f.mount(); f.form.onBatchClosed()
  assert.ok(f.calls.includes('reports')); assert.deepEqual(f.navigation, [])
})
test('开工记录补报失败保留详情，成功关闭两层表单后回工作台', async () => {
  let fail = true
  const f = fixture(1, { from: 'h5', action: 'pending' }, { reportPendingWorkSession: async () => { if (fail) throw Error('fixture'); return { data: null } } })
  await f.mount()
  f.form.selectPending({ id: 1, startTime: '2026-10-22T08:00:01', endTime: '2026-10-22T09:00:02' })
  await assert.rejects(f.form.savePending())
  assert.equal(f.form.pendingSaving.value, false); assert.equal(f.form.pendingEditDlg.value, true); assert.equal(f.form.pendingDlg.value, true)
  fail = false; await f.form.savePending()
  assert.equal(f.form.pendingEditDlg.value, false); assert.equal(f.form.pendingDlg.value, false)
  f.form.returnFromManagement('pending'); assert.deepEqual(f.navigation, ['/h5/home']); assert.ok(!f.calls.includes('reports'))
})
test('切换补报工单时旧请求不能回填，清空立即失效旧详情', async () => {
  const pending = {}
  const f = fixture(1, { from: 'h5', action: 'batch' }, { getOrderByNo: no => new Promise(resolve => { pending[no] = resolve }) })
  await f.mount()
  f.form.batchForm.value.orderNo = 'A'; const first = f.form.loadBatchOrder()
  f.form.batchForm.value.orderNo = 'B'; const second = f.form.loadBatchOrder()
  pending.B({ data: { id: 2, status: 1, tasks: [] } }); await second
  pending.A({ data: { id: 1, status: 1, tasks: [] } }); await first
  assert.equal(f.form.batchDetail.value.id, 2)
  f.form.batchForm.value.orderNo = ''; await f.form.loadBatchOrder(); assert.equal(f.form.batchDetail.value, null)
})
