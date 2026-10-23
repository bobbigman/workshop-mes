const fs = require('fs');
const p='frontend/src/views/h5/home.vue'; let s=fs.readFileSync(p,'utf8').replace(/\r\n/g,'\n');
const script=s.slice(s.indexOf('<script setup>'),s.indexOf('<style scoped>')).replace("import { computed, ref, onMounted }", "import { computed, ref, onMounted }").replace("import { ActionSheet as VanActionSheet } from 'vant'", "import { Icon as VanIcon } from 'vant'").replace("import { managementDestination } from '@/utils/reportManagement'\n",'');
let logic=script.replace(/const managementOpen = ref\(false\)[\s\S]*?(?=const todayGoodQty)/,'');
logic=logic.replace("const role = Number(localStorage.getItem('role') || 0)\n",'');
logic=logic.replace("const todayGoodQty = ref(0)",`const shortcuts = computed(() => [
  ...(viewReady.value && showOrdersEntry.value ? [{ label: '工单', icon: 'orders-o', tone: 'blue', to: '/h5/orders' }] : []),
  ...(canWage ? [{ label: '我的工资', icon: 'gold-coin-o', tone: 'green', to: '/h5/my-wage' }] : []),
  { label: '上报异常', icon: 'warning-o', tone: 'orange', to: '/h5/abnormal' },
  ...(viewReady.value && showOrdersEntry.value ? [
    { label: '生产报表', icon: 'bar-chart-o', tone: 'purple', to: { path: '/stat', query: { from: 'h5' } } },
    { label: '执行监控', icon: 'tv-o', tone: 'cyan', to: { path: '/execution-monitor', query: { from: 'h5' } } },
    { label: '车间看板', icon: 'chart-trending-o', tone: 'yellow', to: { path: '/board', query: { from: 'h5' } } }
  ] : []),
  { label: '帮助', icon: 'question-o', tone: 'pink', to: '/h5/help' },
  { label: '更多功能', icon: 'ellipsis', tone: 'slate', to: '/h5/more' }
])
const todayGoodQty = ref(0)`);
logic=logic.replace('工单入口配置','查询入口配置').replace('暂不显示工单入口','暂不显示全车间查询入口');
const template=`<template>
  <div class="h5-page">
    <header class="header">
      <FactoryBadge />
      <h2>工作台</h2>
      <span class="login-user" :title="userName">{{ userName }}</span>
      <H5LogoutBtn />
    </header>

    <button v-if="canScan" type="button" class="scan-btn" @click="router.push('/h5/scan')">
      <span class="scan-copy"><strong>扫码报工</strong><span>扫工单二维码，填写本次数量</span></span>
      <span class="scan-icon"><van-icon name="scan" aria-hidden="true" /></span>
    </button>

    <section v-if="canWage" class="today-summary" aria-label="今日计件">
      <div v-if="summaryLoading" class="summary-state" role="status">今日计件加载中…</div>
      <div v-else-if="summaryError" class="summary-state" role="alert">今日计件加载失败 <button type="button" @click="loadTodaySummary">重试</button></div>
      <template v-else>
        <div class="today-cards">
          <div class="today-card good"><span>今日良品报工累计</span><strong>{{ todayGoodQty }}</strong></div>
          <div class="today-card wage"><span>预计计件收入</span><strong>¥{{ formatMoney(todayWage) }}</strong></div>
        </div>
        <p class="today-tip">含已复核和待复核，以工资单为准</p>
      </template>
    </section>

    <section class="task-summary" aria-label="我的任务">
      <router-link class="task-entry" to="/h5/tasks">
        <span class="task-copy">
          <strong>我的任务</strong>
          <span v-if="tasksLoading" class="entry-note" role="status">任务加载中…</span>
          <span v-else-if="loadError" class="entry-note">任务数量暂不可用</span>
          <span v-else-if="!list.length" class="entry-note">暂无派给我或本班组的任务</span>
          <span v-else class="entry-note"><b>已派任务 {{ list.length }} 项</b>（含本班组）</span>
        </span>
        <span class="entry-arrow">{{ loadError ? '查看任务' : '查看全部' }} ›</span>
      </router-link>
      <p v-if="loadError" class="load-error" role="alert">任务加载失败 <button type="button" @click="load">重试</button></p>
    </section>

    <section class="block" aria-label="常用功能">
      <h3 class="block-title">常用功能</h3>
      <p v-if="viewError" class="load-error" role="alert">查询入口配置加载失败 <button type="button" @click="refreshViewMode">重试</button></p>
      <div class="shortcuts">
        <router-link v-for="item in shortcuts" :key="item.label" class="shortcut" :to="item.to">
          <span class="shortcut-icon" :class="item.tone"><van-icon :name="item.icon" aria-hidden="true" /></span>
          <span class="shortcut-label">{{ item.label }}</span>
        </router-link>
      </div>
    </section>

    <TabBar active="home" />
    <PwaGuide v-if="showPwaGuide" @dismiss-this-time="onPwaDismissThisTime" @dismiss-forever="onPwaDismissForever" />
  </div>
</template>

`;
const css=`<style scoped>
.h5-page { padding: 0 16px calc(var(--h5-tabbar-height) + 16px + env(safe-area-inset-bottom)); min-height: 100vh; background: #f5f6fa; box-sizing: border-box; color: var(--app-text-1); }
.header { display: flex; align-items: center; gap: 8px; min-height: 52px; margin: 0 -16px 16px; padding: 0 16px; background: var(--app-card); }
.header :deep(.factory-badge) { flex: 1; min-width: 0; max-width: 150px; padding: 0; background: transparent; border-radius: 0; font-size: 11px; font-weight: 400; line-height: 1.4; }
h2 { margin: 0; flex-shrink: 0; font-size: 17px; }
.login-user { margin-left: auto; max-width: 56px; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 12px; color: var(--app-text-2); }
.header :deep(.h5-logout) { margin-left: 0; flex-shrink: 0; font-size: 12px; min-height: 44px; padding: 6px 0; }
.scan-btn { display: flex; justify-content: space-between; align-items: center; gap: 8px; width: 100%; min-height: 104px; padding: 18px; border: 0; border-radius: 16px; background: linear-gradient(110deg, #0968ff, #3e98ff); color: #fff; text-align: left; cursor: pointer; margin-bottom: 12px; }
.scan-copy { min-width: 0; display: flex; flex-direction: column; gap: 6px; }
.scan-copy strong { font-size: 23px; line-height: 1.2; }
.scan-copy > span { font-size: 12px; line-height: 1.5; }
.scan-icon { flex-shrink: 0; display: grid; place-items: center; width: 52px; height: 52px; border-radius: 15px; background: #f1f8ff; color: #1677ff; font-size: 30px; }
.today-summary { margin-bottom: 16px; }
.today-cards { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
.today-card { min-width: 0; padding: 14px 8px; border-radius: 14px; text-align: center; }
.today-card > span { display: block; font-size: 12px; line-height: 1.5; color: #65758b; }
.today-card strong { display: block; margin-top: 5px; font-size: 27px; line-height: 1.2; overflow-wrap: anywhere; }
.good { background: #edf3ff; color: #1677ff; }
.wage { background: #e7f6ee; color: #12975f; }
.today-tip { margin: 8px 0 0; font-size: 11px; line-height: 1.5; color: #7f899a; }
.summary-state { padding: 18px 12px; border-radius: 14px; background: #fff; font-size: 14px; }
.task-summary { background: var(--app-card); border: 1px solid #e9edf3; border-radius: 14px; margin-bottom: 18px; }
.task-entry { display: flex; align-items: center; justify-content: space-between; gap: 8px; min-height: 52px; padding: 8px 12px; box-sizing: border-box; color: var(--app-text-1); text-decoration: none; }
.task-copy { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; min-width: 0; font-size: 12px; overflow-wrap: anywhere; }
.task-copy strong { font-size: 13px; font-weight: 500; }
.entry-note { color: var(--app-text-2); }
.entry-note b { color: var(--app-primary); font-weight: 600; }
.entry-arrow { flex-shrink: 0; color: var(--app-primary); font-size: 12px; }
.load-error { margin: 0; padding: 0 12px 12px; font-size: 13px; }
.block-title { margin: 0 0 10px; font-size: 16px; font-weight: 700; }
.shortcuts { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px; }
.shortcut { display: flex; flex-direction: column; align-items: center; justify-content: flex-start; gap: 8px; min-width: 0; min-height: 102px; padding: 12px 3px 10px; box-sizing: border-box; border: 1px solid #e9edf3; border-radius: 14px; background: var(--app-card); color: var(--app-text-1); text-align: center; text-decoration: none; }
.shortcut-icon { display: grid; place-items: center; width: 48px; height: 48px; border-radius: 14px; font-size: 26px; }
.shortcut-label { font-size: 13px; line-height: 1.4; overflow-wrap: anywhere; }
.blue { background: #edf3ff; color: #1677ff; }
.green { background: #e7f6ee; color: #109961; }
.orange { background: #fff3e7; color: #ed8a00; }
.purple { background: #f2edff; color: #7a61e8; }
.cyan { background: #e4f6fa; color: #0896ad; }
.yellow { background: #fff9e4; color: #d5a000; }
.pink { background: #fceef4; color: #e65399; }
.slate { background: #edf1f7; color: #8190a8; }
.scan-btn:active, .shortcut:active { opacity: .8; }
@media (max-width: 350px) { .header { gap: 5px; } .header :deep(.factory-badge) { font-size: 10px; } .login-user { max-width: 36px; } .shortcuts { gap: 8px; } .shortcut-icon { width: 44px; height: 44px; } .shortcut-label { font-size: 13px; } .scan-btn { padding: 16px; } }
</style>
`;
fs.writeFileSync(p,template+logic+css);
const mp='frontend/src/views/h5/more.vue'; let m=fs.readFileSync(mp,'utf8').replace(/\r\n/g,'\n');
m=m.replace('    </van-cell-group>', '      <van-cell v-if="managementActions.length" title="管理操作" label="代报工、补报与管理后台（按角色）" is-link @click="managementOpen = true" />\n    </van-cell-group>\n    <van-action-sheet v-model:show="managementOpen" title="管理操作" :actions="managementActions" cancel-text="取消" close-on-click-action @select="openManagement" />');
m=m.replace("import { ref, onMounted }", "import { computed, ref, onMounted }").replace("Button as VanButton }", "Button as VanButton, ActionSheet as VanActionSheet }");
m=m.replace("import { getWorkerView }", "import { useRouter } from 'vue-router'\nimport { managementDestination, canOpenReportManagement } from '@/utils/reportManagement'\nimport { getWorkerView }");
const management=fs.readFileSync('.tmp-build/278/home.vue','utf8').replace(/\r\n/g,'\n').match(/const managementOpen = ref\(false\)[\s\S]*?(?=const todayGoodQty)/)[0];
m=m.replace('const loading =', "const router = useRouter()\nconst role = Number(localStorage.getItem('role') || 0)\n"+management.replace("if (!managementActions.value.some(option => option.action === item.action)) return", "if (!managementActions.value.some(option => option.action === item.action)) return\n  if (item.action !== 'backend' && !canOpenReportManagement(item.action, role)) return")+'const loading =');
m=m.replace('v-if="tasksOnly" class="help-note"','v-if="!loading && !error && tasksOnly" class="help-note"');
fs.writeFileSync(mp,m);