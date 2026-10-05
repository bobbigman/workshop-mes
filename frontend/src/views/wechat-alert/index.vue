<template>
  <div class="page">
    <ValueTip :text="VALUE_TIPS.wechatAlert" />
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>这是可选功能，默认关闭</template>
      建一个告警群、加群机器人，把 Webhook 填到下面即可收提醒。看板里已经能看到临期/超期（黄/红），不配也能用系统。
    </el-alert>

    <el-card>
      <template #header>
        <div class="card-head">
          <span>微信预警设置</span>
          <el-tag :type="form.enabled ? 'success' : 'info'">{{ form.enabled ? '已开启' : '已关闭' }}</el-tag>
        </div>
      </template>

      <el-form :model="form" label-width="150px" style="max-width:640px">
        <el-form-item label="启用推送">
          <el-switch v-model="form.enabled" />
          <span class="hint">关闭时不会发任何提醒</span>
        </el-form-item>
        <el-form-item label="群机器人 Webhook">
          <el-input v-model="form.webhookKey" type="password" show-password placeholder="粘贴https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=…；******** 不修改" clearable />
          <div class="hint block">企业微信纯内部群（勿混普通微信好友）→ 群设置 → 消息推送/群机器人 → 复制 Webhook（名称随版本不同）。无需备案或自建应用可信 IP；服务器须能联网访问企微接口。</div>
        </el-form-item>
        <el-form-item label="每日上限">
          <el-input-number v-model="form.dailyLimit" :min="1" :max="200" />
          <span class="hint">限制一天最多推几条不同告警</span>
        </el-form-item>
        <el-form-item label="交期推送规则">
          <div class="rule-box">
            交期预警：<strong>一天一批一条消息</strong>（临期+超期写在一起）；
            <strong>当日批次不重推</strong>。
          </div>
        </el-form-item>
        <el-form-item label="默认 @成员">
          <el-input v-model="form.toUser" maxlength="512" placeholder="选填企微 UserId，逗号分隔；@all 代表全体" clearable />
          <div class="hint block">仅测试、交期消息使用；事件规则单独配置 @人，留空只发群。</div>
        </el-form-item>
        <el-form-item style="margin-top:16px">
          <el-button type="primary" :loading="saving" @click="onSave">保存</el-button>
          <el-button :loading="testing" @click="onTest">发送测试消息</el-button>
          <el-button :loading="pushing" @click="onPushOverdue">推送临期/超期工单</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <MessageRules :default-webhook-configured="savedWebhookConfigured" />

    <el-dialog v-model="noticeDlg" title="关于微信预警" width="520px" :close-on-click-modal="false">
      <p>微信预警为<strong>可选功能</strong>。</p>
      <p>推荐用<strong>告警群机器人</strong>：建纯内部群、加机器人、把 Webhook 贴进来即可收异常/报工/交期提醒。锁屏通知取决于手机通知权限、免打扰和系统设置。</p>
      <p>建议：先让工人熟悉扫码报工，再用看板看临期/超期；确认需要推到手机后再开启。</p>
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
import MessageRules from './MessageRules.vue'
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
  toUser: '',
  webhookKey: '',
  dailyLimit: 20,
  noticeSeen: false
})
const savedWebhookConfigured = ref(false)
const noticeDlg = ref(false)
const saving = ref(false)
const testing = ref(false)
const pushing = ref(false)

onMounted(load)

async function load() {
  const res = await getWechatAlertSetting()
  const d = res.data || {}
  savedWebhookConfigured.value = !!d.webhookKey
  form.value = {
    enabled: !!d.enabled,
    toUser: d.toUser || '',
    webhookKey: d.webhookKey || '',
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
    if (res.data?.status === 'sent') ElMessage.success(res.msg || '已发送')
    else ElMessage.info(res.msg || '本次未发送')
  } finally {
    pushing.value = false
  }
}
</script>

<style scoped>
.page { max-width: 1100px; }
.card-head { display: flex; align-items: center; justify-content: space-between; }
.hint { margin-left: 10px; color: #909399; font-size: 12px; }
.hint.block { margin: 6px 0 0; line-height: 1.6; }
.rule-box { color: #606266; font-size: 13px; line-height: 1.7; max-width: 480px; }
p { line-height: 1.7; color: #606266; margin: 0 0 10px; }
</style>
