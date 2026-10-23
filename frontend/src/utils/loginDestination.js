/** 只用于登录成功后的默认入口，不拦截用户主动访问的业务页面。 */
export function isMobileDevice(device = globalThis.navigator) {
  return /Android|iPhone|iPad|iPod|Windows Phone|Windows CE|Windows Mobile|Mobile/i.test(device?.userAgent || '')
    || (/Macintosh/i.test(device?.userAgent || '') && device?.maxTouchPoints > 1)
}

export function loginDestination(redirect, role, device = globalThis.navigator) {
  if (typeof redirect === 'string' && /^\/h5\/[a-z-]+(?:[?#]|$)/.test(redirect)
    && !/[\\\u0000-\u001f]/.test(redirect)) return redirect
  return isMobileDevice(device) || Number(role) === 2 ? '/h5/home' : '/home'
}
