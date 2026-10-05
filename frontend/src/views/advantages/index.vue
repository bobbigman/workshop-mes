<template>
  <div class="adv-page">
    <header class="adv-header">
      <div>
        <span class="eyebrow">微聚 · 产品优势速览</span>
        <h1>三个麻烦，一次说清</h1>
        <p class="header-desc">先看三个麻烦怎么解，再看和别人差在哪——本地部署、断外网也能用。</p>
      </div>
      <div class="header-actions">
        <el-button @click="openNewWindow">新窗口打开</el-button>
        <router-link :to="returnPath" class="return-link">{{ signedIn ? '返回系统' : '返回登录' }}</router-link>
      </div>
    </header>

    <div class="deploy-banner" role="note">{{ VALUE_TIPS.localDeployBanner }}</div>

    <main>
      <!-- A 区：客户版 -->
      <section class="block" aria-label="三大痛点">
        <h2 class="block-title">先看您现在的三个麻烦</h2>
        <div class="pain-grid">
          <article v-for="item in pains" :key="item.title" class="pain-card">
            <div class="icon-wrap" :class="item.tone">
              <el-icon :size="40"><component :is="item.icon" /></el-icon>
            </div>
            <div>
              <h3>{{ item.title }}</h3>
              <p class="solve"><span class="solve-label">我们怎么解</span>{{ item.solve }}</p>
            </div>
          </article>
        </div>
      </section>

      <section class="block" aria-label="六步闭环">
        <h2 class="block-title">六步闭环：从下单到提醒，一路通</h2>
        <ol class="flow">
          <li v-for="(step, i) in flowSteps" :key="step">
            <span class="flow-num">{{ i + 1 }}</span>
            <span class="flow-text">{{ step }}</span>
            <el-icon v-if="i < flowSteps.length - 1" class="flow-arrow" :size="18"><ArrowRight /></el-icon>
          </li>
        </ol>
      </section>

      <section class="block" aria-label="本次新增">
        <h2 class="block-title">本次新增（2026-10-02）</h2>
        <ul class="ext-list">
          <li v-for="item in newAdds" :key="item.label">
            <strong>{{ item.label }}</strong>
            <span>{{ item.text }}</span>
          </li>
        </ul>
      </section>

      <section class="block" aria-label="三大差异">
        <h2 class="block-title">和别人比，您最该记住这三点</h2>
        <div class="diff-grid">
          <article v-for="item in diffs" :key="item.title" class="diff-card">
            <div class="icon-wrap tone-green">
              <el-icon :size="40"><component :is="item.icon" /></el-icon>
            </div>
            <h3>{{ item.title }}</h3>
            <p>{{ item.desc }}</p>
          </article>
        </div>
      </section>

      <section class="block" aria-label="深度集成">
        <h2 class="block-title">能和金蝶打通，数据不重复录</h2>
        <p class="ext-lead">下面几条已能演示（有金蝶环境更贴）。</p>
        <ul class="ext-list">
          <li v-for="item in extensions" :key="item.label">
            <strong>{{ item.label }}</strong>
            <span>{{ item.text }}</span>
          </li>
        </ul>
      </section>

      <!-- B 区：销售版 -->
      <section class="block sales-block" aria-label="销售备忘">
        <button type="button" class="sales-toggle" :aria-expanded="salesOpen" @click="salesOpen = !salesOpen">
          <span>{{ salesOpen ? '收起销售备忘' : '展开销售备忘（演示时请收起）' }}</span>
          <el-icon :size="16"><component :is="salesOpen ? ArrowUp : ArrowDown" /></el-icon>
        </button>

        <div v-show="salesOpen" class="sales-body">
          <div class="sales-section">
            <h3>演示路径（照着点）</h3>
            <ol class="demo-links">
              <li v-for="item in demoLinks" :key="item.path">
                <a :href="'#' + item.path" target="_blank" rel="noopener">{{ item.label }}</a>
                <span class="demo-hint">{{ item.hint }}</span>
              </li>
            </ol>
          </div>

          <div class="sales-section">
            <h3>话术卡 · 三句杀招</h3>
            <ol class="talk-list">
              <li v-for="(t, i) in talkLines" :key="i"><strong>{{ t.tag }}</strong>{{ t.text }}</li>
            </ol>
            <p class="case-line"><strong>案例话术：</strong>{{ caseLine }}</p>
          </div>

          <div class="sales-section">
            <h3>差异化小点 · 上手不蒙圈（演示彩蛋）</h3>
            <p class="sales-para">{{ helpSellBody }}</p>
            <p class="case-line"><strong>杀招：</strong>{{ helpSellHook }}</p>
            <p class="sales-note">{{ helpSellBoundary }}</p>
          </div>

          <div class="sales-section">
            <h3>对比备答（老板追问时用）</h3>
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>维度</th>
                    <th>我们</th>
                    <th>黑湖小工单</th>
                    <th>简道云</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="row in compareRows" :key="row.dim">
                    <td>{{ row.dim }}</td>
                    <td>{{ row.us }}</td>
                    <td>{{ row.heihu }}</td>
                    <td>{{ row.jiandaoyun }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p class="sales-note">红线：不承诺钉钉/短信；不承诺自动定时推送哪天上线；数据不出厂、按厂收费仍是主词。A 区不点名竞品。</p>
          </div>
        </div>
      </section>
    </main>

    <footer class="adv-footer">
      <p class="credit-line">{{ creditLine }}</p>
      <p v-if="creditRoles" class="credit-roles">{{ creditRoles }}</p>
      <p>给老板看上面 · 销售备忘默认收着 · 口径对齐推广定盘</p>
    </footer>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import {
  Timer, Coin, View, Lock, Setting, OfficeBuilding,
  ArrowRight, ArrowDown, ArrowUp
} from '@element-plus/icons-vue'
import { VALUE_TIPS } from '@/constants/valueTips'
import { getInstanceInfo } from '@/api/auth'
import { creditLine, creditRoles } from '@/utils/instanceDisplay'

const signedIn = !!localStorage.getItem('token')
const returnPath = signedIn
  ? (Number(localStorage.getItem('role')) === 2 ? '/h5/home' : '/order')
  : '/login'
const salesOpen = ref(false)

onMounted(async () => {
  try {
    await getInstanceInfo()
  } catch {
    // 回退胡工单方版
  }
})

const pains = [
  {
    icon: Timer,
    tone: 'tone-warn',
    title: '快到交期才发现要拖',
    solve: '看板黄红预警，企业微信推到手机。'
  },
  {
    icon: Coin,
    tone: 'tone-blue',
    title: '月底算工资拿表对、还老吵',
    solve: '工价配一次，计件部分自动算，每笔可查可重算。'
  },
  {
    icon: View,
    tone: 'tone-blue',
    title: '想知道单做到哪了，得下车间问',
    solve: '扫码报工，看板实时，坐办公室管全厂。'
  }
]

const flowSteps = [
  '下单出二维码',
  '扫码报工',
  '看板实时',
  '工资自动算',
  '交期自动提醒',
  '派工 / 异常上报'
]

const newAdds = [
  { label: '手机端视角（200）', text: '工厂级开关，默认全车间可见可报；可选"只看我的任务"。' },
  { label: '班组长手机派工（201）', text: '移动端派工 + 推送，班组长限本组。' },
  { label: '多人派工 + 两种分摊（202）', text: '平均 / 按工时两种拆分计件。' },
  { label: '手机撤回改 + 留痕（205）', text: '未审核报工手机撤改，修改留痕可追。' },
  { label: '工人今日计件（206）', text: '手机看当日产量与工资，即时激励。' },
  { label: '不良品三表（207，在做）', text: '分布 / 汇总 / 明细；口径含待复核+已通过、不含退回。' },
  { label: '工资批量重算（134）', text: '多人一次重算，不用逐个点。' },
  { label: '只算计件（135）', text: '工资单拿掉手工发薪项，保底/加班/考勤归人事。' }
]

const diffs = [
  {
    icon: Lock,
    title: '本地部署，断外网也能用',
    desc: VALUE_TIPS.localDeployDesc
  },
  {
    icon: Setting,
    title: '工资能按您厂规矩算',
    desc: '计件绩效算清楚：复核前置，可追溯可重算。底薪社保仍走财务。'
  },
  {
    icon: OfficeBuilding,
    title: '按厂收费，不按人头',
    desc: '轻量，局域网就能跑；不是按人头年费把人卡死。'
  }
]

const extensions = [
  { label: '建议报价', text: VALUE_TIPS.quoteSuggest },
  { label: '排产评分', text: VALUE_TIPS.schedule },
  { label: '金蝶集成', text: VALUE_TIPS.kingdeeSlogan }
]

const demoLinks = [
  { path: '/order', label: '① 下单 / 出二维码', hint: '工单 → 创建 → 打印二维码' },
  { path: '/h5/scan', label: '② 扫码报工', hint: '手机报工页' },
  { path: '/board', label: '③ 看板', hint: '进度跳、黄红预警' },
  { path: '/salary', label: '④ 工资', hint: '工资报表 · 明细可查' },
  { path: '/wechat-alert', label: '⑤ 交期提醒', hint: '微信预警 · 群机器人即可' },
  { path: '/quote-suggest', label: '⑥ 建议报价', hint: '金蝶取数 · 有依据' },
  { path: '/schedule', label: '⑥ 排产评分', hint: '齐套 · 不接烂单' },
  { path: '/execution-monitor', label: '⑦ 派工', hint: '执行监控' },
  { path: '/abnormal', label: '⑦ 异常', hint: '异常处理' }
]

const talkLines = [
  {
    tag: '戳痛：',
    text: '月底算工资是不是还得拿表对？想知道单做到哪了，是不是得下车间问？快到交期是不是到跟前才发现要拖？'
  },
  {
    tag: '亮底：',
    text: '大软件管大厂，您这规模花大钱办小事。这套照着您厂子的活法做：扫码报工、看板看进度、工资自动算、交期自动提醒。'
  },
  {
    tag: '差异：',
    text: '别人数据在云上、按人头年费、一定要外网。我这套装您自己电脑里，断外网也能用，数据不出厂，推送照发，不泄露。'
  }
]

const caseLine = '某张单离交期还有 2 天、还卡在第三道工序，您手机上就收到了——不用等您去问，系统先提醒您。'

const helpSellBody =
  '别人给一整本操作手册，工人遇到按钮不懂要自己翻；我们反过来——每个页面右上角都有「？」，按一下 F1，就地弹出这一页每个按钮是干嘛的、谁能用、下一步去哪，不用翻手册。报工看到「待复核」不知道怎么处理，按 F1 就告诉你去「报工复核」页点【通过】。'
const helpSellHook =
  '黑湖给你一本手册，我们给你每个页面自己解释自己——按 F1，当场告诉你这一步去哪。'
const helpSellBoundary =
  '演示边界：PC 各业务页 + 车间看板按「？」/ F1 均有本页说明（docs/107 已落地）；H5 手机端无 F1（已有帮助 tab）。演示时打开任意 PC 业务页按 F1 即可。'

const compareRows = [
  { dim: '部署', us: '您服务器 / 局域网 / 断外网也能用', heihu: '云端（一定要外网）', jiandaoyun: '云端搭表' },
  { dim: '收费', us: '按厂，不按人头', heihu: '专业版约 1.08 万/年', jiandaoyun: '免费版起' },
  { dim: '工资', us: '计件可定制 + 可重算', heihu: '标准口径偏僵', jiandaoyun: '要自己搭' },
  { dim: '试用', us: '低门槛试用', heihu: '7 天免费 + 上门演示', jiandaoyun: '免费版' },
  { dim: '上手', us: '每页按 F1 就地看按钮说明', heihu: '整本手册自己翻', jiandaoyun: '要自己搭' }
]

function openNewWindow() {
  const url = `${window.location.origin}${window.location.pathname}#/advantages`
  window.open(url, '_blank', 'noopener,noreferrer')
}
</script>

<style scoped>
.adv-page {
  min-height: 100vh;
  background: var(--app-bg);
  color: var(--app-text-1);
  font-size: 16px;
}
.adv-header {
  background: var(--app-card);
  border-bottom: 1px solid var(--app-border);
  padding: 18px max(20px, calc((100vw - 1100px) / 2));
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
}
.eyebrow { font-size: 13px; color: var(--app-text-3); }
h1 { font-size: 24px; margin: 4px 0 0; line-height: 1.3; }
.header-desc {
  margin: 6px 0 0;
  font-size: 14px;
  line-height: 1.5;
  color: var(--app-text-2);
  max-width: 36em;
}
.header-actions { display: flex; align-items: center; gap: 14px; flex-shrink: 0; }
.return-link { color: var(--app-primary); white-space: nowrap; text-decoration: none; font-size: 14px; }

.deploy-banner {
  text-align: center;
  padding: 10px 16px;
  background: var(--app-primary);
  color: #fff;
  font-size: 14px;
  font-weight: 700;
  letter-spacing: 0.04em;
}

main {
  max-width: 1100px;
  margin: auto;
  padding: 20px 20px 28px;
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.block-title {
  font-size: 17px;
  margin: 0 0 12px;
  color: var(--app-primary);
}

.pain-grid,
.diff-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
}
.pain-card,
.diff-card {
  background: var(--app-card);
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  padding: 16px;
  display: flex;
  gap: 12px;
  align-items: flex-start;
}
.diff-card { flex-direction: column; }
.pain-card h3,
.diff-card h3 {
  margin: 0 0 6px;
  font-size: 16px;
  line-height: 1.35;
}
.pain-card p,
.diff-card p {
  margin: 0;
  color: var(--app-text-2);
  font-size: 14px;
  line-height: 1.55;
}
.solve-label {
  display: inline-block;
  margin-right: 6px;
  padding: 1px 6px;
  border-radius: var(--app-radius-sm);
  background: var(--app-primary-light);
  color: var(--app-primary);
  font-size: 12px;
  font-weight: 600;
}

.icon-wrap {
  width: 52px;
  height: 52px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.tone-blue { background: var(--app-primary-light); color: var(--app-primary); }
.tone-warn { background: #fff4e5; color: #c47a12; }
.tone-green { background: #e8f6ee; color: #1a7a45; }

.flow {
  list-style: none;
  margin: 0;
  padding: 14px 12px;
  background: var(--app-card);
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 4px;
}
.flow li {
  display: flex;
  align-items: center;
  gap: 6px;
}
.flow-num {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  background: var(--app-primary);
  color: #fff;
  font-size: 12px;
  font-weight: 700;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}
.flow-text { font-size: 14px; font-weight: 600; color: var(--app-text-1); white-space: nowrap; }
.flow-arrow { color: var(--app-text-4); }

.ext-lead {
  margin: 0 0 10px;
  font-size: 14px;
  color: var(--app-text-2);
  line-height: 1.55;
}
.ext-list {
  margin: 0;
  padding: 14px 16px 14px 28px;
  background: var(--app-card);
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  list-style: disc;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.ext-list li {
  font-size: 14px;
  line-height: 1.55;
  color: var(--app-text-2);
}
.ext-list strong {
  display: inline-block;
  min-width: 4.5em;
  margin-right: 8px;
  color: var(--app-primary);
}

.sales-block {
  border-top: 1px dashed var(--app-border);
  padding-top: 8px;
}
.sales-toggle {
  width: 100%;
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 14px;
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  background: var(--app-card);
  color: var(--app-text-2);
  font-size: 14px;
  cursor: pointer;
}
.sales-toggle:hover { border-color: var(--app-primary); background: var(--app-primary-light); }
.sales-body {
  margin-top: 12px;
  background: var(--app-card);
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  padding: 18px 20px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}
.sales-section h3 {
  margin: 0 0 10px;
  font-size: 15px;
  color: var(--app-primary);
}
.demo-links {
  margin: 0;
  padding-left: 20px;
  line-height: 1.8;
  font-size: 14px;
}
.demo-links a { color: var(--app-primary); font-weight: 600; }
.demo-hint { margin-left: 8px; color: var(--app-text-3); font-size: 13px; }
.talk-list {
  margin: 0;
  padding-left: 20px;
  line-height: 1.7;
  font-size: 14px;
  color: var(--app-text-2);
}
.talk-list strong { color: var(--app-primary); margin-right: 4px; }
.case-line {
  margin: 12px 0 0;
  padding: 10px 12px;
  background: var(--app-primary-light);
  border-radius: var(--app-radius-sm);
  font-size: 14px;
  line-height: 1.6;
  color: var(--app-text-2);
}
.table-wrap { overflow-x: auto; }
table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}
th, td {
  border: 1px solid var(--app-border);
  padding: 8px 10px;
  text-align: left;
  vertical-align: top;
}
th { background: var(--app-border-light); color: var(--app-primary); font-weight: 600; }
td:nth-child(2) { background: #f3faf5; font-weight: 600; color: #1a7a45; }
.sales-para {
  margin: 0;
  font-size: 14px;
  line-height: 1.7;
  color: var(--app-text-2);
}
.sales-note {
  margin: 10px 0 0;
  font-size: 12px;
  color: #8a5a12;
}

.adv-footer {
  text-align: center;
  color: var(--app-text-3);
  padding: 8px 20px 24px;
  font-size: 12px;
  line-height: 1.6;
}
.adv-footer .credit-line { margin: 0; font-size: 13px; }
.adv-footer .credit-roles { margin: 4px 0 8px; }
.adv-footer > p:last-child { margin: 0; }

@media (max-width: 860px) {
  .pain-grid,
  .diff-grid { grid-template-columns: 1fr; }
  .adv-header { flex-wrap: wrap; padding: 14px 16px; }
  h1 { font-size: 20px; }
  main { padding: 16px; }
  .flow-text { white-space: normal; }
}
</style>
