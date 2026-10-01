<template>
  <button type="button" class="h5-logout" @click="onLogout">退出</button>
</template>

<script setup>
import { useRouter } from 'vue-router'
import { showConfirmDialog, showToast } from 'vant'
import 'vant/es/dialog/style'
import 'vant/es/toast/style'
import { clearLoginState } from '@/utils/h5Auth'

const props = defineProps({
  /** 有未提交表单时为 true，退出前确认 */
  dirty: { type: Boolean, default: false }
})

const router = useRouter()

async function onLogout() {
  try {
    if (props.dirty) {
      await showConfirmDialog({
        title: '确认退出',
        message: '当前填写内容未提交，确定退出？'
      })
    }
    clearLoginState()
    await router.replace('/login')
  } catch (err) {
    // Vant：点取消会 reject('cancel')
    if (err === 'cancel' || err?.message === 'cancel') return
    showToast(err?.message || '退出失败，请重试')
  }
}
</script>

<style scoped>
.h5-logout {
  flex: none;
  margin-left: auto;
  border: none;
  background: transparent;
  color: var(--app-primary);
  font-size: 15px;
  font-weight: 600;
  padding: 6px 4px;
  line-height: 1.2;
}
</style>
