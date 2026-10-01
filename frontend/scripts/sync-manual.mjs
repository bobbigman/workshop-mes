import { readFileSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { parseChapters } from '../src/help/parser.js'

const root = fileURLToPath(new URL('../../', import.meta.url))
const input = [
  ['start', 'docs/manual/第一次使用.md'],
  ['pc', 'docs/30-使用手册-管理员（电脑端）.md'],
  ['mobile', 'docs/31-使用手册-工人（手机端）.md'],
  ['faq', 'docs/manual/遇到问题.md']
]
const chapters = input.flatMap(([group, file]) => parseChapters(readFileSync(resolve(root, file), 'utf8'), group))
function section(id) {
  const item = chapters.find(chapter => chapter.id === id)
  if (!item) throw new Error(`同步手册失败：章节不存在 ${id}`)
  return `## ${item.title}\n\n${item.body.replace(/^!\[[^\]]*\]\([^)]*\)\s*$/gm, '').trim()}`
}
const basic = ['pc/建账号、改密码', 'pc/建部门、把人加入部门', 'pc/配单位、不良品项和工序', 'pc/批量导入', 'pc/配工艺路线和产品', 'pc/上传作业指导书、图纸和图片', 'pc/按需使用的辅助功能']
const orders = ['pc/下工单', 'pc/派工：把工序派给工人', 'mobile/我的任务（被派工了看这里）', 'pc/打印二维码流转卡', 'pc/改工单、结束、撤回和取消', 'pc/查进度、查报表、看看板与执行监控']
const reporting = ['pc/电脑报工', 'pc/复核：通过或退回报工', 'pc/报错数量怎么改', 'pc/一键补报未完工序', 'pc/处理现场异常上报', 'mobile/用手机摄像头扫码', 'mobile/用 PDA 扫码枪，或手动输入', 'mobile/选工序、填本次数量', 'mobile/提交后怎么确认成功', 'mobile/现场异常怎么上报']
const quantity = ['faq/报工后生产报表为什么没增加', 'faq/完成数为什么是 80，不是 180', 'faq/工单找不到、结束后又变执行中', 'faq/超出计划数、剩余可报为零', 'pc/工价和工资报表', 'pc/建议报价（相似件 · 金蝶材料 + 车间实绩）']
const faq = chapters.filter(chapter => chapter.group === 'faq').map(chapter => chapter.id)
const salary = ['pc/工价和工资报表', 'pc/微信预警（企业微信推送，默认关闭）', 'pc/产品优势速览（演示给老板看）']
const permission = ['start/先看懂每天要做什么', 'pc/建部门、把人加入部门', 'faq/提示无报工权限', 'faq/产品或基础资料删不掉', 'pc/使用 AI 助手查问题', 'pc/产品优势速览（演示给老板看）']
const outputs = [
  ['SupportDocs/01-操作入门.md', '操作入门', ['start/先看懂每天要做什么', 'start/登录前准备什么', 'pc/登录与选厂（多账套）', 'start/第一次配置的顺序', ...basic.slice(0, 2)]],
  ['SupportDocs/02-基础资料.md', '基础资料', basic],
  ['SupportDocs/03-工单.md', '工单', orders],
  ['SupportDocs/04-报工与复核.md', '报工与复核', reporting],
  ['SupportDocs/05-常见问题.md', '常见问题', faq],
  ['SupportDocs/06-数据口径.md', '数据口径', quantity],
  ['SupportDocs/07-工资与预警.md', '工资与预警', salary],
  ['docs/ai-support/work-order.md', '工单怎么用', orders],
  ['docs/ai-support/reporting.md', '报工与复核', reporting],
  ['docs/ai-support/faq.md', '常见问题', faq],
  ['docs/ai-support/quantity.md', '数量与进度', quantity],
  ['docs/ai-support/permissions.md', '角色与权限', permission]
]
let stale = false
for (const [path, title, ids] of outputs) {
  const content = `---\ntitle: ${title}\nrole: 全员\nversion: 1.7\nsystemVersion: 2026-09-30\nupdatedAt: 2026-09-30\n---\n\n# ${title}\n\n> 与操作手册同源生成；维护 docs/30、31 和 docs/manual 正文后运行 npm run manual:sync。\n\n${ids.map(section).join('\n\n')}\n`
  if (process.argv.includes('--check')) {
    if (readFileSync(resolve(root, path), 'utf8') !== content) {
      console.error(`手册同步检查失败：${path}，请运行 npm run manual:sync`)
      stale = true
    }
  } else writeFileSync(resolve(root, path), content, 'utf8')
}
if (stale) process.exitCode = 1
else console.log(`手册与 AI 资料${process.argv.includes('--check') ? '一致' : '已同步'}：${outputs.length} 篇`)
