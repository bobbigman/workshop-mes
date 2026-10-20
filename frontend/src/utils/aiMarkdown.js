// 把 AI 回答里的 Markdown 解析成结构化块，交给 AiAnswer.vue 用 Vue 模板渲染。
// 只支持模型实际会用的语法：标题、段落、加粗、行内代码、代码块、列表、引用、表格、分隔线、链接。
// 这里不产出 HTML 字符串，避免 v-html 把模型输出当代码执行。

const FENCE = /^\s*```([\w-]*)\s*$/
const HEADING = /^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$/
const HORIZONTAL_RULE = /^\s{0,3}(?:-{3,}|\*{3,}|_{3,})\s*$/
const QUOTE = /^\s{0,3}>\s?(.*)$/
const UNORDERED = /^\s{0,3}[-*+]\s+(.+)$/
const ORDERED = /^\s{0,3}(\d{1,3})[.、)]\s+(.+)$/

// 行内语法按优先级匹配：代码 → 加粗 → 链接 → 来源编号。
const INLINE = /(`[^`\n]+`)|(\*\*[^*\n]+\*\*)|(\[[^\]\n]+\]\(https?:\/\/[^\s)]+\))|(\[(?:doc|evt|src)_[^\]\n]{1,200}\]|\b(?:doc|evt|src)_[A-Za-z0-9]+(?:#[^\s，。；：、（）()<>[\]]+)?)/g

export function parseAiMarkdown(source) {
  const text = String(source ?? '').replace(/\r\n?/g, '\n')
  const lines = text.split('\n')
  const blocks = []
  let i = 0

  while (i < lines.length) {
    const line = lines[i]

    if (!line.trim()) {
      i++
      continue
    }

    const fence = line.match(FENCE)
    if (fence) {
      const body = []
      i++
      while (i < lines.length && !/^\s*```\s*$/.test(lines[i])) body.push(lines[i++])
      if (i < lines.length) i++
      blocks.push({ type: 'code', lang: fence[1] || '', text: body.join('\n') })
      continue
    }

    const heading = line.match(HEADING)
    if (heading) {
      blocks.push({
        type: 'h',
        level: Math.min(6, heading[1].length),
        parts: parseInline(heading[2].trim())
      })
      i++
      continue
    }

    if (HORIZONTAL_RULE.test(line)) {
      blocks.push({ type: 'hr' })
      i++
      continue
    }

    if (isTableStart(lines, i)) {
      i = readTable(lines, i, blocks)
      continue
    }

    if (QUOTE.test(line)) {
      const quoted = []
      while (i < lines.length && QUOTE.test(lines[i])) {
        quoted.push(parseInline(lines[i].match(QUOTE)[1]))
        i++
      }
      blocks.push({ type: 'quote', lines: quoted })
      continue
    }

    if (UNORDERED.test(line)) {
      const items = []
      while (i < lines.length) {
        const m = lines[i].match(UNORDERED)
        if (!m) break
        items.push(parseInline(m[1]))
        i++
      }
      blocks.push({ type: 'ul', items })
      continue
    }

    if (ORDERED.test(line)) {
      const start = Number(line.match(ORDERED)[1]) || 1
      const items = []
      while (i < lines.length) {
        const m = lines[i].match(ORDERED)
        if (!m) break
        items.push(parseInline(m[2]))
        i++
      }
      blocks.push({ type: 'ol', start, items })
      continue
    }

    // 普通段落：连续非空行合为一段，行内换行保留为 <br>。
    const paragraph = [line.trim()]
    i++
    while (i < lines.length && lines[i].trim() && !isBlockStart(lines, i)) {
      paragraph.push(lines[i].trim())
      i++
    }
    blocks.push(makeParagraph(paragraph))
  }

  return blocks
}

export function parseInline(source) {
  const text = String(source ?? '')
  const parts = []
  let last = 0
  let match

  INLINE.lastIndex = 0
  while ((match = INLINE.exec(text)) !== null) {
    if (match.index > last) parts.push(textPart(text.slice(last, match.index)))

    const token = match[0]
    if (match[1]) {
      parts.push({ type: 'code', text: token.slice(1, -1) })
    } else if (match[2]) {
      parts.push({ type: 'strong', text: token.slice(2, -2) })
    } else if (match[3]) {
      const link = token.match(/^\[([^\]\n]+)\]\((https?:\/\/[^\s)]+)\)$/)
      parts.push(link
        ? { type: 'link', text: link[1], href: link[2] }
        : textPart(token))
    } else if (match[4]) {
      parts.push({ type: 'src', text: token.startsWith('[') ? token.slice(1, -1) : token })
    }

    last = match.index + token.length
  }

  if (last < text.length) parts.push(textPart(text.slice(last)))
  return parts
}

function textPart(text) {
  return text ? { type: 'text', text } : null
}

function makeParagraph(lines) {
  const parts = []
  lines.forEach((line, index) => {
    if (index > 0) parts.push({ type: 'br' })
    parts.push(...parseInline(line))
  })
  const filtered = parts.filter(Boolean)
  const plain = filtered
    .map(part => (part.type === 'br' ? ' ' : part.text))
    .join('')
    .trim()

  let tone = ''
  if (/^⚠/.test(plain)) tone = 'warn'
  else if (/^(结论|重点|关键)/.test(plain)) tone = 'lead'

  return { type: 'p', tone, parts: filtered }
}

function isBlockStart(lines, index) {
  const line = lines[index]
  return Boolean(
    line.match(FENCE) ||
    line.match(HEADING) ||
    HORIZONTAL_RULE.test(line) ||
    QUOTE.test(line) ||
    UNORDERED.test(line) ||
    ORDERED.test(line) ||
    isTableStart(lines, index)
  )
}

function isTableStart(lines, index) {
  const line = lines[index]
  if (!line || !line.includes('|')) return false
  const next = lines[index + 1]
  if (!next || !next.includes('|')) return false
  const separator = splitRow(next)
  return separator.length === splitRow(line).length && separator.every(cell => /^:?-+:?$/.test(cell))
}

function readTable(lines, index, blocks) {
  const head = splitRow(lines[index]).map(parseInline)
  const align = splitRow(lines[index + 1]).map(cell => {
    const left = cell.startsWith(':')
    const right = cell.endsWith(':')
    if (left && right) return 'center'
    if (right) return 'right'
    return 'left'
  })

  let i = index + 2
  const rows = []
  while (i < lines.length && lines[i].trim() && lines[i].includes('|')) {
    const row = splitRow(lines[i])
    // 列数异常时保留原行作为后续普通文本，不能把多出的业务数据截掉。
    if (row.length > head.length) break
    while (row.length < head.length) row.push('')
    rows.push(row.slice(0, head.length).map(parseInline))
    i++
  }

  while (align.length < head.length) align.push('left')
  blocks.push({ type: 'table', head, align: align.slice(0, head.length), rows })
  return i
}

function splitRow(line) {
  const source = String(line).trim().replace(/^\|/, '').replace(/\|$/, '')
  const cells = []
  let current = ''

  for (let i = 0; i < source.length; i++) {
    const ch = source[i]
    if (ch === '\\' && source[i + 1] === '|') {
      current += '|'
      i++
      continue
    }
    if (ch === '|') {
      cells.push(current.trim())
      current = ''
      continue
    }
    current += ch
  }
  cells.push(current.trim())
  return cells
}
