namespace Lx.Probe.Remote;

internal static class MobilePage
{
    public const string Html = """
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="theme-color" content="#0f1115">
<title>Lx 手机遥控</title>
<style>
:root{color-scheme:dark;--bg:#0f1115;--panel:#171a21;--line:#2a303b;--text:#e8edf5;--muted:#8d98a8;--accent:#4f8cff;--ok:#3fbf7f;--warn:#e0a43b;--err:#e06666}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);font:14px/1.55 -apple-system,BlinkMacSystemFont,"Segoe UI","Microsoft YaHei",sans-serif;-webkit-tap-highlight-color:transparent}
header{position:sticky;top:0;z-index:2;background:rgba(15,17,21,.94);backdrop-filter:blur(12px);border-bottom:1px solid var(--line);padding:14px 16px;display:flex;align-items:center;justify-content:space-between}
h1{font-size:17px;margin:0}
.badge{font-size:11px;color:var(--muted);border:1px solid var(--line);border-radius:999px;padding:3px 9px}
main{padding:14px;max-width:820px;margin:0 auto;display:grid;gap:14px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:14px}
.card h2{font-size:13px;color:var(--muted);font-weight:600;margin:0 0 10px;letter-spacing:.04em}
.status-row{display:flex;align-items:center;gap:9px;flex-wrap:wrap}
.dot{width:9px;height:9px;border-radius:50%;background:var(--warn)}
.dot.ok{background:var(--ok)}.dot.err{background:var(--err)}
.muted{color:var(--muted)}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(230px,1fr));gap:10px}
.instance{border:1px solid var(--line);border-radius:12px;padding:12px;background:#12151b}
.instance strong{display:block;font-size:15px;margin-bottom:4px}
.meta{font-size:12px;color:var(--muted);line-height:1.5}
button{border:0;border-radius:10px;padding:9px 13px;font:inherit;background:var(--accent);color:#fff;font-weight:600}
button.secondary{background:#262d39;color:var(--text)}
button:disabled{opacity:.45}
.instance button{width:100%;margin-top:10px}
input{width:100%;border:1px solid var(--line);background:#0c0e12;color:var(--text);border-radius:10px;padding:11px;font:inherit}
pre{white-space:pre-wrap;word-break:break-word;background:#0b0d11;border:1px solid var(--line);border-radius:10px;padding:10px;max-height:280px;overflow:auto;font-size:12px;color:#b9c4d4;margin:0}
.row{display:flex;gap:8px;flex-wrap:wrap;align-items:center}
.notice{font-size:12px;color:var(--muted)}
#login{max-width:440px;margin:10vh auto;padding:16px}
#login .card{display:grid;gap:10px}
[hidden]{display:none!important}
</style>
</head>
<body>
<header>
  <h1>Lx 手机遥控</h1>
  <span id="conn" class="badge">未连接</span>
</header>

<section id="login">
  <div class="card">
    <h2>连接到电脑</h2>
    <p class="notice">电脑上的启动器要打开「手机遥控」，并保持在同一局域网。扫描/输入电脑上显示的地址即可。</p>
    <input id="token" placeholder="配对 token" autocomplete="off">
    <div class="row">
      <button id="connectBtn">连接</button>
      <button id="clearBtn" class="secondary">清除</button>
    </div>
    <p id="loginMsg" class="notice"></p>
  </div>
</section>

<main id="app" hidden>
  <div class="card">
    <h2>状态</h2>
    <div class="status-row">
      <span id="stateDot" class="dot"></span>
      <strong id="stateText">读取中…</strong>
      <span id="gameText" class="muted"></span>
    </div>
    <p id="statusDetail" class="notice"></p>
  </div>

  <div class="card">
    <h2>实例</h2>
    <div id="instances" class="grid"></div>
    <p id="instanceEmpty" class="notice" hidden>还没有实例。请先在电脑上创建实例。</p>
  </div>

  <div class="card">
    <h2>日志</h2>
    <div class="row" style="margin-bottom:8px">
      <button id="logBtn" class="secondary">刷新日志</button>
      <span id="logTime" class="notice"></span>
    </div>
    <pre id="logs">等待连接…</pre>
  </div>

  <p class="notice">远程控制只在局域网内生效；不要把这个地址转发到公网。</p>
</main>

<script>
const $ = (id) => document.getElementById(id);
let token = localStorage.getItem('lx_remote_token') || new URLSearchParams(location.search).get('token') || '';
let timer = null;
let logsTimer = null;

const esc = (value) => String(value ?? '').replace(/[&<>"']/g, (ch) => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
}[ch]));

async function api(path, options = {}) {
  const headers = Object.assign({}, options.headers || {}, { 'X-Lx-Token': token });
  if (options.body) headers['Content-Type'] = 'application/json';
  const response = await fetch(path, Object.assign({}, options, { headers }));
  if (response.status === 401) throw new Error('token 无效，请重新配对');
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.error || ('HTTP ' + response.status));
  return data;
}

function showLogin(message = '') {
  localStorage.removeItem('lx_remote_token');
  token = '';
  $('app').hidden = true;
  $('login').hidden = false;
  $('loginMsg').textContent = message;
  $('conn').textContent = '未连接';
  $('conn').className = 'badge';
  clearInterval(timer); clearInterval(logsTimer);
}

async function connect() {
  token = $('token').value.trim() || token;
  if (!token) { $('loginMsg').textContent = '请输入电脑上显示的 token'; return; }
  try {
    const ping = await api('/api/ping');
    localStorage.setItem('lx_remote_token', token);
    $('login').hidden = true;
    $('app').hidden = false;
    $('conn').textContent = ping.product + ' ' + ping.version;
    $('conn').className = 'badge';
    await refresh();
    await loadLogs();
    clearInterval(timer); clearInterval(logsTimer);
    timer = setInterval(refresh, 3000);
    logsTimer = setInterval(loadLogs, 5000);
  } catch (error) {
    $('loginMsg').textContent = error.message;
    showLogin(error.message);
  }
}

async function refresh() {
  try {
    const snapshot = await api('/api/snapshot');
    const running = !!snapshot.gameRunning;
    const launching = !!snapshot.launching;
    $('stateDot').className = 'dot ' + (running ? 'ok' : launching ? 'warn' : '');
    $('stateText').textContent = running ? '游戏运行中' : launching ? '正在启动…' : '空闲';
    $('gameText').textContent = snapshot.status || '';
    $('statusDetail').textContent = running
      ? '可以从手机继续查看日志；关闭游戏请直接在电脑上操作。'
      : launching ? '电脑正在准备资源，请稍候。' : '';

    const box = $('instances');
    box.innerHTML = '';
    const list = snapshot.instances || [];
    $('instanceEmpty').hidden = list.length > 0;
    for (const item of list) {
      const card = document.createElement('div');
      card.className = 'instance';
      card.innerHTML =
        '<strong>' + esc(item.name) + '</strong>' +
        '<div class="meta">版本：' + esc(item.versionId) + '</div>' +
        '<div class="meta">加载器：' + esc(item.loader || '原版') +
          (item.loaderVersion ? ' / ' + esc(item.loaderVersion) : '') + '</div>' +
        '<div class="meta">账号：' + esc(item.accountName || '未绑定') + '</div>';
      const button = document.createElement('button');
      button.textContent = running || launching ? '正在运行' : '启动游戏';
      button.disabled = running || launching;
      button.onclick = () => launch(item.name, button);
      card.appendChild(button);
      box.appendChild(card);
    }
  } catch (error) {
    $('stateDot').className = 'dot err';
    $('stateText').textContent = '连接失败';
    $('statusDetail').textContent = error.message;
    if (error.message.includes('token')) showLogin(error.message);
  }
}

async function launch(name, button) {
  if (!confirm('在电脑上启动「' + name + '」？')) return;
  button.disabled = true;
  button.textContent = '正在请求…';
  try {
    const result = await api('/api/launch', { method: 'POST', body: JSON.stringify({ name }) });
    $('statusDetail').textContent = result.message || '已发送启动请求';
    setTimeout(refresh, 800);
  } catch (error) {
    $('statusDetail').textContent = error.message;
    button.disabled = false;
    button.textContent = '启动游戏';
  }
}

async function loadLogs() {
  try {
    const data = await api('/api/logs?lines=200');
    $('logs').textContent = data.text || '(暂无日志)';
    $('logTime').textContent = '更新于 ' + new Date().toLocaleTimeString();
  } catch (error) {
    $('logs').textContent = error.message;
  }
}

$('connectBtn').onclick = connect;
$('token').addEventListener('keydown', (event) => { if (event.key === 'Enter') connect(); });
$('clearBtn').onclick = () => { $('token').value = ''; showLogin(''); };
$('logBtn').onclick = loadLogs;

if (token) {
  $('token').value = token;
  connect();
} else {
  showLogin();
}
</script>
</body>
</html>
""";
}
