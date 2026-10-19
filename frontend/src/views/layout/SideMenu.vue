<template>
  <el-menu
    :default-active="active"
    :default-openeds="openedGroups"
    @select="onSelect"
    @open="onOpen"
    @close="onClose"
  >
    <el-menu-item index="/home">
      <div class="menu-row">
        <span class="menu-label"><el-icon><House /></el-icon>首页</span>
        <MenuStar menu-key="/home" />
      </div>
    </el-menu-item>
    <el-sub-menu index="production">
      <template #title>生产管理</template>
      <el-menu-item index="/order">
        <div class="menu-row">
          <span class="menu-label"><el-icon><Tickets /></el-icon>工单</span>
          <MenuStar menu-key="/order" />
        </div>
      </el-menu-item>
      <el-menu-item index="/execution-monitor">
        <div class="menu-row">
          <span class="menu-label"><el-icon><Monitor /></el-icon>执行监控</span>
          <MenuStar menu-key="/execution-monitor" />
        </div>
      </el-menu-item>
      <el-menu-item index="/report">
        <div class="menu-row">
          <span class="menu-label"><el-icon><EditPen /></el-icon>报工</span>
          <MenuStar menu-key="/report" />
        </div>
      </el-menu-item>
      <el-menu-item v-if="(role === 1 || role === 3) && canReview" index="/review">
        <div class="menu-row">
          <span class="menu-label"><el-icon><CircleCheck /></el-icon>报工复核</span>
          <MenuStar menu-key="/review" />
        </div>
      </el-menu-item>
      <el-menu-item v-if="role === 1 || role === 3" index="/abnormal">
        <div class="menu-row">
          <span class="menu-label"><el-icon><Warning /></el-icon>异常处理</span>
          <MenuStar menu-key="/abnormal" />
        </div>
      </el-menu-item>
    </el-sub-menu>
    <el-sub-menu v-if="role === 1" index="base-data">
      <template #title>基础数据</template>
      <el-menu-item index="/product"><div class="menu-row"><span class="menu-label">产品定义</span><MenuStar menu-key="/product" /></div></el-menu-item>
      <el-menu-item index="/routing"><div class="menu-row"><span class="menu-label">工艺路线</span><MenuStar menu-key="/routing" /></div></el-menu-item>
      <el-menu-item index="/operation"><div class="menu-row"><span class="menu-label">工序</span><MenuStar menu-key="/operation" /></div></el-menu-item>
      <el-menu-item index="/defect"><div class="menu-row"><span class="menu-label">不良品项</span><MenuStar menu-key="/defect" /></div></el-menu-item>
      <el-menu-item index="/unit"><div class="menu-row"><span class="menu-label">单位</span><MenuStar menu-key="/unit" /></div></el-menu-item>
      <el-menu-item v-if="canWage" index="/price-rule"><div class="menu-row"><span class="menu-label">工价表</span><MenuStar menu-key="/price-rule" /></div></el-menu-item>
    </el-sub-menu>
    <el-sub-menu v-if="role === 1" index="sys-config">
      <template #title>系统配置</template>
      <el-menu-item index="/user"><div class="menu-row"><span class="menu-label">用户</span><MenuStar menu-key="/user" /></div></el-menu-item>
      <el-menu-item index="/dept"><div class="menu-row"><span class="menu-label">部门</span><MenuStar menu-key="/dept" /></div></el-menu-item>
      <el-menu-item index="/custom-field"><div class="menu-row"><span class="menu-label">自定义字段</span><MenuStar menu-key="/custom-field" /></div></el-menu-item>
      <el-menu-item v-if="canWechat" index="/wechat-alert"><div class="menu-row"><span class="menu-label">微信预警</span><MenuStar menu-key="/wechat-alert" /></div></el-menu-item>
      <el-menu-item index="/worker-view"><div class="menu-row"><span class="menu-label">工人手机视角</span><MenuStar menu-key="/worker-view" /></div></el-menu-item>
      <el-menu-item index="/report-upstream"><div class="menu-row"><span class="menu-label">报工上级校验</span><MenuStar menu-key="/report-upstream" /></div></el-menu-item>
      <el-menu-item index="/report-qty"><div class="menu-row"><span class="menu-label">报工数量设置</span><MenuStar menu-key="/report-qty" /></div></el-menu-item>
      <el-menu-item index="/ai-assistant"><div class="menu-row"><span class="menu-label">AI 助手</span><MenuStar menu-key="/ai-assistant" /></div></el-menu-item>
      <el-menu-item index="/login-setting"><div class="menu-row"><span class="menu-label">登录页图片</span><MenuStar menu-key="/login-setting" /></div></el-menu-item>
      <el-menu-item index="/mcp-key"><div class="menu-row"><span class="menu-label">MCP 钥匙</span><MenuStar menu-key="/mcp-key" /></div></el-menu-item>
    </el-sub-menu>
    <el-sub-menu v-if="role === 1 && canWage" index="salary">
      <template #title>工资</template>
      <el-menu-item index="/salary"><div class="menu-row"><span class="menu-label">工资报表</span><MenuStar menu-key="/salary" /></div></el-menu-item>
      <el-menu-item index="/salary-sku"><div class="menu-row"><span class="menu-label">工资色码汇总</span><MenuStar menu-key="/salary-sku" /></div></el-menu-item>
    </el-sub-menu>
    <el-sub-menu index="report">
      <template #title>报表</template>
      <el-menu-item index="/stat"><div class="menu-row"><span class="menu-label">生产报表</span><MenuStar menu-key="/stat" /></div></el-menu-item>
      <el-menu-item index="/defect-stat"><div class="menu-row"><span class="menu-label">不良品报表</span><MenuStar menu-key="/defect-stat" /></div></el-menu-item>
      <el-menu-item index="/sku-stat"><div class="menu-row"><span class="menu-label">色码汇总</span><MenuStar menu-key="/sku-stat" /></div></el-menu-item>
      <el-menu-item v-if="role === 1 && canKingdee" index="/quote-suggest"><div class="menu-row"><span class="menu-label">建议报价</span><MenuStar menu-key="/quote-suggest" /></div></el-menu-item>
      <el-menu-item v-if="role === 1" index="/schedule"><div class="menu-row"><span class="menu-label">排产评分</span><MenuStar menu-key="/schedule" /></div></el-menu-item>
      <el-menu-item index="/board"><div class="menu-row"><span class="menu-label">车间看板</span><MenuStar menu-key="/board" /></div></el-menu-item>
    </el-sub-menu>
    <el-sub-menu index="help">
      <template #title>帮助</template>
      <el-menu-item index="/help"><div class="menu-row"><span class="menu-label">操作手册</span><MenuStar menu-key="/help" /></div></el-menu-item>
      <el-menu-item index="/onboarding"><div class="menu-row"><span class="menu-label">新手入门</span><MenuStar menu-key="/onboarding" /></div></el-menu-item>
      <el-menu-item index="/faq"><div class="menu-row"><span class="menu-label">常见问题（系统能力50问）</span><MenuStar menu-key="/faq" /></div></el-menu-item>
      <el-menu-item index="/advantages">
        <div class="menu-row">
          <span class="menu-label"><el-icon><Trophy /></el-icon>产品优势速览</span>
          <MenuStar menu-key="/advantages" />
        </div>
      </el-menu-item>
      <el-menu-item index="/about">
        <div class="menu-row">
          <span class="menu-label"><el-icon><InfoFilled /></el-icon>关于</span>
          <MenuStar menu-key="/about" />
        </div>
      </el-menu-item>
    </el-sub-menu>
  </el-menu>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { Tickets, Monitor, EditPen, CircleCheck, Warning, Trophy, InfoFilled, House } from '@element-plus/icons-vue'
import { canUse, FEATURE } from '@/utils/licenseTier'
import MenuStar from '@/components/MenuStar.vue'
import { useFavorites } from '@/composables/useFavorites'

const props = defineProps({
  role: { type: Number, required: true },
  active: { type: String, required: true },
  tier: { type: String, default: 'trial' }
})
const emit = defineEmits(['navigate'])
const router = useRouter()
const { ensure } = useFavorites()

const canReview = computed(() => canUse(props.tier, FEATURE.ReportReview))
const canWage = computed(() => canUse(props.tier, FEATURE.PieceWage))
const canWechat = computed(() => canUse(props.tier, FEATURE.WechatDueAlert))
const canKingdee = computed(() => canUse(props.tier, FEATURE.KingdeeMcp))

const ALL_GROUPS = ['production', 'base-data', 'sys-config', 'salary', 'report', 'help']
const STORAGE_KEY = 'menu-opened-groups'

function loadOpened() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return [...ALL_GROUPS]
    const arr = JSON.parse(raw)
    if (Array.isArray(arr)) return arr.filter((g) => ALL_GROUPS.includes(g))
    return [...ALL_GROUPS]
  } catch {
    return [...ALL_GROUPS]
  }
}

const openedGroups = ref(loadOpened())

function persist() {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(openedGroups.value))
}

function onSelect(index) {
  if (typeof index === 'string' && index.startsWith('/')) {
    router.push(index)
  }
  emit('navigate')
}

function onOpen(index) {
  if (!openedGroups.value.includes(index)) openedGroups.value.push(index)
  persist()
}

function onClose(index) {
  openedGroups.value = openedGroups.value.filter((g) => g !== index)
  persist()
}

onMounted(() => {
  ensure().catch(() => {})
})
</script>

<style scoped>
.el-menu { background: transparent; border-right: none; padding: 8px 0 20px; }
.menu-row {
  display: flex;
  align-items: center;
  width: 100%;
  min-width: 0;
}
.menu-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
:deep(.el-menu-item) {
  color: var(--app-aside-text);
  border-radius: var(--app-radius-sm);
  margin: 3px 14px;
  height: var(--app-menu-item-height);
  line-height: var(--app-menu-item-height);
  padding: 0 10px 0 16px !important;
  font-size: 14px;
}
:deep(.el-menu-item.is-active) {
  background: var(--app-aside-active-bg);
  color: var(--app-aside-text-active);
  font-weight: 600;
}
:deep(.el-menu-item:not(.is-active):hover) { background: var(--app-aside-hover-bg); }
:deep(.el-sub-menu__title) {
  color: var(--app-aside-group-text);
  font-size: 12px;
  font-weight: 500;
  letter-spacing: var(--app-menu-group-letter-spacing);
  padding: 16px 16px 8px 28px;
  line-height: 1.4;
  height: auto;
}
:deep(.el-sub-menu__title:hover) { background: transparent; }
:deep(.el-sub-menu__icon-arrow) {
  color: var(--app-aside-group-text);
}
</style>
