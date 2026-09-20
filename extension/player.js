/*
 * Everything that knows about YouTube Music's page structure lives here.
 *
 * Play and pause act on the media element, which is stable. Next and previous
 * have no media-element equivalent and must click the player controls, so those
 * selectors are the only fragile part of the extension. When YouTube Music
 * changes its markup, this file is the only one that needs editing.
 *
 * Loaded as a plain content script, so it publishes one global into the
 * isolated world rather than exporting a module.
 */

const YTMPlayer = (() => {
  // Ordered by preference. The first selector that matches a visible element wins.
  const NEXT_SELECTORS = [
    'ytmusic-player-bar tp-yt-paper-icon-button.next-button',
    'ytmusic-player-bar .next-button',
    '.ytmusic-player-bar .next-button',
  ];

  const PREVIOUS_SELECTORS = [
    'ytmusic-player-bar tp-yt-paper-icon-button.previous-button',
    'ytmusic-player-bar .previous-button',
    '.ytmusic-player-bar .previous-button',
  ];

  /** The page's single media element, or null while the player is still loading. */
  function media() {
    const candidates = document.querySelectorAll('video');
    for (const video of candidates) {
      // YouTube Music keeps one video element for audio playback. Anything with
      // no source attached yet is not it.
      if (video.src || video.currentSrc || video.readyState > 0) return video;
    }
    return candidates[0] || null;
  }

  /** True when the player is actually producing sound, not merely loaded. */
  function isPlaying() {
    const video = media();
    if (!video) return false;
    return !video.paused && !video.ended;
  }

  function firstMatch(selectors) {
    for (const selector of selectors) {
      const element = document.querySelector(selector);
      if (element && element.offsetParent !== null) return element;
    }
    // Fall back to a hidden-but-present control rather than doing nothing.
    for (const selector of selectors) {
      const element = document.querySelector(selector);
      if (element) return element;
    }
    return null;
  }

  async function play() {
    const video = media();
    if (!video) return false;
    try {
      await video.play();
      return true;
    } catch (error) {
      console.warn('[MacroDeck YTM] play() rejected', error);
      return false;
    }
  }

  function pause() {
    const video = media();
    if (!video) return false;
    video.pause();
    return true;
  }

  async function playPause() {
    return isPlaying() ? pause() : play();
  }

  function next() {
    const button = firstMatch(NEXT_SELECTORS);
    if (!button) {
      console.warn('[MacroDeck YTM] next control not found');
      return false;
    }
    button.click();
    return true;
  }

  function previous() {
    const button = firstMatch(PREVIOUS_SELECTORS);
    if (!button) {
      console.warn('[MacroDeck YTM] previous control not found');
      return false;
    }
    button.click();
    return true;
  }

  return { media, isPlaying, play, pause, playPause, next, previous };
})();
