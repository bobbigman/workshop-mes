/** 页面请求归属于发起时的登录身份和请求代次；过期结果不得影响新页面。 */
export function createAiIdentityBoundary(readIdentity, onChanged) {
  let identity = readIdentity(), epoch = 0, disposed = false
  const requests = new Map()
  function check() {
    const next = readIdentity()
    if (identity !== next) { identity = next; epoch++; onChanged(); return false }
    return !disposed
  }
  return {
    check,
    begin(channel = 'main') {
      if (!check()) return { identity: null, epoch: -1 }
      const serial = (requests.get(channel) || 0) + 1
      requests.set(channel, serial)
      return { identity, epoch, channel, serial }
    },
    current(ticket) { return check() && ticket.identity === identity && ticket.epoch === epoch && requests.get(ticket.channel) === ticket.serial },
    invalidate() { epoch++ },
    dispose() { disposed = true; epoch++ }
  }
}
