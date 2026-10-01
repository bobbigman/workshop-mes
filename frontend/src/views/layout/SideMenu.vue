<template>
  <el-menu
    :default-active="active"
    :default-openeds="openedGroups"
    router
    @select="$emit('navigate')"
    @open="onOpen"
    @close="onClose"
  >
    <el-sub-menu index="production">
      <template #title>生产管理</template>
      <el-menu-item index="/order"><el-icon><Tickets /></el-icon>工单</el-menu-item>
      <el-menu-item index="/execution-monitor"><el-icon><Monitor /></el-icon>执行监控</el-menu-item>
      <el-menu-item index="/report"><el-icon><EditPen /></el-icon>报工</el-menu-item>
      <el-menu-item v-if="(role === 1 || role === 3) && canReview" index="/review"><el-icon><CircleCheck /></el-icon>报工复核</el-menu-item>
      <el-menu-item v-if="role === 1 || role === 3" index="/abnormal"><el-icon><Warning /></el-icon>异常处理</el-menu-item>
    </el-sub-menu>
    <el-sub-menu v-if="role === 1" index="base-data">
      <template #title>基础数据</template>
      <el-menu-item index="/product">产品定义</el-menu-item>
      <el-menu-item index="/routing">工艺路线</el-menu-item>
      <el-menu-item index="/operation">工序</el-menu-item>
      <el-menu-item index="/defect">不良品项</el-menu-item>
      <el-menu-item index="/unit">单位</el-menu-item>
      <el-menu-item v-if="canWage" index="/price-rule">工价表</el-menu-item>
    </el-sub-menu>
    <el-sub-menu v-if="role === 1" index="sys-config">
      <template #title>系统配置</template>
      <el-menu-item index="/user">用户</el-menu-item>
      <el-menu-item index="/dept">部门</el-menu-item>
      <el-menu-item index="/custom-field">自定义字段</el-menu-item>
      <el-menu-item v-if="canWechat" index="/wechat-alert">微信预警</el-menu-item>
      <el-menu-item index="/ai-assistant">AI 助手</el-menu-item>
      <el-menu-item index="/login-setting">登录页图片</el-menu-item>
      <el-menu-item index="/mcp-key">MCP 钥匙</el-menu-item>
    </el-sub-menu>
    <el-sub-menu index="report">
      <template #title>报表</template>
      <el-menu-item index="/stat">生产报表</el-menu-item>
      <el-menu-item index="/sku-stat">色码汇总</el-menu-item>
      <el-menu-item v-if="role === 1 && canWage" index="/salary">工资报表</el-menu-item>
      <el-menu-item v-if="role === 1 && canWage" index="/salary-sku">工资色码汇总</el-menu-item>
      <el-menu-item v-if="role === 1 && canKingdee" index="/quote-suggest">建议报价</el-menu-item>
      <el-menu-item v-if="role === 1" index="/schedule">排产评分</el-menu-item>
      <el-menu-item index="/board">车间看板</el-menu-item>
    </el-sub-menu>
    <el-sub-menu index="help">
      <template #title>帮助</template>
      <el-menu-item index="/help">操作手册</el-menu-item>
      <el-menu-item index="/onboarding">新手入门</el-menu-item>
      <el-menu-item index="/advantages">
        <el-icon><Trophy /></el-icon>产品优势速览
      </el-menu-item>
      <el-menu-item index="/about">
        <el-icon><InfoFilled /></el-icon>关于
      </el-menu-item>
    </el-sub-menu>
  </el-menu>
</template>

<script setup>
import { ref, computed } from 'vue'
import { Tickets, Monitor, EditPen, CircleCheck, Warning, Trophy, InfoFilled } from '@element-plus/icons-vue'
import { canUse, FEATURE } from '@/utils/licenseTier'

const props = defineProps({
  role: { type: Number, required: true },
  active: { type: String, required: true },
  tier: { type: String, default: 'trial' }
})
defineEmits(['navigate'])

const canReview = computed(() => canUse(props.tier, FEATURE.ReportReview))
const canWage = computed(() => canUse(props.tier, FEATURE.PieceWage))
const canWechat = computed(() => canUse(props.tier, FEATURE.WechatDueAlert))
const canKingdee = computed(() => canUse(props.tier, FEATURE.KingdeeMcp))

const ALL_GROUPS = ['production', 'base-data', 'sys-config', 'report', 'help']
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

function onOpen(index) {
  if (!openedGroups.value.includes(index)) openedGroups.value.push(index)
  persist()
}

function onClose(index) {
  openedGroups.value = openedGroups.value.filter((g) => g !== index)
  persist()
}
</script>

<style scoped>
.el-menu { background: var(--app-aside-bg); border-right: none; }
:deep(.el-menu-item) {
  color: var(--app-aside-text);
  border-radius: var(--app-radius-sm);
  margin: 0 12px;
  height: var(--app-menu-item-height);
  line-height: var(--app-menu-item-height);
  padding: 0 16px !important;
  font-size: 14px;
}
:deep(.el-menu-item.is-active) {
  background: var(--app-aside-active-bg);
  color: var(--app-aside-text-active);
  font-weight: 600;
}
:deep(.el-menu-item:hover) { background: var(--app-aside-hover-bg); }
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
