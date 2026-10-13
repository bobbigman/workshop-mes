import http from './http'

export const getPendingReviews = (params) => http.get('/Review/pending', { params })
export const getReviewOrderOptions = () => http.get('/Review/order-options')
export const approveReview = (id) => http.post(`/Review/${id}/approve`)
export const rejectReview = (id, data) => http.post(`/Review/${id}/reject`, data)
export const batchApproveReview = (data) => http.post('/Review/batch-approve', data)
