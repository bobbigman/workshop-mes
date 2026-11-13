/**
 * 界面卖点提示条文案（对齐 docs/77 §二 / §三）
 * 改口径先改 docs/77 与定盘备忘录，再同步本文件。
 */
export const VALUE_TIPS = {
  order: '单做到哪了，打开就能看——不用下车间问、不用打电话追',
  report: '扫一下产量就记上，计件有据——月底少扯皮',
  review: '报工过一遍再进工资——错数少进账、少扯皮',
  abnormal: '现场卡住有单可跟——不用微信群里追',
  product: '产品一次定清，建单直接选用——不用每次重抄规格',
  operationPriceList: '计件单价按条件取齐——报工完工资有据可查',
  board: '订单到哪一步，大屏一眼见——不用下车间、不用打电话，坐办公室就能管进度',
  salary: '报工一结，计件部分算准、能查能重算——不用从表里抄一遍，少吵工资架',
  defectStat: '不良品报表看现场、生产报表算钱，两表数不同属正常（本页含待复核和已通过，不含退回）',
  wechatAlert: '快拖单了，手机先提醒您——不用等客户催，交期心里有数',
  reportSetting: '工人手机看什么、能不能串报、数量上限怎么算——三个开关同一页改，报工和复核一起生效',
  documentPermission: '想防别人改自己建的工单/价目表时打开；默认关，和现在一样；管理员始终能改全部',
  workerView: '工人手机只看自己的活，还是全车间都能报——管理员一键切，现场不乱',
  reportUpstream: '上道没过完就报下道？可软提示、可硬拦——管理员按工序开关，现场少乱序',
  reportQty: '计划允许超一点，上道不良算不算下道的量——管理员两个开关，报工和复核一起生效',
  quoteSuggest: '跟金蝶打通，料钱工钱有依据——不用翻旧账重录一遍',
  schedule: '跟金蝶打通，接单前就知道料够不够——不用另建一套表，不接烂单',
  aiAssistant: '车间常见问题，问一下有处可查——新人少干等老师傅',
  localDeployBanner: '本地部署・局域网运行・数据不出厂',
  localDeployDesc:
    '数据存在您自己的服务器上，局域网就能跑，断网也能正常使用',
  kingdeeSlogan: '支持对接金蝶，订单和物料数据自动带出'
}

/** 单据列表页挂页签行右侧；编辑页（billTab）不显示，省宽 */
const BILL_LIST_TIP_BY_PATH = {
  '/order': VALUE_TIPS.order,
  '/report': VALUE_TIPS.report,
  '/review': VALUE_TIPS.review,
  '/abnormal': VALUE_TIPS.abnormal,
  '/product': VALUE_TIPS.product,
  '/operation-price-list': VALUE_TIPS.operationPriceList,
  '/salary': VALUE_TIPS.salary
}

/**
 * @param {{ path?: string, meta?: Record<string, unknown> }} route
 * @returns {string}
 */
export function billListValueTip(route) {
  if (!route || route.meta?.billTab) return ''
  return BILL_LIST_TIP_BY_PATH[route.path] || ''
}
