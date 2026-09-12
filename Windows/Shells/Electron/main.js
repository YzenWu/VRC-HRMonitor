// Minimal HRM Electron shell: loads the local backend Web UI.
// Shares the same Vue frontend URL as the WebView2 shell and contains only a BrowserWindow, with no application logic.
// Usage: npm install (first run; downloads Electron) -> npm start
const { app, BrowserWindow } = require('electron');
const net = require('net');

const PORT = Number(process.env.HRM_PORT || 8228);
const URL = process.env.HRM_URL || `http://127.0.0.1:${PORT}/webui/`;
const BACKEND_EXE = 'HeartRateMonitor.exe'; // Expected beside the packaged artifacts, but not enforced during scaffolding

function probe(timeoutMs = 1200) {
  return new Promise((resolve) => {
    const sock = net.connect(PORT, '127.0.0.1');
    sock.setTimeout(timeoutMs);
    sock.on('connect', () => { sock.destroy(); resolve(true); });
    sock.on('error', () => resolve(false));
    sock.on('timeout', () => { sock.destroy(); resolve(false); });
  });
}

async function waitBackend(maxMs) {
  const t0 = Date.now();
  while (Date.now() - t0 < maxMs) {
    if (await probe(600)) return true;
    await new Promise((r) => setTimeout(r, 400));
  }
  return probe(600);
}

function errorPage() {
  const body = encodeURIComponent(
    `<body style="font-family:system-ui;background:#111318;color:#ddd;display:grid;place-items:center;height:100vh;margin:0">
       <div><h2>后端 Web 服务未运行</h2>
       <p>请先启动 HeartRateMonitor（--web），再刷新本窗口。</p>
       <p><code>http://127.0.0.1:${PORT}/webui/</code></p></div></body>`);
  return `data:text/html;charset=utf-8,${body}`;
}

app.whenReady().then(async () => {
  const win = new BrowserWindow({
    width: 1280,
    height: 820,
    minWidth: 900,
    minHeight: 600,
    title: 'HeartRateMonitor',
    backgroundColor: '#111318',
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  // Scaffolding phase: only probe and wait (start the backend with --web first); launch/package integration is completed in Phase A4.
  await waitBackend(3000);

  win.webContents.on('did-fail-load', (_e, code, _desc, url) => {
    if (code === -3) return; // Ignore ERR_ABORTED (navigation interrupted)
    win.loadURL(errorPage()); // eslint-disable-line no-underscore-dangle
    void url;
  });

  await win.loadURL(URL);
});

app.on('window-all-closed', () => app.quit());
