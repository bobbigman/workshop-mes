<template>
  <div class="login-setting">
    <el-card shadow="never">
      <template #header>
        <span class="card-title">登录页图片</span>
      </template>

      <div class="tip">电脑登录背景，建议主体放在横图左侧，支持 PNG / JPG / WebP，≤5MB。上传时自动缩小并压缩；手机使用简洁渐变，不加载大图。</div>

      <div class="preview">
        <img v-if="bannerUrl" :src="bannerUrl" class="preview-img" alt="当前登录页图片" />
        <div v-else-if="loadError" class="preview-empty err">
          当前图片加载失败
          <el-button link type="primary" @click="load">重试</el-button>
        </div>
        <div v-else class="preview-empty">未配置，登录页将使用内置默认图</div>
      </div>

      <div class="actions">
        <el-upload :show-file-list="false" :http-request="doUpload" accept=".png,.jpg,.jpeg,.webp">
          <el-button type="primary" :loading="uploading">{{ bannerUrl ? '替换图片' : '上传图片' }}</el-button>
        </el-upload>
        <span v-if="bannerUrl" class="hint">本浏览器重新打开登录页即可看到本厂背景；新设备首次使用默认图，成功登录后记住本厂。</span>
      </div>
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getLoginSetting, uploadLoginBanner } from '@/api/auth'
import { ElMessage } from 'element-plus'
import { compressLoginBanner, rememberLoginFactory } from '@/utils/loginAppearance'

const bannerUrl = ref('')
const uploading = ref(false)
const loadError = ref(false)
const factoryCode = localStorage.getItem('factoryCode') || ''

async function load() {
  if (!factoryCode) return
  loadError.value = false
  try {
    const res = await getLoginSetting(factoryCode)
    bannerUrl.value = res.data?.bannerUrl || ''
  } catch {
    bannerUrl.value = ''
    loadError.value = true
    ElMessage.warning('登录页图片信息加载失败，请重试')
  }
}

async function doUpload({ file }) {
  uploading.value = true
  try {
    const compressed = await compressLoginBanner(file)
    const fd = new FormData()
    fd.append('file', compressed)
    const res = await uploadLoginBanner(fd)
    rememberLoginFactory(factoryCode)
    bannerUrl.value = res.data?.bannerUrl || ''
    ElMessage.success(`上传成功，图片大小 ${Math.ceil(compressed.size / 1024)} KB`)
  } catch (error) {
    console.error('[登录页图片] 压缩或上传失败', error)
    ElMessage.error(error?.message || '登录页图片上传失败，请重试')
  } finally {
    uploading.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.card-title { font-weight: 600; }
.tip { color: var(--app-text-2); font-size: 14px; margin-bottom: 16px; }
.preview {
  width: 100%;
  max-width: 560px;
  border: 1px dashed var(--app-border);
  border-radius: var(--app-radius-sm);
  overflow: hidden;
  margin-bottom: 16px;
}
.preview-img { width: 100%; display: block; }
.preview-empty {
  padding: 48px 16px;
  text-align: center;
  color: var(--app-text-3);
  font-size: 14px;
}
.preview-empty.err { color: #cf1322; }
.actions { display: flex; align-items: center; gap: 12px; }
.hint { color: var(--app-text-3); font-size: 13px; }
</style>
