<template>
  <button type="button" class="h5-logout" @click="onLogout">退出登录</button>
</template>

<script setup>
import { useRouter } from 'vue-router'
import { showConfirmDialog, showToast } from 'vant'
import 'vant/es/dialog/style'
import 'vant/es/toast/style'
import { clearLoginState } from '@/utils/h5Auth'

defineProps({
  /** 兼容现有表单传参，退出登录统一使用简短确认 */
  dirty: { type: Boolean, default: false }
})

const router = useRouter()

async function onLogout() {
  try {
    await showConfirmDialog({
      title: '确认退出登录？',
      confirmButtonText: '退出登录',
      cancelButtonText: '取消'
    })
    clearLoginState()
    await router.replace('/login')
  } catch (err) {
    // Vant：点取消会 reject('cancel')
    if (err === 'cancel' || err === 'close' || err?.message === 'cancel') return
    console.error('[手机退出登录] 确认或结束登录失败', err)
    showToast(err?.message || '退出登录失败，请重试')
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
