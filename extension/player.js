/*
 * Everything that knows about YouTube Music's page structure lives here.
 *
 * Playback, volume and mute act on the media element, which is stable. Shuffle
 * and repeat have no media-element equivalent and must read and click the
 * player controls, so those selectors are the only fragile part of the
 * extension. When YouTube Music changes its markup, this file is the only one
 * that needs editing — and playback, volume and mute keep working even when the
 * fragile parts go stale.
 *
 * Loaded as a plain content script, so it publishes one global into the
 * isolated world rather than exporting a module.
 */

const YTMPlayer = (() => {
  const LOG = '[MacroDeck YTM]';

  // How much one Volume Up or Volume Down press moves the volume.
  const VOLUME_STEP = 0.05;

  // Ordered by preference. The first selector that matches a visible element wins.
  //
  // YouTube Music used tp-yt-paper-icon-button and now uses yt-icon-button, so
  // both are listed before the bare class name.
  const NEXT_SELECTORS = [
    'ytmusic-player-bar yt-icon-button.next-button',
    'ytmusic-player-bar tp-yt-paper-icon-button.next-button',
    'ytmusic-player-bar .next-button',
    '.ytmusic-player-bar .next-button',
  ];

  const PREVIOUS_SELECTORS = [
    'ytmusic-player-bar yt-icon-button.previous-button',
    'ytmusic-player-bar tp-yt-paper-icon-button.previous-button',
    'ytmusic-player-bar .previous-button',
    '.ytmusic-player-bar .previous-button',
  ];

  const SHUFFLE_SELECTORS = [
    'ytmusic-player-bar yt-icon-button.shuffle',
    'ytmusic-player-bar tp-yt-paper-icon-button.shuffle',
    'ytmusic-player-bar .shuffle',
    '.ytmusic-player-bar .shuffle',
  ];

  const REPEAT_SELECTORS = [
    'ytmusic-player-bar yt-icon-button.repeat',
    'ytmusic-player-bar tp-yt-paper-icon-button.repeat',
    'ytmusic-player-bar .repeat',
    '.ytmusic-player-bar .repeat',
  ];

  const PLAYER_BAR_SELECTORS = ['ytmusic-player-bar', '.ytmusic-player-bar'];

  /** The three repeat modes YouTube Music cycles through, in cycle order. */
  const REPEAT_MODES = ['off', 'all', 'one'];

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

  function clickControl(selectors, what) {
    const button = firstMatch(selectors);
    if (!button) {
      console.warn(LOG, `${what} control not found`);
      return false;
    }
    button.click();
    return true;
  }

  /* ----------------------------------------------------------- playback */

  async function play() {
    const video = media();
    if (!video) return false;
    try {
      await video.play();
      return true;
    } catch (error) {
      console.warn(LOG, 'play() rejected', error);
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

  const next = () => clickControl(NEXT_SELECTORS, 'next');
  const previous = () => clickControl(PREVIOUS_SELECTORS, 'previous');

  /* ------------------------------------------------------- volume, mute */

  /** Volume as 0-100, or null when there is no player yet. */
  function volume() {
    const video = media();
    if (!video) return null;
    return Math.round(video.volume * 100);
  }

  function isMuted() {
    const video = media();
    return video ? video.muted === true : false;
  }

  function setVolume(fraction) {
    const video = media();
    if (!video) return false;

    video.volume = Math.min(1, Math.max(0, fraction));

    // Raising the volume on a muted player should be audible, which is what
    // every other media control does.
    if (video.volume > 0 && video.muted) video.muted = false;

    return true;
  }

  function volumeUp() {
    const video = media();
    return video ? setVolume(video.volume + VOLUME_STEP) : false;
  }

  function volumeDown() {
    const video = media();
    return video ? setVolume(video.volume - VOLUME_STEP) : false;
  }

  function muteToggle() {
    const video = media();
    if (!video) return false;
    video.muted = !video.muted;
    return true;
  }

  /* ---------------------------------------------------- shuffle, repeat */

  function playerBar() {
    for (const selector of PLAYER_BAR_SELECTORS) {
      const bar = document.querySelector(selector);
      if (bar) return bar;
    }
    return null;
  }

  /**
   * True when shuffle is on, false when off, null when it cannot be read.
   *
   * The state lives on the player bar as a bare `shuffle-on` attribute, not on
   * the button: the shuffle button carries no aria-pressed and its title stays
   * "Shuffle" in both states. Verified against the live page 2026-09-21.
   */
  function shuffleState() {
    const bar = playerBar();
    if (bar) return bar.hasAttribute('shuffle-on');

    // Older markup exposed the state on the button itself.
    const button = firstMatch(SHUFFLE_SELECTORS);
    if (!button) return null;

    const pressed = button.getAttribute('aria-pressed');
    if (pressed === 'true') return true;
    if (pressed === 'false') return false;

    if (button.classList.contains('active') || button.hasAttribute('active')) return true;

    return null;
  }

  const shuffle = () => clickControl(SHUFFLE_SELECTORS, 'shuffle');

  /**
   * 'off', 'all', 'one', or null when the mode cannot be read.
   *
   * The player bar's `repeat-mode` attribute reads NONE, ALL or ONE. Verified
   * against the live page 2026-09-21.
   */
  function repeatState() {
    const bar = playerBar();
    const attribute = bar?.getAttribute('repeat-mode') || bar?.getAttribute('repeat-mode_');

    if (attribute) {
      const mode = attribute.toUpperCase();
      if (mode.includes('NONE') || mode.includes('OFF')) return 'off';
      if (mode.includes('ALL')) return 'all';
      if (mode.includes('ONE')) return 'one';
    }

    // Fall back to the button's label, which names the mode currently in
    // effect — "Repeat off", "Repeat all", "Repeat one".
    const button = firstMatch(REPEAT_SELECTORS);
    const label = (
      button?.getAttribute('title') ||
      button?.getAttribute('label') ||
      button?.getAttribute('aria-label') ||
      ''
    ).toLowerCase();

    if (label.includes('repeat one')) return 'one';
    if (label.includes('repeat all')) return 'all';
    if (label.includes('repeat off') || label.includes('no repeat')) return 'off';

    return null;
  }

  const repeatCycle = () => clickControl(REPEAT_SELECTORS, 'repeat');

  /**
   * Clicks repeat until the requested mode is reached. The cycle is three long,
   * so two clicks always suffice; the loop stops as soon as the mode matches.
   */
  function setRepeat(target) {
    if (!REPEAT_MODES.includes(target)) return false;

    const current = repeatState();
    if (current === null) {
      // Without a readable mode there is no way to know when to stop clicking.
      console.warn(LOG, `cannot set repeat to '${target}': current mode unreadable`);
      return false;
    }

    let mode = current;
    for (let clicks = 0; clicks < REPEAT_MODES.length && mode !== target; clicks++) {
      if (!repeatCycle()) return false;
      mode = REPEAT_MODES[(REPEAT_MODES.indexOf(mode) + 1) % REPEAT_MODES.length];
    }

    return true;
  }

  return {
    media,
    isPlaying,
    play,
    pause,
    playPause,
    next,
    previous,
    volume,
    isMuted,
    volumeUp,
    volumeDown,
    muteToggle,
    shuffle,
    shuffleState,
    repeatCycle,
    repeatState,
    setRepeat,
  };
})();
