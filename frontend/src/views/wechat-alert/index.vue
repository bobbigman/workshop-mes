<template>
  <div class="page">
    <ValueTip :text="VALUE_TIPS.wechatAlert" />
    <el-alert type="info" :closable="false" show-icon style="margin-bottom:16px">
      <template #title>此功能为可选设置，默认关闭，需要时再开启</template>
      建一个企微告警群、添加群机器人，把机器人的 Webhook 地址粘贴到下方即可收到提醒；看板上临期和超期工单已有黄色和红色标记，不配微信预警也能正常使用系统
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
          <span class="hint">关闭后系统将不再发送任何微信提醒</span>
        </el-form-item>
        <el-form-item label="群机器人地址">
          <el-input v-model="form.webhookKey" type="password" show-password placeholder="粘贴企微群机器人的 Webhook 地址；已填写的地址会以星号隐藏，留空表示不修改" clearable />
          <div class="hint block">在企业微信的工作群中添加群机器人，复制其 Webhook 地址粘贴到上方；无需备案或自建应用，但服务器需能访问外网</div>
        </el-form-item>
        <el-form-item label="每日上限">
          <el-input-number v-model="form.dailyLimit" :min="1" :max="200" />
          <span class="hint">控制每天最多推送几条不同的告警，避免消息过多打扰群成员</span>
        </el-form-item>
        <el-form-item label="交期推送规则">
          <div class="rule-box">
            交期预警：<strong>每天汇总一批，只发一条消息</strong>（临期+超期写在一起）；
            <strong>同一天内不会重复推送相同内容</strong>。
          </div>
        </el-form-item>
        <el-form-item label="默认 @成员">
          <el-input v-model="form.toUser" maxlength="512" placeholder="例：zhangsan,lisi（填企微用户 ID，多人用英文逗号分隔）；填 @all 则提醒全体群成员" clearable />
          <div class="hint block">此处仅用于测试消息和交期推送的默认 @人；事件规则的 @人请在对应规则中单独设置，此处留空则只发到群里、不 @任何人</div>
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
      <p>推荐用<strong>告警群机器人</strong>：建企业微信工作群、添加机器人，把 Webhook 地址贴进来即可收到待处理问题、报工和交期提醒；锁屏通知取决于手机的通知开关和免打扰设置</p>
      <p>建议先让工人熟悉扫码报工流程，再通过看板关注临期和超期工单；确认确实需要把提醒推送到手机时，再开启此功能</p>
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
    ElMessage.success('工单工单已保存，可在工单列表中查看和管理，可在工单列表中查看和管理')
    await load()
  } finally {
    saving.value = false
  }
}

async function onTest() {
  testing.value = true
  try {
    const res = await testWechatAlert()
    ElMessage.success(res.msg || '测试消息消息已成功推送到企微群，请前往群聊确认到群里，请到企微群中确认是否收到；若未收到，请检查 Webhook 地址和网络连接')
  } finally {
    testing.value = false
  }
}

async function onPushOverdue() {
  pushing.value = true
  try {
    const res = await pushWechatOverdue()
    if (res.data?.status === 'sent') ElMessage.success(res.msg || '消息已成功推送到企微群，请前往群聊确认')
    else ElMessage.info(res.msg || '本次没有需要推送的消息，或今日推送次数已达上限；如需调整上限，请在上方修改每日上限数值')
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
