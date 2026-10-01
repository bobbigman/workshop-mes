import http from './http'

// 【业务背景】用户 / 部门
export const getUserList = (params) => http.get('/User', { params })
export const createUser = (data) => http.post('/User', data)
export const updateUser = (id, data) => http.put(`/User/${id}`, data)
export const deleteUser = (id) => http.delete(`/User/${id}`)

export const getDeptList = (params) => http.get('/Department', { params })
export const getDept = (id) => http.get(`/Department/${id}`)
export const createDept = (data) => http.post('/Department', data)
export const updateDept = (id, data) => http.put(`/Department/${id}`, data)
export const deleteDept = (id) => http.delete(`/Department/${id}`)
