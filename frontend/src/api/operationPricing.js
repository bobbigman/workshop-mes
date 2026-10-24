import request from './http'

export const getProcessingConfig = id => request.get(`/operation-pricing/${id}`)
export const saveProcessingConfig = (id, config) => request.put(`/operation-pricing/${id}`, config)
export const getProductProcessing = id => request.get(`/operation-pricing/product/${id}`)
export const previewProcessing = (id, productId, choice) => request.post(`/operation-pricing/${id}/preview`, choice, { params: { productId } })

export const getPricingMigrationStatus = () => request.get('/operation-pricing/migration-status')
