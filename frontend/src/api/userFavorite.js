import http from './http'

export const getFavorites = () => http.get('/userfavorite')
export const toggleFavorite = (data) => http.post('/userfavorite/toggle', data)
