import {
  House, Tickets, Monitor, EditPen, CircleCheck, Warning, Box, Share, Operation,
  CircleClose, Collection, Coin, User, OfficeBuilding, Setting, ChatDotRound, Iphone,
  Connection, Histogram, Cpu, Picture, Key, Money, Grid, DataLine, PieChart,
  TrendCharts, DataBoard, Odometer, Document, Reading, QuestionFilled, Trophy, InfoFilled
} from '@element-plus/icons-vue'
import { canUse, FEATURE } from '@/utils/licenseTier'

const ICONS = {
  House, Tickets, Monitor, EditPen, CircleCheck, Warning, Box, Share, Operation,
  CircleClose, Collection, Coin, User, OfficeBuilding, Setting, ChatDotRound, Iphone,
  Connection, Histogram, Cpu, Picture, Key, Money, Grid, DataLine, PieChart,
  TrendCharts, DataBoard, Odometer, Document, Reading, QuestionFilled, Trophy, InfoFilled
}

export function menuIcon(name) {
  return ICONS[name] || Tickets
}

/** 一个都没收藏时的 4 个入口。看不到复核时，第三格换成生产报表。 */
export function defaultFavoriteMenus(role, tier) {
  const reviewOk = (role === 1 || role === 3) && canUse(tier, FEATURE.ReportReview)
  return [
    { menuKey: '/order', menuTitle: '工单', icon: 'Tickets' },
    { menuKey: '/report', menuTitle: '报工', icon: 'EditPen' },
    reviewOk
      ? { menuKey: '/review', menuTitle: '报工复核', icon: 'CircleCheck' }
      : { menuKey: '/stat', menuTitle: '生产报表', icon: 'DataLine' },
    { menuKey: '/execution-monitor', menuTitle: '执行监控', icon: 'Monitor' }
  ]
}
