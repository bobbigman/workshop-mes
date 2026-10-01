<template>
  <div class="onboard-page">
    <header class="onboard-header">
      <div>
        <span class="eyebrow">小蜜蜂报工 · 上手引导</span>
        <h1>新手入门</h1>
        <p class="header-desc">照着下面的步骤走一遍，管理员能把厂子配起来，工人能上机报工。</p>
      </div>
      <router-link :to="returnPath" class="return-link">{{ signedIn ? '返回系统' : '返回登录' }}</router-link>
    </header>

    <main>
      <section class="block" aria-label="管理员线">
        <h2 class="block-title">老板 / 管理员：从零把厂子配起来（6 步）</h2>
        <ol class="step-list">
          <li v-for="(step, i) in adminSteps" :key="'a' + i" class="step-card">
            <div class="step-num">{{ i + 1 }}</div>
            <div class="step-body">
              <h3>{{ step.title }}</h3>
              <p class="step-desc">{{ step.desc }}</p>
              <div v-if="step.links?.length" class="step-actions">
                <el-button
                  v-for="link in step.links"
                  :key="link.path"
                  type="primary"
                  link
                  @click="go(link.path)"
                >去操作：{{ link.label }}</el-button>
              </div>
              <p class="step-done">完成标志：{{ step.done }}</p>
            </div>
          </li>
        </ol>
      </section>

      <section class="block" aria-label="工人线">
        <h2 class="block-title">工人：一学就会（3 步）</h2>
        <ol class="step-list">
          <li v-for="(step, i) in workerSteps" :key="'w' + i" class="step-card">
            <div class="step-num">{{ i + 1 }}</div>
            <div class="step-body">
              <h3>{{ step.title }}</h3>
              <p class="step-desc">{{ step.desc }}</p>
              <div v-if="step.links?.length" class="step-actions">
                <el-button
                  v-for="link in step.links"
                  :key="link.path"
                  type="primary"
                  link
                  @click="go(link.path)"
                >去操作：{{ link.label }}</el-button>
              </div>
              <p class="step-done">完成标志：{{ step.done }}</p>
            </div>
          </li>
        </ol>
      </section>

      <p class="foot-tip">有问题？右上角「帮助」有完整操作手册；或找管理员。</p>
    </main>
  </div>
</template>

<script setup>
import { useRouter } from 'vue-router'

const router = useRouter()
const signedIn = !!localStorage.getItem('token')
const returnPath = signedIn
  ? (Number(localStorage.getItem('role')) === 2 ? '/h5/home' : '/order')
  : '/login'

const adminSteps = [
  {
    title: '环境准备',
    desc: '用电脑浏览器打开系统地址（就是内网 IP）。管理员管配置用电脑；工人报工用手机或 PDA。建议用 Chrome / Edge 浏览器。',
    done: '浏览器能打开登录页。'
  },
  {
    title: '建账号和部门',
    desc: '先给每个工人开个账号、分到部门。报工能不能做，看的是部门，不是看人。',
    links: [
      { path: '/user', label: '用户' },
      { path: '/dept', label: '部门' }
    ],
    done: '账号建好、人都进了部门。'
  },
  {
    title: '填基础资料',
    desc: '把「单位 → 工序 → 不良品项 → 工艺路线 → 产品 → 工价表」按顺序填一遍。先有单位才有产品，先有工序才能排路线。',
    links: [
      { path: '/unit', label: '单位' },
      { path: '/operation', label: '工序' },
      { path: '/defect', label: '不良品项' },
      { path: '/routing', label: '工艺路线' },
      { path: '/product', label: '产品' },
      { path: '/price-rule', label: '工价表' }
    ],
    done: '产品定义里有产品、工艺路线能选。'
  },
  {
    title: '建工单出码',
    desc: '进工单，选产品、填数量、定交期，保存后能打印出带二维码的工单。',
    links: [{ path: '/order', label: '工单' }],
    done: '工单出来、二维码能打印。'
  },
  {
    title: '扫码报工',
    desc: '工人用手机/PDA 扫工单二维码 → 选自己干的工序 → 填良品数、不良品数（有不良要选原因）→ 提交。班组长在报工复核把关。',
    links: [
      { path: '/report', label: '报工' },
      { path: '/review', label: '报工复核' }
    ],
    done: '工人在手机上把工序报上去了。'
  },
  {
    title: '看进度',
    desc: '车间看板、生产报表、执行监控，一眼看到每张单到哪道工序、不良率多少、工人今天挣多少。',
    links: [
      { path: '/board', label: '车间看板' },
      { path: '/stat', label: '生产报表' },
      { path: '/execution-monitor', label: '执行监控' },
      { path: '/salary', label: '工资报表' }
    ],
    done: '看板上能看到工单进度、报表有数。'
  }
]

const workerSteps = [
  {
    title: '扫码打开工单',
    desc: '手机或 PDA 扫工单上的二维码，工单就打开了。',
    links: [{ path: '/h5/scan', label: '扫码报工' }],
    done: '手机上能看到这张工单和要干的工序。'
  },
  {
    title: '报工',
    desc: '选自己干的工序，填良品数和不良品数（有不良要选原因），点提交。',
    links: [{ path: '/report', label: '报工' }],
    done: '显示「报工成功」。'
  },
  {
    title: '看产出/工资',
    desc: '下班看看今天报了多少、挣了多少（计件/计时工资自动算）。',
    links: [{ path: '/stat', label: '生产报表' }],
    done: '能查到自己的产量和工资。'
  }
]

function go(path) {
  router.push(path)
}
</script>

<style scoped>
.onboard-page {
  min-height: 100vh;
  background: var(--app-bg);
  color: var(--app-text-1);
  font-size: 16px;
}
.onboard-header {
  background: var(--app-card);
  border-bottom: 1px solid var(--app-border);
  padding: 22px max(24px, calc((100vw - 960px) / 2));
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 20px;
}
.eyebrow { font-size: 13px; color: var(--app-text-3); }
h1 { font-size: 26px; margin: 6px 0 8px; }
.header-desc { margin: 0; color: var(--app-text-2); font-size: 15px; line-height: 1.6; max-width: 560px; }
.return-link { color: var(--app-primary); white-space: nowrap; text-decoration: none; margin-top: 8px; }

main { max-width: 960px; margin: auto; padding: 28px 24px 40px; }
.block { margin-bottom: 32px; }
.block-title { font-size: 20px; margin: 0 0 16px; }

.step-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 12px; }
.step-card {
  display: flex;
  gap: 16px;
  background: var(--app-card);
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  padding: 18px 20px;
}
.step-num {
  flex: 0 0 36px;
  height: 36px;
  border-radius: 50%;
  background: var(--app-primary-light);
  color: var(--app-primary);
  font-weight: 700;
  font-size: 15px;
  display: flex;
  align-items: center;
  justify-content: center;
}
.step-body { flex: 1; min-width: 0; }
.step-body h3 { margin: 4px 0 8px; font-size: 17px; }
.step-desc { margin: 0 0 10px; color: var(--app-text-2); line-height: 1.7; }
.step-actions { display: flex; flex-wrap: wrap; gap: 4px 12px; margin-bottom: 8px; }
.step-done { margin: 0; font-size: 13px; color: var(--app-text-3); }

.foot-tip {
  margin: 8px 0 0;
  padding: 16px 18px;
  background: var(--app-card);
  border: 1px dashed var(--app-border);
  border-radius: var(--app-radius);
  color: var(--app-text-2);
  font-size: 14px;
  line-height: 1.6;
}

@media (max-width: 640px) {
  .onboard-header { padding: 18px 16px; flex-direction: column; }
  main { padding: 22px 16px 32px; }
  .step-card { padding: 14px 16px; gap: 12px; }
}
</style>
