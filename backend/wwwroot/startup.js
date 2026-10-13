// 使用普通脚本和旧语法：模块本身加载/解析失败时也能显示诊断，不依赖 Vue。
(function () {
  var panel = document.getElementById('startup-status');
  var message = document.getElementById('startup-message');
  var detail = document.getElementById('startup-detail');
  var retry = document.getElementById('startup-retry');
  var finished = false;
  var failed = false;
  function show(code) {
    if (finished) return;
    failed = true;
    message.textContent = '页面未能启动，请将下面的提示反馈给管理员';
    detail.textContent = code;
    retry.hidden = false;
  }
  function brief(err) {
    var name = (err && err.name) || 'Error';
    var msg = err && err.message ? String(err.message) : '';
    msg = msg.replace(/https?:\/\/\S+/g, '').replace(/\s+/g, ' ').trim();
    if (msg.length > 60) msg = msg.slice(0, 60);
    return msg ? (name + '：' + msg) : name;
  }
  function onError(event) {
    var target = event.target;
    if (target && target.tagName === 'SCRIPT') {
      show('启动脚本加载失败（BOOT-RESOURCE）');
    } else if (event.message) {
      // 只留异常类型和短句，去掉网址，避免把接口地址摊在页面上。
      show('浏览器执行失败（BOOT-SCRIPT / ' + brief(event.error || { name: 'Error', message: event.message }) + '）');
    }
  }
  function onRejection(event) {
    show('页面初始化失败（BOOT-ASYNC / ' + brief(event.reason) + '）');
  }
  retry.onclick = function () {
    var url = new URL(window.location.href);
    url.searchParams.set('startup-retry', String(Date.now()));
    window.location.replace(url.href);
  };
  window.addEventListener('error', onError, true);
  window.addEventListener('unhandledrejection', onRejection);
  var timer = window.setTimeout(function () {
    if (!failed) show('页面加载超过 20 秒（BOOT-TIMEOUT）');
  }, 20000);
  window.addEventListener('mes:ready', function () {
    finished = true;
    window.clearTimeout(timer);
    window.removeEventListener('error', onError, true);
    window.removeEventListener('unhandledrejection', onRejection);
    if (panel.parentNode) panel.parentNode.removeChild(panel);
  });
})();
