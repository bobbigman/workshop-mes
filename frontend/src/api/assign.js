import http from './http'

// 【业务背景】班组长派工（docs/29）：工序任务派给执行人；工人看我的任务
export const myTasks = () => http.get('/Assign/my-tasks')
export const assign = (taskId, userId) => http.put(`/Assign/${taskId}`, { userId })
