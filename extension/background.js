/*
 * Owns the connection to the Macro Deck plugin.
 *
 * Responsibilities:
 *   - hold the WebSocket, authenticate, reconnect with backoff
 *   - keep the Manifest V3 service worker alive while connected
 *   - decide which YouTube Music tab is authoritative
 *   - relay state upward and commands downward
 *
 * It knows nothing about the YouTube Music DOM.
 */

const LOG = '[MacroDeck YTM]';
const EXTENSION_VERSION = chrome.runtime.getManifest().version;

const DEFAULT_PORT = 8975;
const HANDSHAKE_VERSION = '1.0.0';
const PING_INTERVAL_MS = 20000;
const BACKOFF_START_MS = 1000;
const BACKOFF_MAX_MS = 10000;
// A rejected token is a configuration error, not a transient fault. Retrying
// every second would spam the log for as long as the user leaves it wrong.
const BACKOFF_REJECTED_MS = 30000;

let socket = null;
let authenticated = false;
let backoffMs = BACKOFF_START_MS;
let reconnectTimer = null;
let pingTimer = null;
let lastSentState = null;

/** tabId -> { playing: boolean, lastActive: number } */
const tabs = new Map();

/* ------------------------------------------------------------------ tabs */

function authoritativeTabId() {
  let best = null;
  let bestScore = null;

  for (const [tabId, info] of tabs) {
    // A playing tab always outranks a silent one; among equals, the most
    // recently activated wins.
    const score = [info.playing ? 1 : 0, info.lastActive];
    if (bestScore === null || score[0] > bestScore[0] ||
        (score[0] === bestScore[0] && score[1] > bestScore[1])) {
      best = tabId;
      bestScore = score;
    }
  }

  return best;
}

function currentState() {
  const tabId = authoritativeTabId();
  const connected = tabs.size > 0;
  const playing = connected && tabId !== null ? tabs.get(tabId).playing === true : false;
  return { connected, playing };
}

function stateChanged(state) {
  return !lastSentState ||
    lastSentState.connected !== state.connected ||
    lastSentState.playing !== state.playing;
}

function pushState(force = false) {
  if (!authenticated || !socket || socket.readyState !== WebSocket.OPEN) return;

  const state = currentState();
  if (!force && !stateChanged(state)) return;

  lastSentState = state;
  send({ type: 'state', connected: state.connected, playing: state.playing });
}

/* ------------------------------------------------------------ connection */

function send(message) {
  if (!socket || socket.readyState !== WebSocket.OPEN) return;
  try {
    socket.send(JSON.stringify(message));
  } catch (error) {
    console.warn(LOG, 'send failed', error);
  }
}

async function settings() {
  const stored = await chrome.storage.local.get(['port', 'token']);
  return {
    port: Number(stored.port) || DEFAULT_PORT,
    token: typeof stored.token === 'string' ? stored.token.trim() : '',
  };
}

function scheduleReconnect(delayMs) {
  clearTimeout(reconnectTimer);
  reconnectTimer = setTimeout(() => connect(), delayMs);
}

function teardown() {
  authenticated = false;
  lastSentState = null;
  clearInterval(pingTimer);
  pingTimer = null;

  if (socket) {
    socket.onopen = socket.onmessage = socket.onclose = socket.onerror = null;
    try {
      socket.close();
    } catch { /* already closing */ }
    socket = null;
  }
}

async function connect() {
  clearTimeout(reconnectTimer);

  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
    return;
  }

  const { port, token } = await settings();

  if (!token) {
    // Nothing to connect with. The storage listener wakes this up again as
    // soon as the token is saved on the options page.
    console.info(LOG, 'no token configured; open the extension options');
    return;
  }

  teardown();

  const url = `ws://127.0.0.1:${port}`;
  console.debug(LOG, 'connecting to', url);

  try {
    socket = new WebSocket(url);
  } catch (error) {
    console.warn(LOG, 'could not open socket', error);
    scheduleReconnect(backoffMs);
    backoffMs = Math.min(backoffMs * 2, BACKOFF_MAX_MS);
    return;
  }

  socket.onopen = () => {
    send({ type: 'hello', token, version: HANDSHAKE_VERSION, extension: EXTENSION_VERSION });
  };

  socket.onmessage = (event) => {
    let message;
    try {
      message = JSON.parse(event.data);
    } catch {
      console.warn(LOG, 'unparseable frame', event.data);
      return;
    }
    handle(message);
  };

  socket.onerror = () => {
    // onclose always follows; reconnect logic lives there.
    console.debug(LOG, 'socket error');
  };

  socket.onclose = (event) => {
    const wasRejected = event.code === 1008;
    console.info(LOG, 'disconnected', event.code, event.reason || '');
    teardown();
    const delay = wasRejected ? BACKOFF_REJECTED_MS : backoffMs;
    if (!wasRejected) backoffMs = Math.min(backoffMs * 2, BACKOFF_MAX_MS);
    scheduleReconnect(delay);
  };
}

function handle(message) {
  switch (message?.type) {
    case 'welcome':
      authenticated = true;
      backoffMs = BACKOFF_START_MS;
      console.info(LOG, 'connected to Macro Deck plugin', message.version || '');
      startPing();
      pushState(true);
      break;

    case 'error':
      console.warn(LOG, 'plugin refused the connection:', message.reason);
      break;

    case 'command':
      dispatchCommand(message.command);
      break;

    case 'ping':
      send({ type: 'pong' });
      break;

    case 'pong':
      break;

    default:
      // Forward compatibility: ignore anything we do not understand.
      break;
  }
}

function startPing() {
  clearInterval(pingTimer);
  // The ping matters less as a liveness check than as the thing that keeps
  // Chrome from terminating this service worker for inactivity.
  pingTimer = setInterval(() => send({ type: 'ping' }), PING_INTERVAL_MS);
}

async function dispatchCommand(command) {
  const tabId = authoritativeTabId();
  if (tabId === null) {
    console.info(LOG, 'command ignored, no YouTube Music tab:', command);
    return;
  }

  try {
    await chrome.tabs.sendMessage(tabId, { type: 'command', command });
  } catch (error) {
    // The tab went away between registration and dispatch.
    console.warn(LOG, 'command delivery failed', error);
    tabs.delete(tabId);
    pushState();
  }
}

/* -------------------------------------------------------------- listeners */

chrome.runtime.onMessage.addListener((message, sender) => {
  if (message?.type !== 'tab_state' || !sender.tab) return false;

  const tabId = sender.tab.id;
  const existing = tabs.get(tabId);
  tabs.set(tabId, {
    playing: message.playing === true,
    lastActive: existing ? existing.lastActive : Date.now(),
  });

  pushState();
  connectIfIdle();
  return false;
});

chrome.tabs.onActivated.addListener(({ tabId }) => {
  const info = tabs.get(tabId);
  if (!info) return;
  info.lastActive = Date.now();
  pushState();
});

chrome.tabs.onRemoved.addListener((tabId) => {
  if (tabs.delete(tabId)) pushState();
});

chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== 'local') return;
  if (!('token' in changes) && !('port' in changes)) return;
  console.info(LOG, 'settings changed, reconnecting');
  backoffMs = BACKOFF_START_MS;
  teardown();
  connect();
});

chrome.runtime.onStartup.addListener(() => connect());
chrome.runtime.onInstalled.addListener(() => connect());

// Wakes the worker if Chrome terminated it despite the ping, and re-establishes
// the socket. One minute is the shortest period alarms allow.
chrome.alarms.create('keepalive', { periodInMinutes: 1 });
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === 'keepalive') connectIfIdle();
});

function connectIfIdle() {
  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) return;
  connect();
}

connect();
