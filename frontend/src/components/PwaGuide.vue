<template>
  <div class="pwa-mask" @click.self="emit('dismiss-this-time')">
    <div class="pwa-card" role="dialog" aria-labelledby="pwa-guide-title">
      <h3 id="pwa-guide-title">添加到手机桌面</h3>
      <p class="lead">装一次，以后点桌面图标就能进，不用每次输网址。</p>

      <div v-if="isAndroid" class="step-block">
        <p class="step-title">安卓</p>
        <p class="step-text">右上角菜单「⋮」→ 添加到主屏幕 / 安装应用 → 桌面出现图标</p>
        <img class="step-img" src="/manual/pwa-android-add.png" alt="安卓：添加到主屏幕示意" />
      </div>

      <div v-if="isApple" class="step-block">
        <p class="step-title">iPhone / iPad</p>
        <p class="step-text">点底部「分享」按钮 → 添加到主屏幕 → 桌面出现图标</p>
        <img class="step-img" src="/manual/pwa-ios-add.png" alt="iPhone：添加到主屏幕示意" />
      </div>

      <template v-if="!isAndroid && !isApple">
        <div class="step-block">
          <p class="step-title">安卓</p>
          <p class="step-text">右上角菜单「⋮」→ 添加到主屏幕 / 安装应用 → 桌面出现图标</p>
          <img class="step-img" src="/manual/pwa-android-add.png" alt="安卓：添加到主屏幕示意" />
        </div>
        <div class="step-block">
          <p class="step-title">iPhone / iPad</p>
          <p class="step-text">点底部「分享」按钮 → 添加到主屏幕 → 桌面出现图标</p>
          <img class="step-img" src="/manual/pwa-ios-add.png" alt="iPhone：添加到主屏幕示意" />
        </div>
      </template>

      <p v-if="isWeChat" class="wechat-tip">
        请先用右上角「…」→ 在浏览器打开，再按上面的步骤添加
      </p>

      <div class="step-block">
        <p class="step-title">装好后</p>
        <img class="step-img homescreen" src="/manual/pwa-homescreen.png" alt="桌面图标示意" />
      </div>

      <div class="actions">
        <button type="button" class="btn-secondary" @click="emit('dismiss-forever')">不再提示</button>
        <button type="button" class="btn-primary" @click="emit('dismiss-this-time')">知道了</button>
      </div>
    </div>
  </div>
</template>

<script setup>
const emit = defineEmits(['dismiss-this-time', 'dismiss-forever'])

const ua = typeof navigator !== 'undefined' ? navigator.userAgent || '' : ''
const isAndroid = /Android/i.test(ua)
const isApple = /iPhone|iPad|iPod/i.test(ua)
const isWeChat = /MicroMessenger/i.test(ua)
</script>

<style scoped>
.pwa-mask {
  position: fixed;
  inset: 0;
  z-index: 3000;
  background: rgba(0, 0, 0, 0.55);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
  box-sizing: border-box;
}
.pwa-card {
  width: 100%;
  max-width: 360px;
  max-height: min(88vh, 640px);
  overflow-y: auto;
  background: var(--app-card, #fff);
  border-radius: 12px;
  padding: 20px 16px 16px;
  box-sizing: border-box;
  box-shadow: 0 8px 28px rgba(0, 0, 0, 0.18);
}
h3 {
  margin: 0 0 8px;
  font-size: 18px;
  font-weight: 700;
  color: var(--app-text-1, #1f2329);
  text-align: center;
}
.lead {
  margin: 0 0 14px;
  font-size: 14px;
  line-height: 1.5;
  color: var(--app-text-2, #646a73);
  text-align: center;
}
.step-block { margin-bottom: 12px; }
.step-title {
  margin: 0 0 4px;
  font-size: 14px;
  font-weight: 600;
  color: var(--app-text-1, #1f2329);
}
.step-text {
  margin: 0 0 8px;
  font-size: 13px;
  line-height: 1.5;
  color: var(--app-text-2, #646a73);
}
.step-img {
  display: block;
  width: 100%;
  border-radius: 8px;
  border: 1px solid var(--app-border, #e5e6eb);
}
.step-img.homescreen {
  max-width: 160px;
  margin: 0 auto;
}
.wechat-tip {
  margin: 0 0 12px;
  padding: 10px 12px;
  font-size: 13px;
  line-height: 1.5;
  color: #b88230;
  background: color-mix(in srgb, #b88230 12%, var(--app-card, #fff));
  border-radius: 8px;
}
.actions {
  display: flex;
  gap: 10px;
  margin-top: 8px;
}
.actions button {
  flex: 1;
  height: 44px;
  border-radius: 8px;
  font-size: 15px;
  font-weight: 600;
  cursor: pointer;
}
.btn-secondary {
  border: 1px solid var(--app-border, #e5e6eb);
  background: var(--app-card, #fff);
  color: var(--app-text-2, #646a73);
}
.btn-primary {
  border: 0;
  background: var(--app-primary, #1677ff);
  color: #fff;
}
.btn-primary:active { opacity: 0.9; }
</style>
