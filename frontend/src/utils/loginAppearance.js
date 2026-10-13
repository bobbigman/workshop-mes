// 仅记住已成功登录的工厂代码，不保存身份凭证或匿名工厂名单。
export const LOGIN_FACTORY_KEY = 'loginAppearance.factoryCode'

export function resolveLoginFactory(queryCode, instanceCode, storage = localStorage) {
  const explicit = typeof queryCode === 'string' ? queryCode.trim() : ''
  return explicit || storage.getItem(LOGIN_FACTORY_KEY) || storage.getItem('factoryCode') || instanceCode || ''
}

export function rememberLoginFactory(code, storage = localStorage) {
  if (code) storage.setItem(LOGIN_FACTORY_KEY, String(code).trim())
}

/** 上传前缩小并转 WebP；原图只保留在用户电脑，服务器保存轻量版本。 */
export async function compressLoginBanner(file) {
  if (!/^image\/(png|jpeg|webp)$/.test(file.type)) throw new Error('请选择 PNG、JPG 或 WebP 图片')
  if (file.size > 5 * 1024 * 1024) throw new Error('图片不能超过 5MB')
  const url = URL.createObjectURL(file)
  try {
    const image = new Image()
    await new Promise((resolve, reject) => {
      image.onload = resolve
      image.onerror = () => reject(new Error('登录页图片处理失败：无法读取图片内容，请重新选择'))
      image.src = url
    })
    const scale = Math.min(1, 1920 / Math.max(image.naturalWidth, image.naturalHeight))
    const canvas = document.createElement('canvas')
    canvas.width = Math.round(image.naturalWidth * scale)
    canvas.height = Math.round(image.naturalHeight * scale)
    const context = canvas.getContext('2d')
    if (!context) throw new Error('登录页图片处理失败：浏览器不支持图片压缩')
    context.drawImage(image, 0, 0, canvas.width, canvas.height)
    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/webp', 0.8))
    if (!blob) throw new Error('登录页图片处理失败：压缩结果为空')
    // 不支持 WebP 的浏览器可能返回 PNG，扩展名必须与实际格式一致。
    if (blob.size >= file.size) return file
    return new File([blob], blob.type === 'image/webp' ? 'login-banner.webp' : 'login-banner.png', { type: blob.type })
  } finally {
    URL.revokeObjectURL(url)
  }
}
