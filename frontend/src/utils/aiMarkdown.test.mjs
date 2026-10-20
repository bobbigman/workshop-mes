import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { parseAiMarkdown, parseInline } from './aiMarkdown.js'

describe('parseAiMarkdown', () => {
  it('renders conclusion, heading, table and ordered list', () => {
    const blocks = parseAiMarkdown([
      '**结论：目前能确认的延期工单只有 1 条。**',
      '',
      '## 已取证的延期工单',
      '',
      '| 工单号 | 产品（编号） | 计划数 | 剩余 |',
      '|---|:---:|---:|---:|',
      '| A0001 | 打蛋器（DDQ-001） | 10 | 10 |',
      '| A0002 | 汤锅 | 3 | 1 |',
      '',
      '**判定口径** [doc_abc]：交期已过。',
      '',
      '1. 入口：**生产管理 → 执行监控**',
      '2. 再看质检看板'
    ].join('\n'))

    assert.deepEqual(blocks.map(b => b.type), ['p', 'h', 'table', 'p', 'ol'])
    assert.equal(blocks[0].tone, 'lead')
    assert.equal(blocks[1].level, 2)
    assert.deepEqual(blocks[2].align, ['left', 'center', 'right', 'right'])
    assert.equal(blocks[2].head.length, 4)
    assert.equal(blocks[2].rows.length, 2)
    assert.equal(blocks[2].rows[0][1][0].text, '打蛋器（DDQ-001）')
    assert.equal(blocks[3].parts.find(p => p.type === 'src').text, 'doc_abc')
    assert.equal(blocks[4].start, 1)
    assert.equal(blocks[4].items.length, 2)
    assert.ok(blocks[4].items[0].some(p => p.type === 'strong' && p.text === '生产管理 → 执行监控'))
  })

  it('keeps model HTML as text instead of markup', () => {
    const blocks = parseAiMarkdown('<img src=x onerror=alert(1)> 与 **加粗**')
    assert.equal(blocks[0].type, 'p')
    assert.equal(blocks[0].parts[0].text, '<img src=x onerror=alert(1)> 与 ')
    assert.deepEqual(blocks[0].parts[1], { type: 'strong', text: '加粗' })
  })

  it('does not turn javascript links into links', () => {
    const parts = parseInline('[点我](javascript:alert(1))')
    assert.equal(parts.length, 1)
    assert.equal(parts[0].type, 'text')
    assert.equal(parts[0].text, '[点我](javascript:alert(1))')
  })

  it('keeps code fences verbatim', () => {
    const blocks = parseAiMarkdown('```sql\nselect * from t where a = 1\n```')
    assert.deepEqual(blocks[0], { type: 'code', lang: 'sql', text: 'select * from t where a = 1' })
  })

  it('uses line breaks inside a wrapped paragraph', () => {
    const blocks = parseAiMarkdown('第一行\n第二行')
    assert.deepEqual(blocks[0].parts.map(p => p.type), ['text', 'br', 'text'])
  })

  it('marks warning paragraphs', () => {
    const blocks = parseAiMarkdown('⚠️ 注：以上为已确认数据')
    assert.equal(blocks[0].tone, 'warn')
  })

  it('does not mistake a table separator for a horizontal rule', () => {
    const blocks = parseAiMarkdown('| A | B |\n| --- | --- |\n| 1 | 2 |')
    assert.equal(blocks[0].type, 'table')
    assert.equal(blocks.length, 1)
  })

  it('handles empty and plain text', () => {
    assert.deepEqual(parseAiMarkdown(''), [])
    assert.deepEqual(parseAiMarkdown('   '), [])
    assert.equal(parseAiMarkdown('无数据')[0].parts[0].text, '无数据')
  })

  it('accepts compact AI table separators and pads missing cells', () => {
    const [table] = parseAiMarkdown('| 单号 | 数量 |\n|-|:-:|\n| A0001 |')
    assert.equal(table.type, 'table')
    assert.deepEqual(table.rows[0][1], [])
  })

  it('keeps mismatched headers and excess row data visible', () => {
    assert.equal(parseAiMarkdown('| A | B |\n|---|\n| 1 | 2 |')[0].type, 'p')
    const blocks = parseAiMarkdown('| A | B |\n|---|---|\n| 1 | 2 | extra |')
    assert.equal(blocks[1].parts[0].text, '| 1 | 2 | extra |')
  })

  it('preserves incomplete syntax and escaped table pipes', () => {
    assert.equal(parseInline('**尚未结束')[0].text, '**尚未结束')
    assert.equal(parseAiMarkdown('```\n未结束')[0].text, '未结束')
    const [table] = parseAiMarkdown('| 产品 |\n|---|\n| A\\|B |')
    assert.equal(table.rows[0][0][0].text, 'A|B')
  })
})
