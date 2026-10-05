import http from './http'

// 【业务背景】班组长派工（docs/29/201/202）：工序任务派给执行人（可多人）；工人看我的任务
export const myTasks = () => http.get('/Assign/my-tasks')
export const assignWorkers = () => http.get('/Assign/workers')
/** @param {number|null|number[]} userIdOrIds 单人 / 多人 / null|[] 取消 */
export const assign = (taskId, userIdOrIds) => {
  if (Array.isArray(userIdOrIds)) {
    return http.put(`/Assign/${taskId}`, { userIds: userIdOrIds })
  }
  if (userIdOrIds == null || userIdOrIds === '') {
    return http.put(`/Assign/${taskId}`, { userIds: [] })
  }
  return http.put(`/Assign/${taskId}`, { userId: userIdOrIds })
}
