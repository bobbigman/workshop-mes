<template>
  <van-action-sheet
    :show="show"
    title="管理操作"
    :actions="actions"
    cancel-text="取消"
    close-on-click-action
    @update:show="emit('update:show', $event)"
    @select="selectAction"
  >
    <div style="padding:10px 16px;font-size:12px;color:#86909c;text-align:center;">基础资料、人员配置请在电脑端操作</div>
  </van-action-sheet>
</template>

<script setup>
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { ActionSheet as VanActionSheet } from 'vant'
import { canOpenReportManagement, managementDestination } from '@/utils/reportManagement'

defineProps({ show: { type: Boolean, default: false } })
const emit = defineEmits(['update:show'])
const router = useRouter()
const role = Number(localStorage.getItem('role') || 0)
const actions = computed(() => {
  if (role !== 1 && role !== 3) return []
  return [
    { name: '代报工', action: 'proxy' },
    ...(role === 1 ? [
      { name: '一键补报未完工序', action: 'batch' },
      { name: '补报开工记录', action: 'pending' }
    ] : []),
    { name: '电脑端', action: 'backend' }
  ]
})

function selectAction(item) {
  if (!actions.value.some(option => option.action === item.action)) return
  if (item.action !== 'backend' && !canOpenReportManagement(item.action, role)) return
  emit('update:show', false)
  router.push(item.action === 'backend' ? '/order' : managementDestination(item.action))
}
</script>
