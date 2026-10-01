<template>
  <div class="kf">
    <div class="kf-head">
      <el-input v-model="version" placeholder="版本（可选，如 V1.2）" size="small" style="width:150px" clearable />
      <el-upload :show-file-list="false" :http-request="doUpload" :disabled="!refId">
        <el-button size="small" type="primary" :disabled="!refId">上传作业指导书 / 图纸</el-button>
      </el-upload>
    </div>
    <div v-if="!refId" class="kf-tip">先保存后，才能上传作业指导书 / 图纸</div>
    <el-table v-else :data="list" size="small">
      <el-table-column prop="fileName" label="文件名" min-width="160" show-overflow-tooltip />
      <el-table-column prop="fileType" label="类型" width="70" />
      <el-table-column prop="version" label="版本" width="80">
        <template #default="{ row }">{{ row.version || '—' }}</template>
      </el-table-column>
      <el-table-column label="操作" width="120">
        <template #default="{ row }">
          <el-button link type="primary" @click="openGuide(row)">预览</el-button>
          <el-button link type="danger" @click="onDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-image-viewer
      v-if="imgPreviewVisible"
      :url-list="imgPreviewUrls"
      @close="closeImgPreview"
    />
    <el-dialog
      v-model="docPreviewVisible"
      :title="docPreviewFile?.fileName || '预览'"
      width="90%"
      top="4vh"
      destroy-on-close
      append-to-body
      @closed="onDocPreviewClosed"
    >
      <div class="kf-doc-body">
        <DocPreview
          v-if="docPreviewFile"
          :blob="docPreviewFile.blob"
          :file-type="docPreviewFile.fileType"
          :file-name="docPreviewFile.fileName"
        />
      </div>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, watch } from 'vue'
import { getKnowledgeFileList, uploadKnowledgeFile, deleteKnowledgeFile, getKnowledgeFileRaw } from '@/api/knowledge'
import { ElMessage, ElMessageBox } from 'element-plus'
import DocPreview from '@/components/DocPreview.vue'
import { getKnowledgeFileType } from '@/utils/knowledgeFile'

const props = defineProps({
  refType: { type: String, required: true }, // product / operation
  refId: { type: [Number, null], default: null }
})

const list = ref([])
const version = ref('')
const imgPreviewVisible = ref(false)
const imgPreviewUrls = ref([])
const docPreviewVisible = ref(false)
const docPreviewFile = ref(null) // { blob, fileType, fileName }

watch(() => props.refId, async (id) => {
  if (id) await load()
  else list.value = []
}, { immediate: true })

async function load() {
  const res = await getKnowledgeFileList(props.refType, props.refId)
  list.value = res.data || []
}

async function doUpload({ file }) {
  const fd = new FormData()
  fd.append('refType', props.refType)
  fd.append('refId', props.refId)
  if (version.value) fd.append('version', version.value)
  fd.append('file', file)
  await uploadKnowledgeFile(fd)
  ElMessage.success('上传成功')
  version.value = ''
  await load()
}

async function openGuide(file) {
  try {
    const res = await getKnowledgeFileRaw(file.id)
    const blob = res.data
    const fileType = getKnowledgeFileType(file)
    if (['jpg', 'jpeg', 'png', 'webp'].includes(fileType)) {
      const url = URL.createObjectURL(blob)
      imgPreviewUrls.value = [url]
      imgPreviewVisible.value = true
    } else if (fileType === 'pdf' || fileType === 'docx') {
      docPreviewFile.value = { blob, fileType, fileName: file.fileName }
      docPreviewVisible.value = true
    } else {
      // xlsx 等触发下载
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = file.fileName || 'file'
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    }
  } catch {
    ElMessage.error('预览失败，请稍后重试')
  }
}

function onDocPreviewClosed() {
  docPreviewFile.value = null
}

function closeImgPreview() {
  imgPreviewVisible.value = false
  for (const u of imgPreviewUrls.value) URL.revokeObjectURL(u)
  imgPreviewUrls.value = []
}

async function onDelete(row) {
  await ElMessageBox.confirm(`确认删除「${row.fileName}」？`, '提示', { type: 'warning' })
  await deleteKnowledgeFile(row.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<style scoped>
.kf-head { display: flex; gap: 8px; margin-bottom: 8px; }
.kf-tip { color: var(--app-text-3, #909399); font-size: 12px; padding: 4px 0; }
.kf-doc-body { height: 72vh; }
</style>
