import http from './http'

/** PC 常见问题（系统能力50问，docs/113） */
export const getFaq = () => http.get('/SupportDocs/faq')
