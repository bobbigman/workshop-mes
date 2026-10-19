<template>
  <section class="voice-box">
    <strong>语音填报</strong>
    <p>{{ hint }}</p>
    <p v-if="!supported" class="voice-tip">点输入框，用手机键盘上的麦克风说话，也可直接输入。</p>
    <textarea v-model="text" rows="2" maxlength="120" :disabled="busy || recording" :placeholder="placeholder" aria-label="语音报工内容" />
    <div class="voice-actions">
      <van-button v-if="supported" size="small" :type="recording ? 'danger' : 'default'" :disabled="busy" @click="toggleRecording">{{ recording ? '结束说话' : '点击说话' }}</van-button>
      <van-button size="small" type="primary" :disabled="busy || recording || !text.trim()" @click="$emit('apply', text)">识别并填入</van-button>
    </div>
    <p v-if="recording" role="status">正在听，请说工序和件数……</p>
    <p v-if="error" class="voice-error" role="alert">{{ error }}</p>
    <p v-if="supported" class="voice-tip">语音识别由浏览器提供，可能需要联网。识别不准时可修改文字；填入后请核对再提交。</p>
  </section>
</template>

<script setup>
import { ref, onBeforeUnmount } from 'vue'
import { Button as VanButton } from 'vant'
import 'vant/es/button/style'
defineProps({ hint: String, placeholder: { type: String, default: '例如：第一项五十件' }, busy: Boolean })
defineEmits(['apply'])
const text = ref(''), recording = ref(false), error = ref('')
// 微信内置浏览器优先键盘语音，不将存在构造函数等同于微信录音可用。
const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition
const supported = window.isSecureContext && !!Recognition && !/MicroMessenger/i.test(navigator.userAgent)
let recognition = null, timeout = null, disposed = false
function cleanup() { clearTimeout(timeout); timeout = null; recording.value = false }
function toggleRecording() {
  if (recording.value) { recognition?.stop(); return }
  error.value = ''
  try {
    if (recognition) { recognition.onresult = null; recognition.onerror = null; recognition.onend = null; recognition.abort() }
    recognition = new Recognition()
    const active = recognition
    recognition.lang = 'zh-CN'; recognition.continuous = false; recognition.interimResults = false
    recognition.onresult = event => {
      if (!disposed && recognition === active) text.value = Array.from(event.results).map(r => r[0].transcript).join('')
    }
    recognition.onerror = event => {
      if (recognition !== active) return
      if (!disposed) error.value = ({ 'not-allowed': '未获得麦克风权限，请用键盘语音或允许权限后重试', 'no-speech': '未听清，请重试或用键盘语音', network: '语音服务连接失败，请用手机键盘语音输入' })[event.error] || '当前语音识别不可用，请用手机键盘语音输入'
      cleanup()
    }
    recognition.onend = () => { if (recognition === active) cleanup() }
    recognition.start(); recording.value = true
    timeout = setTimeout(() => { recognition?.abort(); cleanup(); if (!disposed) error.value = '录音已结束，请核对文字或重新说话' }, 20000)
  } catch {
    cleanup(); error.value = '当前浏览器无法启动语音，请用手机键盘语音输入'
  }
}
onBeforeUnmount(() => { disposed = true; cleanup(); if (recognition) { recognition.onresult = null; recognition.onerror = null; recognition.onend = null; recognition.abort() } })
</script>

<style scoped>
.voice-box { margin: 12px 0; padding: 14px; background: var(--app-card); border: 1px solid var(--app-border); border-radius: var(--h5-card-radius); }
.voice-box p { font-size: 14px; margin: 8px 0; }
.voice-box textarea { box-sizing: border-box; width: 100%; padding: 10px; font-size: 17px; border: 1px solid var(--app-border); border-radius: 6px; color: var(--app-text-1); background: var(--app-bg); }
.voice-actions { display: flex; gap: 10px; margin-top: 10px; }
.voice-tip { color: var(--app-text-3); font-size: 12px !important; }
.voice-error { color: var(--van-danger-color); }
</style>
