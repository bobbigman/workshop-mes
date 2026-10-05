import { createRouter, createWebHashHistory } from 'vue-router'
import { canUse, FEATURE, currentTier } from '@/utils/licenseTier'

const routes = [
  { path: '/help', name: 'Help', component: () => import('@/views/help/index.vue'), meta: { title: '操作手册' } },
  { path: '/advantages', name: 'Advantages', component: () => import('@/views/advantages/index.vue'), meta: { title: '产品优势速览' } },
  { path: '/onboarding', name: 'Onboarding', component: () => import('@/views/onboarding/index.vue'), meta: { title: '新手入门' } },
  { path: '/login', name: 'Login', component: () => import('@/views/login/index.vue') },
  {
    path: '/',
    component: () => import('@/views/layout/index.vue'),
    redirect: '/order',
    children: [
      { path: 'order', name: 'Order', component: () => import('@/views/order/index.vue'), meta: { title: '工单' } },
      { path: 'execution-monitor', name: 'ExecutionMonitor', component: () => import('@/views/execution-monitor/index.vue'), meta: { title: '执行监控' } },
      { path: 'report', name: 'Report', component: () => import('@/views/report/index.vue'), meta: { title: '报工' } },
      { path: 'review', name: 'Review', component: () => import('@/views/review/index.vue'), meta: { title: '报工复核', feature: FEATURE.ReportReview } },
      { path: 'abnormal', name: 'Abnormal', component: () => import('@/views/abnormal/index.vue'), meta: { title: '异常处理', leaderOk: true } },
      { path: 'product', name: 'Product', component: () => import('@/views/product/index.vue'), meta: { title: '产品定义', adminOnly: true } },
      { path: 'routing', name: 'Routing', component: () => import('@/views/routing/index.vue'), meta: { title: '工艺路线', adminOnly: true } },
      { path: 'operation', name: 'Operation', component: () => import('@/views/operation/index.vue'), meta: { title: '工序', adminOnly: true } },
      { path: 'defect', name: 'Defect', component: () => import('@/views/defect/index.vue'), meta: { title: '不良品项', adminOnly: true } },
      { path: 'unit', name: 'Unit', component: () => import('@/views/unit/index.vue'), meta: { title: '单位', adminOnly: true } },
      { path: 'price-rule', name: 'PriceRule', component: () => import('@/views/price-rule/index.vue'), meta: { title: '工价表', adminOnly: true, feature: FEATURE.PieceWage } },
      { path: 'user', name: 'User', component: () => import('@/views/user/index.vue'), meta: { title: '用户', adminOnly: true } },
      { path: 'dept', name: 'Dept', component: () => import('@/views/dept/index.vue'), meta: { title: '部门', adminOnly: true } },
      { path: 'custom-field', name: 'CustomField', component: () => import('@/views/custom-field/index.vue'), meta: { title: '自定义字段', adminOnly: true } },
      { path: 'wechat-alert', name: 'WechatAlert', component: () => import('@/views/wechat-alert/index.vue'), meta: { title: '微信预警', adminOnly: true, feature: FEATURE.WechatDueAlert } },
      { path: 'worker-view', name: 'WorkerView', component: () => import('@/views/worker-view/index.vue'), meta: { title: '工人手机视角', adminOnly: true } },
      { path: 'stat', name: 'Stat', component: () => import('@/views/stat/index.vue'), meta: { title: '生产报表' } },
      { path: 'defect-stat', name: 'DefectStat', component: () => import('@/views/defect-stat/index.vue'), meta: { title: '不良品报表' } },
      { path: 'sku-stat', name: 'SkuStat', component: () => import('@/views/sku-stat/index.vue'), meta: { title: '色码汇总' } },
      { path: 'salary', name: 'Salary', component: () => import('@/views/salary/index.vue'), meta: { title: '工资报表', adminOnly: true, feature: FEATURE.PieceWage } },
      { path: 'salary-sku', name: 'SalarySku', component: () => import('@/views/salary-sku/index.vue'), meta: { title: '工资色码汇总', adminOnly: true, feature: FEATURE.PieceWage } },
      { path: 'quote-suggest', name: 'QuoteSuggest', component: () => import('@/views/quote-suggest/index.vue'), meta: { title: '建议报价', adminOnly: true, feature: FEATURE.KingdeeMcp } },
      { path: 'schedule', name: 'Schedule', component: () => import('@/views/schedule/index.vue'), meta: { title: '排产评分', adminOnly: true } },
      { path: 'ai-assistant', name: 'AiAssistant', component: () => import('@/views/ai-assistant/index.vue'), meta: { title: 'AI 助手', adminOnly: true } },
      { path: 'login-setting', name: 'LoginSetting', component: () => import('@/views/login-setting/index.vue'), meta: { title: '登录页图片', adminOnly: true } },
      { path: 'mcp-key', name: 'McpKey', component: () => import('@/views/mcp-key/index.vue'), meta: { title: 'MCP 钥匙', adminOnly: true } },
      { path: 'about', name: 'About', component: () => import('@/views/about/index.vue'), meta: { title: '关于' } },
      { path: 'faq', name: 'Faq', component: () => import('@/views/faq/index.vue'), meta: { title: '常见问题（系统能力50问）' } },
    ]
  },
  { path: '/h5/home', name: 'H5Home', component: () => import('@/views/h5/home.vue'), meta: { title: '工作台' } },
  { path: '/h5/orders', name: 'H5Orders', component: () => import('@/views/h5/orders.vue'), meta: { title: '工单' } },
  { path: '/h5/tasks', name: 'H5Tasks', component: () => import('@/views/h5/tasks.vue'), meta: { title: '我的任务' } },
  { path: '/h5/scan', name: 'H5Scan', component: () => import('@/views/h5/scan.vue'), meta: { title: '扫码报工', feature: FEATURE.ScanReport } },
  { path: '/h5/report', name: 'H5Report', component: () => import('@/views/h5/report.vue'), meta: { title: '报工' } },
  { path: '/h5/my-reports', name: 'H5MyReports', component: () => import('@/views/h5/my-reports.vue'), meta: { title: '我的报工' } },
  { path: '/h5/my-wage', name: 'H5MyWage', component: () => import('@/views/h5/my-wage.vue'), meta: { title: '我的工资', feature: FEATURE.PieceWage } },
  { path: '/h5/abnormal', name: 'H5Abnormal', component: () => import('@/views/h5/abnormal.vue'), meta: { title: '上报异常' } },
  { path: '/h5/review', name: 'H5Review', component: () => import('@/views/h5/review.vue'), meta: { title: '报工复核', feature: FEATURE.ReportReview } },
  { path: '/board', name: 'Board', component: () => import('@/views/board/index.vue'), meta: { title: '车间看板' } },
]

const router = createRouter({
  history: createWebHashHistory(),
  routes
})

function isSafeH5Redirect(path) {
  return typeof path === 'string' && path.startsWith('/h5/') && !path.startsWith('//')
}

router.beforeEach((to, from, next) => {
  // 帮助 / 优势速览：随版本发布的静态页，不查业务数据；登录前也可打开。
  if (to.path === '/login' || to.path === '/help' || to.path === '/advantages') return next()
  const token = localStorage.getItem('token')
  if (!token) {
    // 未登录打开 H5 报工链接时保留站内目标，登录后回跳（仅 /h5/*）
    if (isSafeH5Redirect(to.fullPath)) {
      return next({ path: '/login', query: { redirect: to.fullPath } })
    }
    return next('/login')
  }

  const role = Number(localStorage.getItem('role') || 0)
  // 基础数据 / 系统配置 / 工资 / 排产 / AI：仅管理员（对齐 SideMenu）
  if (to.meta?.adminOnly && role !== 1) {
    return next(role === 3 ? '/review' : '/order')
  }
  // 班组长可进复核/异常处理
  if (to.meta?.leaderOk && role !== 1 && role !== 3) {
    return next('/order')
  }
  // 生产人员：电脑端仅工单、报工、生产报表、看板 + H5
  const h5Only = to.path.startsWith('/h5')
  const workerQuery = to.path === '/order' || to.path === '/execution-monitor' || to.path === '/report' || to.path === '/stat' || to.path === '/defect-stat' || to.path === '/sku-stat' || to.path === '/board' || to.path === '/about' || to.path === '/faq' || to.path === '/help' || to.path === '/advantages' || to.path === '/onboarding'
  if (role === 2 && !h5Only && !workerQuery) {
    return next('/order')
  }
  // 授权档：与 role 正交；无档功能回退（H5 扫码 → 任务页）
  if (to.meta?.feature && !canUse(currentTier(), to.meta.feature)) {
    return next(to.path.startsWith('/h5') ? '/h5/tasks' : '/order')
  }
  next()
})

export default router
