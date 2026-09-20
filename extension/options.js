const DEFAULT_PORT = 8975;

const tokenInput = document.getElementById('token');
const portInput = document.getElementById('port');
const saveButton = document.getElementById('save');
const status = document.getElementById('status');

function show(message, ok) {
  status.textContent = message;
  status.className = ok ? 'ok' : 'bad';
  if (ok) setTimeout(() => { status.textContent = ''; status.className = ''; }, 2500);
}

async function load() {
  const stored = await chrome.storage.local.get(['token', 'port']);
  tokenInput.value = stored.token || '';
  portInput.value = stored.port || DEFAULT_PORT;
}

saveButton.addEventListener('click', async () => {
  const token = tokenInput.value.trim();
  const port = Number(portInput.value) || DEFAULT_PORT;

  if (!token) {
    show('Enter the token from Macro Deck.', false);
    return;
  }

  if (port < 1024 || port > 65535) {
    show('Port must be between 1024 and 65535.', false);
    return;
  }

  // The service worker watches storage and reconnects as soon as this lands.
  await chrome.storage.local.set({ token, port });
  show('Saved. Reconnecting.', true);
});

load();
