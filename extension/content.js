/*
 * Runs on music.youtube.com. Watches the real media element and reports what it
 * observes to the service worker; executes commands the worker forwards.
 *
 * It never assumes a command succeeded. A command acts on the page, and the
 * resulting media event is what gets reported.
 */

(() => {
  const LOG = '[MacroDeck YTM]';

  let attachedTo = null;
  let lastReported = null;

  const MEDIA_EVENTS = ['play', 'playing', 'pause', 'ended', 'emptied', 'loadedmetadata'];

  function report(force = false) {
    const playing = YTMPlayer.isPlaying();
    if (!force && playing === lastReported) return;
    lastReported = playing;
    try {
      chrome.runtime.sendMessage({ type: 'tab_state', playing });
    } catch (error) {
      // The worker may be restarting. The next event resends, and the worker
      // asks every tab to report when it comes back up.
      console.debug(LOG, 'state not delivered', error);
    }
  }

  function onMediaEvent() {
    report();
  }

  function attach() {
    const video = YTMPlayer.media();
    if (!video || video === attachedTo) return;

    if (attachedTo) {
      for (const name of MEDIA_EVENTS) attachedTo.removeEventListener(name, onMediaEvent);
    }

    attachedTo = video;
    for (const name of MEDIA_EVENTS) video.addEventListener(name, onMediaEvent);
    console.debug(LOG, 'attached to media element');
    report(true);
  }

  // The player element is created after page load, and replaced on some
  // navigations, so keep looking rather than assuming one attach is enough.
  const observer = new MutationObserver(() => attach());
  observer.observe(document.documentElement, { childList: true, subtree: true });

  // Safety net for events the observer misses. The forced report also
  // re-registers this tab after the service worker has been restarted, since a
  // restarted worker has an empty tab registry and no way to enumerate tabs.
  setInterval(() => {
    attach();
    report(true);
  }, 2000);

  window.addEventListener('yt-navigate-finish', () => attach());

  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (message?.type === 'command') {
      Promise.resolve(runCommand(message.command)).then((accepted) => {
        // Report immediately so a rejected play (autoplay policy, no track
        // loaded) does not leave Macro Deck showing the wrong state.
        report(true);
        sendResponse({ accepted });
      });
      return true;
    }

    if (message?.type === 'report_now') {
      report(true);
      sendResponse({ playing: lastReported === true });
      return false;
    }

    return false;
  });

  function runCommand(command) {
    switch (command) {
      case 'play': return YTMPlayer.play();
      case 'pause': return YTMPlayer.pause();
      case 'play_pause': return YTMPlayer.playPause();
      case 'next': return YTMPlayer.next();
      case 'previous': return YTMPlayer.previous();
      default:
        console.warn(LOG, 'unknown command', command);
        return false;
    }
  }

  attach();
  report(true);
  console.debug(LOG, 'content script ready');
})();
