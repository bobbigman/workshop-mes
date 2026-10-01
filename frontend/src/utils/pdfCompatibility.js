// PDF.js legacy 仍依赖此接口；页面与 worker 是独立环境，均需在加载 PDF.js 前补齐。
if (typeof Promise.withResolvers !== 'function') {
  Object.defineProperty(Promise, 'withResolvers', {
    configurable: true,
    writable: true,
    value: function withResolvers() {
      let resolve, reject
      const promise = new this((yes, no) => { resolve = yes; reject = no })
      return { promise, resolve, reject }
    }
  })
}
