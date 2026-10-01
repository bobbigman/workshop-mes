// 显式扩展名优先，避免历史类型字段（如 MIME/Word）让 docx 误走下载。
export function getKnowledgeFileType(file) {
  const type = String(file.fileType || '').trim().toLowerCase().replace(/^\./, '')
  const extension = String(file.fileName || '').trim().match(/\.([^.]+)$/)?.[1].toLowerCase()
  const supported = ['pdf', 'docx', 'xlsx', 'jpg', 'jpeg', 'png', 'webp']
  if (supported.includes(extension)) return extension
  const mimeTypes = {
    'application/pdf': 'pdf',
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document': 'docx',
    'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': 'xlsx',
    'image/jpeg': 'jpg', 'image/png': 'png', 'image/webp': 'webp'
  }
  return mimeTypes[type] || type || extension || ''
}
