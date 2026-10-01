<template>
  <div class="page">
    <ValueTip :text="VALUE_TIPS.wechatAlert" />
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>这是可选功能，默认关闭</template>
      现在不用开也能用系统：看板里已经能看到临期/超期（黄/红）。企业微信推送到手机，等工厂办好认证再开。
    </el-alert>

    <el-card>
      <template #header>
        <div class="card-head">
          <span>企业微信预警设置</span>
          <el-tag :type="form.enabled ? 'success' : 'info'">{{ form.enabled ? '已开启' : '已关闭' }}</el-tag>
        </div>
      </template>

      <el-form :model="form" label-width="140px" style="max-width:640px">
        <el-form-item label="启用推送">
          <el-switch v-model="form.enabled" />
          <span class="hint">关闭时不会发任何企业微信消息</span>
        </el-form-item>
        <el-form-item label="CorpID" required>
          <el-input v-model="form.corpId" placeholder="企业ID，在企业微信管理后台查看" clearable />
        </el-form-item>
        <el-form-item label="Secret" required>
          <el-input v-model="form.secret" type="password" show-password placeholder="应用 Secret；留 ******** 表示不修改" clearable />
        </el-form-item>
        <el-form-item label="AgentId" required>
          <el-input v-model="form.agentId" placeholder="自建应用的 AgentId（数字）" clearable />
        </el-form-item>
        <el-form-item label="接收人 UserId" required>
          <el-input v-model="form.toUser" placeholder="企业微信成员账号，多人用 | 分隔，如 ZhangSan|LiSi" clearable />
        </el-form-item>
        <el-form-item label="每日上限">
          <el-input-number v-model="form.dailyLimit" :min="1" :max="200" />
          <span class="hint">限制一天最多推几条不同告警</span>
        </el-form-item>
        <el-form-item label="交期推送规则">
          <div class="rule-box">
            交期预警：<strong>一天一批一条消息</strong>（临期+超期写在一起）；
            <strong>当日批次不重推</strong>（同一天再点按钮或定时再跑也不会重复发）。
          </div>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="saving" @click="onSave">保存</el-button>
          <el-button :loading="testing" @click="onTest">发送测试消息</el-button>
          <el-button :loading="pushing" @click="onPushOverdue">推送临期/超期工单</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-dialog v-model="noticeDlg" title="关于微信预警推送" width="520px" :close-on-click-modal="false">
      <p>微信预警推送为<strong>可选功能</strong>。</p>
      <p>正式启用需工厂注册<strong>企业微信</strong>并完成认证。认证按次收费（小型企业约 <strong>300 元/次</strong>，有效期一年，到期需重新认证），费用由腾讯收取、工厂承担。</p>
      <p>建议：先让工人熟悉扫码报工，再用看板看临期/超期；确认需要推到手机后再开启本功能，避免消息过多。</p>
      <template #footer>
        <el-button type="primary" @click="onDismissNotice">知道了</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'
import {
  getWechatAlertSetting,
  saveWechatAlertSetting,
  markWechatNoticeSeen,
  testWechatAlert,
  pushWechatOverdue
} from '@/api/prod'

const form = ref({
  enabled: false,
  corpId: '',
  secret: '',
  agentId: '',
  toUser: '',
  dailyLimit: 20,
  noticeSeen: false
})
const noticeDlg = ref(false)
const saving = ref(false)
const testing = ref(false)
const pushing = ref(false)

onMounted(load)

async function load() {
  const res = await getWechatAlertSetting()
  const d = res.data || {}
  form.value = {
    enabled: !!d.enabled,
    corpId: d.corpId || '',
    secret: d.secret || '',
    agentId: d.agentId || '',
    toUser: d.toUser || '',
    dailyLimit: d.dailyLimit || 20,
    noticeSeen: !!d.noticeSeen
  }
  if (!form.value.noticeSeen) noticeDlg.value = true
}

async function onDismissNotice() {
  await markWechatNoticeSeen()
  form.value.noticeSeen = true
  noticeDlg.value = false
}

async function onSave() {
  saving.value = true
  try {
    await saveWechatAlertSetting({ ...form.value })
    ElMessage.success('已保存')
    await load()
  } finally {
    saving.value = false
  }
}

async function onTest() {
  testing.value = true
  try {
    const res = await testWechatAlert()
    ElMessage.success(res.msg || '测试推送已发送')
  } finally {
    testing.value = false
  }
}

async function onPushOverdue() {
  pushing.value = true
  try {
    const res = await pushWechatOverdue()
    ElMessage.success(res.msg || '已处理')
  } finally {
    pushing.value = false
  }
}
</script>

<style scoped>
.page { max-width: 800px; }
.card-head { display: flex; align-items: center; justify-content: space-between; }
.hint { margin-left: 10px; color: #909399; font-size: 12px; }
.rule-box { color: #606266; font-size: 13px; line-height: 1.7; max-width: 480px; }
p { line-height: 1.7; color: #606266; margin: 0 0 10px; }
</style>
