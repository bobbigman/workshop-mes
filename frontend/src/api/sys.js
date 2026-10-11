import http from './http'

// 【业务背景】用户 / 部门
export const getUserList = (params) => http.get('/User', { params })
export const createUser = (data) => http.post('/User', data)
export const updateUser = (id, data) => http.put(`/User/${id}`, data)
export const deleteUser = (id) => http.delete(`/User/${id}`)
/** 停用/启用（docs/219）：status 0停用 1启用 */
export const setUserStatus = (id, status) => http.put(`/User/${id}/status`, { status })
/** 个人免密二维码：获取或首次生成（docs/150） */
export const getUserLoginQr = (id) => http.get(`/User/${id}/login-qr`)
/** 重新生成个人免密码，旧码立刻失效 */
export const regenerateUserLoginQr = (id) => http.post(`/User/${id}/login-qr/regenerate`)

export const getDeptList = (params) => http.get('/Department', { params })
export const getDept = (id) => http.get(`/Department/${id}`)
export const createDept = (data) => http.post('/Department', data)
export const updateDept = (id, data) => http.put(`/Department/${id}`, data)
export const deleteDept = (id) => http.delete(`/Department/${id}`)
