import assert from 'node:assert/strict'
import { test } from 'node:test'
import { parseColumnWidths, readColumnWidths, tableColumnKey, tableColumnLayout, tableLayoutKey } from './tableLayout.js'

test('storage isolates tables, factories and users, including separator characters', () => {
  const keys = [
    tableLayoutKey('orders', 'A', '1'), tableLayoutKey('reports', 'A', '1'),
    tableLayoutKey('orders', 'B', '1'), tableLayoutKey('orders', 'A', '2'),
    tableLayoutKey('orders', 'A:1', '2'), tableLayoutKey('orders', 'A', '1:2')
  ]
  assert.equal(new Set(keys).size, keys.length)
})

test('validates saved widths and rejects corrupt storage containers', () => {
  assert.deepEqual(parseColumnWidths(null), {})
  assert.deepEqual(parseColumnWidths('{"name":260,"small":29,"large":10001,"text":"120","decimal":120.5,"min":30}'), { name: 260, min: 30 })
  for (const raw of ['broken', 'null', '[]', '123', '"text"']) assert.throws(() => parseColumnWidths(raw))
})

test('loads legacy order settings only when new settings are absent', () => {
  const records = new Map([['legacy', '{"orderNo":310,"cf-7":190}']])
  const storage = { getItem: key => records.get(key) ?? null }
  assert.deepEqual(readColumnWidths(storage, 'new', 'legacy'), { orderNo: 310, 'cf-7': 190 })
  records.set('new', '{}')
  assert.deepEqual(readColumnWidths(storage, 'new', 'legacy'), {})
  assert.throws(() => readColumnWidths({ getItem() { throw new Error('denied') } }, 'new'), /denied/)
})

test('column identity survives reorder, visibility changes and explicit-key renaming', () => {
  const a = { prop: 'qty', label: '计划数' }, b = { prop: 'qty', label: '实际数' }
  const widths = { [tableColumnKey(a)]: 180, [tableColumnKey(b)]: 230, 'cf-7': 270 }
  assert.equal(tableColumnLayout(b, undefined, widths).width, 230)
  assert.equal(tableColumnLayout(a, undefined, widths).width, 180)
  assert.equal(tableColumnLayout({ 'column-key': 'cf-7', label: '重命名后' }, 7, widths).width, 270)
  assert.equal(tableColumnLayout({ 'column-key': 'cf-8', label: '新增列' }, 8, widths).width, '')
})

test('reset restores fixed or flexible defaults, preserving min widths and fixed placement', () => {
  const flexible = { prop: 'name', minWidth: 120, fixed: 'left' }
  assert.equal(tableColumnLayout(flexible, null, { [tableColumnKey(flexible, null)]: 300 }).width, 300)
  assert.equal(tableColumnLayout(flexible, null, {}).width, '')
  assert.equal(tableColumnLayout({ prop: 'qty', width: '110' }, null, {}).width, '110')
  assert.deepEqual(flexible, { prop: 'name', minWidth: 120, fixed: 'left' })
})

test('selection, expand, index and operation columns remain usable; explicit switches win', () => {
  for (const props of [{ type: 'selection', width: 48 }, { type: 'expand' }, { type: 'index' }, { label: '操作' }]) {
    const layout = tableColumnLayout(props, null, { [tableColumnKey(props, null)]: 300 })
    assert.equal(layout.resizable, false)
    assert.equal(layout.showOverflowTooltip, false)
    assert.equal(layout.width, props.width ?? '')
  }
  assert.equal(tableColumnLayout({ resizable: false }, null, {}).resizable, false)
  assert.equal(tableColumnLayout({ 'show-overflow-tooltip': false }, null, {}).showOverflowTooltip, false)
  assert.equal(tableColumnLayout({}, null, {}, false).showOverflowTooltip, false)
  assert.equal(tableColumnLayout({ showOverflowTooltip: true }, null, {}, false).showOverflowTooltip, true)
})
