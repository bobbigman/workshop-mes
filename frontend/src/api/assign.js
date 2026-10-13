import http from './http'

// 【业务背景】班组长派工（docs/29/201/202/214）：工序可派个人（多人）或一个班组；工人任务=派给我的∪本班组
export const myTasks = () => http.get('/Assign/my-tasks')
export const assignWorkers = () => http.get('/Assign/workers')
export const assignDepartments = (taskId) => http.get(`/Assign/departments/${taskId}`)
export const assignSuggest = (taskId) => http.get(`/Assign/suggest/${taskId}`)

/**
 * @param {number} taskId
 * @param {number|null|number[]} [userIdOrIds] 单人 / 多人 / null|[] 取消（未派班组时）
 * @param {number|null} [departmentId] 派班组；有值则忽略人
 */
export const assign = (taskId, userIdOrIds, departmentId) => {
  if (departmentId != null && departmentId !== '') {
    return http.put(`/Assign/${taskId}`, { departmentId: Number(departmentId), userIds: [] })
  }
  if (Array.isArray(userIdOrIds)) {
    return http.put(`/Assign/${taskId}`, { userIds: userIdOrIds })
  }
  if (userIdOrIds == null || userIdOrIds === '') {
    return http.put(`/Assign/${taskId}`, { userIds: [] })
  }
  return http.put(`/Assign/${taskId}`, { userId: userIdOrIds })
}
