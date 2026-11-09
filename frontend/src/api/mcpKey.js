import http from './http'

/** 本厂当前钥匙状态（每厂一把） */
export const getMcpKeyStatus = () => http.get('/McpKey')
/** 查看完整连接报文（须再输当前账号登录密码） */
export const getMcpKeyConnection = (password) => http.post('/McpKey/connection', { password })
export const createMcpKey = () => http.post('/McpKey')
export const revokeMcpKey = () => http.post('/McpKey/revoke')
