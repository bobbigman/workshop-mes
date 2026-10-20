import { test } from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import { presentAiAnswer, visibleAiSources } from '../../utils/aiAssistantPresentation.js'
const file = fs.readFileSync(new URL('./index.vue', import.meta.url), 'utf8')
const { descriptor } = parse(file)
const script = descriptor.scriptSetup.content

test('Vue脚本模板编译，停止和发送互斥且停止可点', () => {
  compileScript(descriptor, { id: 'assistant-258' })
  assert.deepEqual(compileTemplate({ source: descriptor.template.content, filename: 'index.vue', id: 'assistant-258' }).errors, [])
  const stop = file.match(/<el-button v-if="busy"[^>]*>/)[0]
  assert.ok(!/disabled|loading/.test(stop))
  assert.match(file, /<el-button v-else type="primary"/)
  assert.ok(!file.includes('为你整理'))
})
test('点角色展开/收起，同support不清空，跨mode清空，忙碌不切换', () => {
  const context = vm.createContext({ selectedRole: { value: 'boss' }, questionsOpen: { value: false }, busy: { value: false }, clears: 0 })
  const click = script.match(/function onRoleClick\(value\) \{[\s\S]*?\n\}/)[0]
  const change = script.match(/let previousMode = mode.value[\s\S]*?\n\}/)[0]
  vm.runInContext(`const mode = { get value() { return selectedRole.value === 'boss' ? 'progress' : 'support' } }; function onNewChat() { clears++ }; ${change}; ${click}`, context)
  vm.runInContext(`onRoleClick('boss')`, context); assert.equal(context.questionsOpen.value, true)
  vm.runInContext(`onRoleClick('boss')`, context); assert.equal(context.questionsOpen.value, false)
  vm.runInContext(`onRoleClick('supervisor'); onRoleClick('finance'); onRoleClick('worker')`, context)
  assert.equal(context.clears, 1)
  vm.runInContext(`onRoleClick('boss'); busy.value=true; onRoleClick('worker')`, context)
  assert.equal(context.clears, 2); assert.equal(context.selectedRole.value, 'boss')
})
test('点题直接发送并收起，忙碌时不覆盖输入', () => {
  const context = vm.createContext({ canChat: { value: true }, busy: { value: false }, questionsOpen: { value: true }, input: { value: '' }, sends: 0 })
  vm.runInContext(`function onSend() { sends++ }; ${script.match(/function useExample\(q\) \{[\s\S]*?\n\}/)[0]}; useExample('设备坏了'); busy.value=true; useExample('重复')`, context)
  assert.equal(context.input.value, '设备坏了'); assert.equal(context.questionsOpen.value, false); assert.equal(context.sends, 1)
})
test('文档来源隐藏、引用脱离展示，真实证据和未知引用保持', () => {
  const sources = [{ type: 'support_doc', sourceId: 'doc_abc#现场异常_20' }, { type: 'report', sourceId: 'evt_12' }, { type: 'operation', sourceId: 'src_1' }]
  assert.equal(presentAiAnswer('点【异常】提交。[doc_abc#现场异常_20]\n来源：\n', sources), '点【异常】提交。')
  assert.equal(presentAiAnswer('[evt_12] 数量50 [doc_unknown]', sources), '[evt_12] 数量50 [doc_unknown]')
  assert.deepEqual(visibleAiSources(sources).map(s => s.sourceId), ['evt_12', 'src_1'])
  assert.equal(sources.length, 3)
})
