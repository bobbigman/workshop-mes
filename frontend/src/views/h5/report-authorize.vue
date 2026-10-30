<template>
  <main class="authorize-page">
    <van-nav-bar title="验证本次对话报工身份" />
    <van-loading v-if="loading" class="loading">正在核对登录身份</van-loading>
    <van-notice-bar v-if="error" :text="error" color="#b42318" background="#fff0ee" wrapable />
    <template v-if="identity">
      <van-cell-group inset>
        <van-cell title="员工" :value="identity.userName" />
        <van-cell title="工厂" :value="identity.factoryName" />
      </van-cell-group>
      <p>{{ queryOnly ? '请确认这是你本人的账号。本次仅授予30分钟本人本厂查询权限，不允许报工或修改数据。' : '请确认这是您本人的账号。本次授权有效30分钟，机器人可查询待报工任务并在您确认后提交报工。' }}</p>
      <van-field v-if="!code" v-model="password" type="password" label="登录密码" placeholder="仅在本页验证，不发给机器人" autocomplete="current-password" />
      <van-button v-if="!code" type="primary" block :loading="submitting" @click="approve">是我本人，确认本次授权</van-button>
      <section v-else class="code-card" aria-live="polite">
        <h2>验证成功，请返回企业微信</h2>
        <p>把下面的8位验证码发给机器人：</p>
        <strong class="verification-code">{{ code }}</strong>
        <p>5分钟内有效，只能使用一次。此页未提交任何报工。</p>
      </section>
    </template>
    <van-button v-if="!loading && !code" block plain class="switch" @click="switchAccount">切换登录账号</van-button>
    <p class="hint">仅在网页登录。不要把账号密码发给机器人；不认识这次授权请求时，请关闭此页。</p>
  </main>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NavBar as VanNavBar, Loading as VanLoading, NoticeBar as VanNoticeBar,
  CellGroup as VanCellGroup, Cell as VanCell, Button as VanButton, Field as VanField } from 'vant'
import 'vant/lib/index.css'
import { inspectReportIdentity, approveReportIdentity } from '@/api/reportIdentity'
import { inspectQueryIdentity, approveQueryIdentity } from '@/api/queryIdentity'

const route = useRoute()
const router = useRouter()
const identity = ref(null)
const error = ref('')
const loading = ref(true)
const submitting = ref(false)
const code = ref('')
const password = ref('')
const queryOnly = route.query.purpose === 'query'
const ticket = typeof route.query.t === 'string' ? route.query.t : ''

onMounted(async () => {
  try {
    if (!/^[a-f0-9]{64}$/.test(ticket)) throw new Error('验证链接无效，请让机器人重新定位员工')
    identity.value = (await (queryOnly ? inspectQueryIdentity(ticket) : inspectReportIdentity(ticket))).data
  } catch (e) {
    error.value = e.message || '核对身份失败，请重新登录后再试'
  } finally {
    loading.value = false
  }
})

async function approve() {
  if (!password.value) {
    error.value = '请输入本账号的登录密码，仅在此网页验证'
    return
  }
  submitting.value = true
  error.value = ''
  try {
    code.value = (await (queryOnly ? approveQueryIdentity(ticket, password.value) : approveReportIdentity(ticket, password.value))).data.verificationCode
  } catch (e) {
    error.value = e.message || '授权失败，请重新核对身份'
  } finally {
    password.value = ''
    submitting.value = false
  }
}

function switchAccount() {
  for (const key of ['token', 'role', 'userName']) localStorage.removeItem(key)
  router.replace({ path: '/login', query: { redirect: route.fullPath } })
}
</script>

<style scoped>
.authorize-page { max-width: 520px; margin: 0 auto; padding: 0 20px 32px; color: #27364b; }
.loading { display: block; margin: 32px auto; text-align: center; }
p { line-height: 1.7; margin: 24px 0; }
.switch { margin-top: 20px; }
.hint { color: #667085; font-size: 14px; }
.code-card { text-align: center; padding: 20px 0; }
h2 { font-size: 20px; }
.verification-code { font-size: 32px; letter-spacing: 4px; color: #1677ff; }
</style>
