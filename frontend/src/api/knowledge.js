import http from './http'

// 【业务背景】知识库文件（作业指导书/图纸）：PC 管理 + H5 预览（docs/66）
export const getKnowledgeFileList = (refType, refId) =>
  http.get('/KnowledgeFile', { params: { refType, refId } })

export const getKnowledgeFileByOrder = (orderId) =>
  http.get(`/KnowledgeFile/by-order/${orderId}`)

export const getKnowledgeFileRaw = (fileId) =>
  http.get(`/KnowledgeFile/${fileId}/raw`, { responseType: 'blob' })

export const uploadKnowledgeFile = (formData) =>
  http.post('/KnowledgeFile', formData)

export const deleteKnowledgeFile = (fileId) =>
  http.delete(`/KnowledgeFile/${fileId}`)
