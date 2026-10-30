import assert from 'node:assert/strict'
import { mock, test } from 'node:test'
import { createRenderer, defineComponent, Fragment, h, nextTick, ref } from 'vue'
import { tableLayoutKey } from '../utils/tableLayout.js'

const messages = [], nativeTables = []
const NativeTable = defineComponent({
  name: 'ElTable',
  inheritAttrs: false,
  setup(_, { attrs, slots, expose }) {
    const state = { selection: [1], sort: 'ascending', clearSelection() { this.selection = [] } }
    nativeTables.push(state)
    expose(state)
    return () => h('table-stub', { ...attrs }, [...(slots.default?.() || []), ...(slots.append?.() || [])])
  }
})
const Column = defineComponent({
  name: 'ElTableColumn',
  setup(_, { attrs, slots }) {
    return () => h('column-stub', { ...attrs }, slots.default?.({ row: { name: 'Alpha' }, $index: 0 }))
  }
})
mock.module('element-plus', { namedExports: {
  ElTable: NativeTable,
  ElMessage: Object.fromEntries(['warning', 'error', 'success'].map(type => [type, text => messages.push({ type, text })]))
} })
const { default: MesTable } = await import('./MesTable.js')

function node(type, props = {}) { return { type, props, children: [], parent: null } }
const renderer = createRenderer({
  createElement: type => node(type), createText: text => ({ ...node('text'), text }),
  createComment: text => ({ ...node('comment'), text }),
  insert(child, parent, anchor = null) {
    if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1)
    const index = anchor ? parent.children.indexOf(anchor) : -1
    parent.children.splice(index < 0 ? parent.children.length : index, 0, child)
    child.parent = parent
  },
  remove(child) { if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1) },
  setElementText(el, text) { el.text = text; el.children = [] },
  setText(el, text) { el.text = text },
  patchProp(el, key, oldValue, value) { el.props[key] = value },
  parentNode: el => el.parent,
  nextSibling: el => el.parent?.children[el.parent.children.indexOf(el) + 1] || null
})
function find(root, type) {
  if (root.type === type) return root
  for (const child of root.children) { const hit = find(child, type); if (hit) return hit }
}

test('table wrapper persists independent layouts and resets in place without discarding business state', async () => {
  const records = new Map([['factoryCode', 'A'], ['userId', '7']])
  const storage = { getItem: k => records.get(k) ?? null, setItem: (k, v) => records.set(k, v), removeItem: k => records.delete(k) }
  globalThis.localStorage = storage
  const root = node('root'), columnsVisible = ref(true), editValue = ref('unchanged')
  let api, events = 0
  function mount() {
    const app = renderer.createApp(defineComponent({
      setup() {
        return () => h(MesTable, {
          tableKey: 'test-list', ref: value => { api = value }, height: 290,
          class: 'pc-table', onHeaderDragend: () => events++
        }, {
          default: () => [h(Fragment, null, columnsVisible.value ? [
            h(Column, { 'column-key': 'name', minWidth: 120, fixed: 'left' }, {
              default: () => h('input-stub', { value: editValue.value })
            })
          ] : [])],
          append: () => [h('original-append', null, 'original footer')]
        })
      }
    }))
    app.mount(root)
    return app
  }
  let app = mount()
  let table = find(root, 'table-stub'), col = find(root, 'column-stub')
  assert.equal(table.props.height, 290)
  assert.ok(table.props.class.includes('pc-table'))
  assert.equal(col.props.fixed, 'left')
  assert.equal(col.props.minWidth, 120)
  assert.ok(find(root, 'original-append'))
  table.props.onHeaderDragend.forEach(fn => fn(330, 120, { columnKey: 'name', resizable: true }))
  await nextTick()
  assert.equal(events, 1)
  assert.equal(find(root, 'column-stub').props.width, 330)
  assert.equal(JSON.parse(records.get(tableLayoutKey('test-list', 'A', '7'))).name, 330)
  columnsVisible.value = false
  await nextTick()
  columnsVisible.value = true
  await nextTick()
  assert.equal(find(root, 'column-stub').props.width, 330)
  app.unmount()
  app = mount()
  assert.equal(find(root, 'column-stub').props.width, 330)
  const instanceCount = nativeTables.length
  find(root, 'button').props.onClick()
  await nextTick()
  assert.equal(find(root, 'column-stub').props.width, '')
  assert.equal(nativeTables.length, instanceCount)
  assert.deepEqual(api.selection, [1])
  assert.equal(api.sort, 'ascending')
  assert.equal(find(root, 'input-stub').props.value, 'unchanged')
  assert.equal(records.has(tableLayoutKey('test-list', 'A', '7')), false)
  api.clearSelection()
  assert.deepEqual(api.selection, [])
  table = find(root, 'table-stub')
  storage.setItem = () => { throw new Error('quota') }
  table.props.onHeaderDragend.forEach(fn => fn(350, 120, { columnKey: 'name', resizable: true }))
  await nextTick()
  assert.equal(find(root, 'column-stub').props.width, 350)
  assert.ok(messages.some(m => m.type === 'error' && m.text.includes('保存表格列宽失败')))
  storage.removeItem = () => { throw new Error('blocked') }
  find(root, 'button').props.onClick()
  await nextTick()
  assert.equal(find(root, 'column-stub').props.width, 350)
  assert.ok(messages.some(m => m.text.includes('清除表格列宽记录失败')))
  app.unmount()
})

test('legacy order settings and corrupt storage get the expected recovery', async () => {
  const oldKey = 'mes.order.column-widths.v1:A:7'
  const records = new Map([['factoryCode', 'A'], ['userId', '7'], [oldKey, '{"orderNo":420}']])
  globalThis.localStorage = { getItem: k => records.get(k) ?? null, setItem: (k, v) => records.set(k, v), removeItem: k => records.delete(k) }
  const root = node('root')
  function mount() {
    const app = renderer.createApp(defineComponent({ setup: () => () => h(MesTable, { tableKey: 'order', legacyOrderWidths: true }, {
      default: () => [h(Column, { 'column-key': 'orderNo', minWidth: 260 })]
    }) }))
    app.mount(root)
    return app
  }
  let app = mount()
  assert.equal(find(root, 'column-stub').props.width, 420)
  find(root, 'button').props.onClick()
  await nextTick()
  assert.equal(records.has(oldKey), false)
  assert.equal(find(root, 'column-stub').props.width, '')
  app.unmount()
  records.set(tableLayoutKey('order', 'A', '7'), 'broken')
  app = mount()
  assert.equal(find(root, 'column-stub').props.width, '')
  assert.ok(messages.some(m => m.type === 'warning' && m.text.includes('读取表格列宽失败')))
  app.unmount()
})
