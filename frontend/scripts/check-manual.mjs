import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parseChapters, parseBlocks, searchChapters } from '../src/help/parser.js'

const root = fileURLToPath(new URL('../../', import.meta.url))
const sources = [
  ['start', 'docs/manual/第一次使用.md'],
  ['pc', 'docs/30-使用手册-管理员（电脑端）.md'],
  ['mobile', 'docs/31-使用手册-工人（手机端）.md'],
  ['faq', 'docs/manual/遇到问题.md']
]
const chapters = sources.flatMap(([group, path]) => {
  const text = readFileSync(resolve(root, path), 'utf8')
  assert(!/此处放截图|待核实|Admin123|F001/.test(text), `未清理的占位或凭据：${path}`)
  return parseChapters(text, group)
})
assert.equal(new Set(chapters.map(chapter => chapter.id)).size, chapters.length, '章节地址重复')
assert(chapters.some(chapter => chapter.id === 'mobile/登录和找到自己的工单'), '手机帮助入口失效')
let images = 0
for (const chapter of chapters) {
  assert(chapter.blocks.length > 0, `空章节：${chapter.id}`)
  for (const block of chapter.blocks) {
    if (block.type !== 'image') continue
    assert(existsSync(resolve(root, `frontend/public${block.src}`)), `图片不存在：${block.src}`)
    images++
  }
  const refs = [...chapter.body.matchAll(/^!\[/gm)].length
  assert.equal(refs, chapter.blocks.filter(block => block.type === 'image').length, `不支持的图片格式：${chapter.id}`)
}
assert(searchChapters(chapters, '补报').some(chapter => chapter.title === '一键补报未完工序'))
assert.equal(searchChapters(chapters, '补报')[0].title, '一键补报未完工序', '应优先显示标题命中的操作')
assert(searchChapters(chapters, '报工 复核').length > 0)
assert(searchChapters(chapters, 'pDa').length > 0)
assert.equal(searchChapters(chapters, '不存在的关键词XYZ987').length, 0)
assert.equal(searchChapters(chapters, '   ').length, chapters.length)
assert.equal(parseBlocks('<script>alert(1)</script>')[0].type, 'p', '原始 HTML 只能作为文本')
assert.equal(parseBlocks('![外部图片](https://example.com/track.png)')[0].type, 'p', '不载入外部图片')
console.log(`手册检查通过：${chapters.length} 篇章节、${images} 处图片，搜索与正文安全检查通过`)
