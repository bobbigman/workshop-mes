import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import assert from 'node:assert/strict'
import { test } from 'node:test'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
const source = readFileSync(new URL('./MyStatements.vue', import.meta.url), 'utf8')
const { descriptor } = parse(source)
test('组件脚本和模板可编译', () => {
  compileScript(descriptor, { id: '241' })
  assert.deepEqual(compileTemplate({ source: descriptor.template.content, filename: 'MyStatements.vue', id: '241' }).errors, [])
})
function setup(api = {}) {
  const context = { ref: value => ({ value }), onMounted: () => {}, onBeforeUnmount: () => {},
    getMyStatements: async () => ({ data: [] }), getMyStatementItems: async () => ({ data: [] }), ...api }
  vm.createContext(context)
  vm.runInContext(descriptor.scriptSetup.content.replace(/^import .*$/gm, '') + '\nglobalThis.state = {statement, items, loading, error, periodType, periodValue};', context)
  return context
}
test('系数、计时1分钟、固定和历史计价展示', () => {
  const c = setup()
  const row = { priceType: 1, goodQty: 10, unitPrice: 2, wageCoeff: .8 }
  assert.match(c.calculation(row), /10件.*2.00元\/件.*系数0.8/)
  assert.match(c.calculation({ ...row, priceType: 2, durationMinutes: 1 }), /^1分钟/)
  assert.match(c.calculation({ ...row, priceType: 2, durationMinutes: 83 }), /^1小时23分/)
  assert.equal(c.calculation({ ...row, priceType: 3 }), '固定2.00元')
  assert.equal(c.calculation({ ...row, priceType: 0 }), '历史记录／计价方式未记录')
})
test('空记录和请求失败均清空旧金额，失败可见', async () => {
  const c = setup()
  await c.load(true)
  assert.equal(c.state.statement.value, null)
  assert.equal(c.state.loading.value, false)
  c.getMyStatements = async () => { throw new Error('无权查看该工资单') }
  await c.load(true)
  assert.equal(c.state.error.value, '无权查看该工资单')
  assert.equal(c.state.statement.value, null)
})
test('快速切换周期，旧请求不覆盖新结果', async () => {
  let resolveOld
  let calls = 0
  const c = setup({ getMyStatements: () => ++calls === 1 ? new Promise(r => { resolveOld = r }) : Promise.resolve({ data: [{ id: 2, periodType: 2, periodValue: '2026-W42' }] }) })
  const old = c.load(true)
  await c.load(true)
  resolveOld({ data: [{ id: 1, periodType: 1, periodValue: '2026-09' }] })
  await old
  assert.equal(c.state.statement.value.id, 2)
})
