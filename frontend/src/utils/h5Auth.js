/** H5 登录态键（与 login/index.vue 写入一致；不清扫码偏好等其它键） */
const LOGIN_KEYS = [
  'token',
  'userName',
  'role',
  'instanceName',
  'factoryName',
  'factoryCode',
  'licenseTier',
  'licenseStatus',
  'licenseMessage'
]

export function clearLoginState() {
  for (const k of LOGIN_KEYS) {
    try {
      localStorage.removeItem(k)
    } catch (err) {
      throw new Error('清除登录信息失败：' + (err?.message || '本地存储不可用'))
    }
  }
}
