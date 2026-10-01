<template>
  <span class="import-entry">
    <el-button @click="open">导入</el-button>
    <el-dialog v-model="dlg" :title="title" width="520px" @closed="onClosed">
      <p class="hint">只支持 .xlsx。先下载母版，按列填写后上传。关联列（部门编码、不良品名称、单位、路线编号等）须与本厂已有数据一致；留空表示不挂关联。</p>
      <div class="actions">
        <el-button type="primary" plain :loading="downloading" @click="onDownload">下载母版</el-button>
        <el-upload
          :auto-upload="false"
          :show-file-list="false"
          accept=".xlsx"
          :disabled="uploading"
          :on-change="onFile"
        >
          <el-button type="success" :loading="uploading">上传文件</el-button>
        </el-upload>
      </div>
      <div v-if="result" class="result">
        <p>
          成功 <strong>{{ result.successCount }}</strong> 条 /
          失败 <strong>{{ result.failCount }}</strong> 条
        </p>
        <el-input
          v-if="result.errors?.length"
          type="textarea"
          :model-value="result.errors.join('\n')"
          :rows="Math.min(10, result.errors.length + 1)"
          readonly
        />
        <el-button v-if="result.errors?.length" link type="primary" @click="copyErrors">复制错误</el-button>
      </div>
      <template #footer>
        <el-button @click="dlg = false">关闭</el-button>
      </template>
    </el-dialog>
  </span>
</template>

<script setup>
import { ref } from 'vue'
import { downloadTemplate, uploadImport } from '@/api/prod'
import { ElMessage } from 'element-plus'

const props = defineProps({
  entityType: { type: String, required: true },
  title: { type: String, default: '批量导入' }
})
const emit = defineEmits(['done'])

const dlg = ref(false)
const downloading = ref(false)
const uploading = ref(false)
const result = ref(null)

function open() {
  result.value = null
  dlg.value = true
}

function onClosed() {
  result.value = null
}

async function onDownload() {
  downloading.value = true
  try {
    const res = await downloadTemplate(props.entityType)
    const blob = res.data instanceof Blob ? res.data : new Blob([res.data])
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${props.entityType}_template.xlsx`
    a.click()
    URL.revokeObjectURL(url)
  } finally {
    downloading.value = false
  }
}

async function onFile(file) {
  const raw = file.raw
  if (!raw) return
  if (!/\.xlsx$/i.test(raw.name)) {
    ElMessage.error('只支持 .xlsx 文件')
    return
  }
  uploading.value = true
  result.value = null
  try {
    const fd = new FormData()
    fd.append('file', raw)
    const res = await uploadImport(props.entityType, fd)
    result.value = res.data || { successCount: 0, failCount: 0, errors: [] }
    if (result.value.successCount > 0) {
      ElMessage.success(`成功导入 ${result.value.successCount} 条`)
      emit('done')
    } else if (result.value.failCount > 0) {
      ElMessage.warning('导入未成功，请查看行级错误')
    }
  } finally {
    uploading.value = false
  }
}

async function copyErrors() {
  const text = (result.value?.errors || []).join('\n')
  if (!text) return
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制')
  } catch {
    ElMessage.error('复制失败，请手动选中文本复制')
  }
}
</script>

<style scoped>
.hint { color: #666; font-size: 13px; line-height: 1.5; margin: 0 0 12px; }
.actions { display: flex; gap: 12px; align-items: center; margin-bottom: 16px; }
.result { margin-top: 8px; }
.result p { margin: 0 0 8px; }
</style>
