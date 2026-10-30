import assert from 'node:assert/strict'
import { test } from 'node:test'
import { cellCopyText, copyText } from './tableCellCopy.js'

test('copies visible text including zero, excludes controls and native editable targets', () => {
  let removed = false
  const content = { textContent: '  0  ', querySelectorAll: () => [{ remove() { removed = true } }] }
  const cell = { querySelector: () => ({ cloneNode: () => content }) }
  assert.equal(cellCopyText(cell, {}, null), '0')
  assert.equal(removed, true)
  assert.equal(cellCopyText(cell, { type: 'selection' }), '')
  assert.equal(cellCopyText(cell, {}, { closest: () => ({}) }), '')
  content.textContent = '进行中'
  assert.equal(cellCopyText(cell, {}), '进行中')
  content.textContent = '  '
  assert.equal(cellCopyText(cell, {}), '')
})

test('clipboard writes exact text and propagates refusal', async () => {
  let written
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { clipboard: {
    async writeText(text) { written = text }
  } } })
  await copyText('WO-001\n产品')
  assert.equal(written, 'WO-001\n产品')
  navigator.clipboard.writeText = async () => { throw new Error('denied') }
  await assert.rejects(copyText('WO-001'), /denied/)
})

test('HTTP fallback checks result and always removes temporary field and restores focus', async () => {
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: {} })
  let removed = 0, focused = 0, selected = 0
  globalThis.window = { getSelection: () => null }
  globalThis.document = {
    activeElement: { focus() { focused++ } },
    body: { appendChild() {} },
    createElement: () => ({ style: {}, select() { selected++ }, remove() { removed++ } }),
    execCommand: () => true
  }
  await copyText('0')
  document.execCommand = () => false
  await assert.rejects(copyText('WO-001'), /未允许/)
  assert.equal(removed, 2)
  assert.equal(focused, 2)
  assert.equal(selected, 2)
})
