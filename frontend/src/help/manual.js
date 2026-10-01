import start from '../../../docs/manual/第一次使用.md?raw'
import pc from '../../../docs/30-使用手册-管理员（电脑端）.md?raw'
import mobile from '../../../docs/31-使用手册-工人（手机端）.md?raw'
import faq from '../../../docs/manual/遇到问题.md?raw'
import { parseChapters } from './parser.js'

export const manualVersion = '1.6'
export const manualUpdatedAt = '2026-09-27'
export const groups = [
  { id: 'start', title: '第一次使用', description: '从准备资料到跑通第一张单', source: start },
  { id: 'pc', title: '电脑日常操作', description: '下单、打印、复核、改数和补报', source: pc },
  { id: 'mobile', title: '手机/PDA 报工', description: '找工单、扫码、填数、确认结果', source: mobile },
  { id: 'faq', title: '遇到问题', description: '权限、数量、扫码和错误处理', source: faq }
]
export const chapters = groups.flatMap(group => parseChapters(group.source, group.id))
