// 手册仅使用本地维护的标题、段落、列表、加粗和图片；不解释 HTML。
export function inlineParts(text) {
  return String(text).split(/(\*\*[^*]+\*\*)/g).filter(Boolean).map(part => ({
    bold: part.startsWith('**') && part.endsWith('**'),
    text: part.startsWith('**') && part.endsWith('**') ? part.slice(2, -2) : part
  }))
}

export function parseBlocks(source) {
  const blocks = []
  for (const raw of source.split(/\r?\n/)) {
    const line = raw.trim()
    if (!line) continue
    const image = line.match(/^!\[([^\]]+)\]\((\/manual\/[a-zA-Z0-9_/-]+\.(?:png|jpg|webp))\)$/)
    if (image) {
      blocks.push({ type: 'image', alt: image[1], src: image[2] })
      continue
    }
    const list = line.match(/^(?:(\d+)\. |(-) )(.*)$/)
    if (list) {
      const type = list[1] ? 'ol' : 'ul'
      const previous = blocks.at(-1)
      const item = inlineParts(list[3])
      if (previous?.type === type) previous.items.push(item)
      else blocks.push({ type, items: [item] })
      continue
    }
    const heading = line.match(/^### (.*)$/)
    blocks.push({ type: heading ? 'h3' : 'p', parts: inlineParts(heading ? heading[1] : line) })
  }
  return blocks
}

export function parseChapters(source, group) {
  return source.split(/^## /m).slice(1).map(section => {
    const [title, ...lines] = section.split(/\r?\n/)
    const body = lines.join('\n').trim()
    return {
      id: `${group}/${title.trim()}`,
      group,
      title: title.trim(),
      body,
      blocks: parseBlocks(body),
      search: `${title}\n${body}`.replace(/\*\*/g, '').toLocaleLowerCase()
    }
  })
}

export function searchChapters(chapters, query) {
  const words = query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean)
  const matches = chapters.filter(chapter => words.every(word => chapter.search.includes(word)))
  const score = chapter => words.reduce((sum, word) => sum + (chapter.title.toLocaleLowerCase().includes(word) ? 1 : 0), 0)
  return matches.sort((a, b) => score(b) - score(a))
}

export function searchExcerpt(chapter, query) {
  const body = chapter.body.replace(/!\[[^\]]*\]\([^)]*\)/g, '').replace(/\*\*/g, '').replace(/\s+/g, ' ').trim()
  const word = query.trim().toLocaleLowerCase().split(/\s+/)[0] || ''
  const position = body.toLocaleLowerCase().indexOf(word)
  const start = Math.max(0, position - 24)
  return `${start ? '…' : ''}${body.slice(start, start + 110)}${body.length > start + 110 ? '…' : ''}`
}
